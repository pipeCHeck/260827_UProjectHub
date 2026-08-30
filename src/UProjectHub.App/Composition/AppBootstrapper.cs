using System.IO;
using System.Security;
using System.Windows;
using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.App.Views;
using UProjectHub.Core.Activity;
using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Discovery;
using UProjectHub.Core.Filtering;
using UProjectHub.Core.Parsing;
using UProjectHub.Core.Searching;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;
using UProjectHub.Core.Storage;
using UProjectHub.Core.Time;
using UProjectHub.Windows.Engines;
using UProjectHub.Windows.Engines.Launcher;
using UProjectHub.Windows.Engines.Manual;
using UProjectHub.Windows.Engines.SourceBuild;
using UProjectHub.Windows.Launching;
using UProjectHub.Windows.Logging;
using UProjectHub.Windows.Projects;
using UProjectHub.Windows.Registry;
using UProjectHub.Windows.Storage;
using AppThemeMode = UProjectHub.Core.Settings.ThemeMode;

namespace UProjectHub.App.Composition;

public sealed record AppRuntime(
    MainViewModel MainViewModel,
    ApplicationCoordinator Coordinator,
    MotionService MotionService);

public sealed class AppBootstrapper
{
    public AppRuntime Build()
    {
        var applicationResources = Application.Current?.Resources
            ?? new ResourceDictionary();
        var registryReader = new WindowsRegistryReader();
        var themeService = new ThemeService(
            applicationResources,
            () => ResolveSystemTheme(registryReader));
        var localizationService = new LocalizationService(applicationResources);
        applicationResources["Service.Localization"] = localizationService;
        localizationService.ApplySettings(new AppSettings());
        themeService.ApplySettings(new AppSettings());
        var statusBar = new StatusBarViewModel(localizationService);

        var animationPreference = new WpfSystemAnimationPreference();
        var motionService = new MotionService(
            applicationResources,
            animationPreference);
        statusBar.SetAnimationsEnabled(motionService.AreAnimationsEnabled);
        motionService.PreferenceChanged += (_, _) =>
            statusBar.SetAnimationsEnabled(motionService.AreAnimationsEnabled);

        var clock = new SystemClock();
        var paths = new LocalAppDataPathProvider().GetPaths();
        var writer = new AtomicJsonFileWriter();
        var settingsRepository = new JsonSettingsRepository(
            paths.SettingsFile,
            writer);
        var projectCacheRepository = new JsonProjectCacheRepository(
            paths.ProjectCacheFile,
            writer);
        var engineCacheRepository = new JsonEngineCacheRepository(
            paths.EngineCacheFile,
            writer);
        IAppLogger logger = new RollingFileLogger(
            paths.LogFile,
            LogRetentionPolicy.Default,
            clock);

        var catalog = new ProjectCatalog();
        var currentEngines = new CurrentEngineSnapshot();
        var removalService = new ManagedProjectRemovalService(
            catalog,
            projectCacheRepository,
            settingsRepository);
        var processLauncher = new ProcessLauncher();
        var unrealEditorLauncher = new UnrealEditorLauncher(processLauncher, clock);
        var solutionLocator = new VisualStudioSolutionLocator();
        var projectFilesGenerator = new UnrealProjectFilesGenerator(
            new ExternalProcessRunner(),
            solutionLocator);
        var projectActions = new ProjectActionService(
            catalog,
            settingsRepository,
            removalService,
            unrealEditorLauncher,
            new ExplorerLauncher(processLauncher),
            new VisualStudioLauncher(processLauncher, solutionLocator),
            new WpfClipboardService(),
            currentEngines.Resolve,
            logger,
            projectFilesGenerator);
        var diagnosticStore = new ProjectDiagnosticSnapshotStore(
            new ProjectDiagnosticsService(
                new BasicProjectDiagnosticsService(clock),
                solutionLocator,
                project => projectActions
                    .PrepareProjectFileGeneration(project)
                    .CanGenerate));
        var projectList = new ProjectListViewModel(project =>
            new ProjectContextActionsViewModel(
                project,
                projectActions,
                ShowProjectDetails,
                ShowGenerateProjectFiles,
                localizationService,
                diagnosticStore),
            localizationService,
            diagnosticStore);
        var searchService = new ProjectSearchService(clock);
        var searchFilter = new SearchFilterViewModel(
            projectList,
            new ProjectQueryParser(),
            new ProjectFilterService(searchService),
            new ProjectSortService(),
            localizationService);
        var newProject = new NewProjectViewModel(
            unrealEditorLauncher,
            statusBar,
            localizationService);

        var metadataLoader = new ProjectMetadataLoader(
            new UProjectParser(),
            new ProjectActivityDetector(new ProjectActivityPolicy()));
        var discoveryService = new ProjectDiscoveryService(
            new ProjectRootScanner(new SystemProjectDirectoryEnumerator()),
            metadataLoader);
        var bestEffortProjectCache = new BestEffortProjectCacheRepository(
            projectCacheRepository,
            logger);
        var refreshService = new ProjectRefreshService(
            catalog,
            metadataLoader,
            bestEffortProjectCache);
        var rescanService = new ProjectRescanService(
            catalog,
            discoveryService,
            bestEffortProjectCache);

        var dispatcher = new WpfUiDispatcher(
            Application.Current?.Dispatcher
                ?? System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var manualEngineValidator = new ManualEngineValidator();

        ApplicationCoordinator? coordinator = null;
        ProjectOperations? projectOperations = null;
        MainViewModel? mainViewModel = null;

        void ShowSettings()
        {
            var settingsViewModel = new SettingsViewModel(
                projectOperations!,
                new FolderPickerService(),
                settings => mainViewModel!.ApplySettings(settings),
                snapshot => mainViewModel!.SetProjects(snapshot),
                localizationService);
            var window = new SettingsWindow(settingsViewModel)
            {
                Owner = Application.Current?.MainWindow,
            };
            _ = window.ShowDialog();
        }

        mainViewModel = new MainViewModel(
            statusBar,
            settingsAction: ShowSettings,
            projectList: projectList,
            searchFilter: searchFilter,
            projectActions: projectActions,
            refreshAction: () => coordinator!.RefreshAsync(),
            newProject: newProject,
            localization: localizationService);

        var backgroundRefresh = new BackgroundRefreshService(
            catalog,
            currentEngines,
            (settings, progress, cancellationToken) =>
                refreshService.RefreshKnownAsync(
                    settings,
                    progress,
                    cancellationToken),
            (roots, settings, progress, cancellationToken) =>
                rescanService.RescanAsync(
                    roots,
                    settings,
                    progress,
                    cancellationToken),
            (roots, settings, excludedProjects, cancellationToken, projectLoaded) =>
                discoveryService.DiscoverShallowAsync(
                    roots,
                    settings,
                    excludedProjects,
                    cancellationToken,
                    projectLoaded),
            (settings, cancellationToken) => DiscoverEnginesAsync(
                settings,
                registryReader,
                manualEngineValidator,
                cancellationToken),
            new UnrealKnownProjectRootProvider(),
            dispatcher,
            mainViewModel.SetProjects);

        coordinator = new ApplicationCoordinator(
            settingsRepository,
            projectCacheRepository,
            engineCacheRepository,
            catalog,
            currentEngines,
            themeService,
            mainViewModel,
            statusBar,
            backgroundRefresh,
            dispatcher,
            logger,
            localizationService,
            diagnosticStore);

        projectOperations = new ProjectOperations(
            settingsRepository,
            manualEngineValidator,
            themeService,
            localizationService,
            catalog,
            (roots, settings, cancellationToken) => rescanService.RescanAsync(
                roots,
                settings,
                cancellationToken: cancellationToken),
            coordinator.RescanAsync);

        var saveGate = new object();
        Task saveTail = Task.CompletedTask;
        searchFilter.PersistedStateChanged += (_, _) =>
        {
            var activeSort = searchFilter.ActiveSort;
            var visibleFilters = searchFilter.VisibleFilters;
            lock (saveGate)
            {
                saveTail = SaveViewStateAfterAsync(
                    saveTail,
                    projectOperations,
                    activeSort,
                    visibleFilters);
            }
        };

        return new AppRuntime(mainViewModel, coordinator, motionService);
    }

    private static Task<EngineDiscoveryResult> DiscoverEnginesAsync(
        AppSettings settings,
        IRegistryReader registryReader,
        ManualEngineValidator manualEngineValidator,
        CancellationToken cancellationToken)
    {
        var discovery = new EngineDiscoveryService(
        [
            new LauncherEngineProvider(),
            new SourceBuildEngineProvider(registryReader),
            new ManualEngineProvider(settings, manualEngineValidator),
        ]);
        return discovery.DiscoverAsync(cancellationToken);
    }

    private static AppThemeMode ResolveSystemTheme(IRegistryReader registryReader)
    {
        const string personalizeKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        var value = registryReader
            .ReadCurrentUserValues(personalizeKey)
            .FirstOrDefault(entry => string.Equals(
                entry.Name,
                "AppsUseLightTheme",
                StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return value is int appsUseLightTheme && appsUseLightTheme == 0
            ? AppThemeMode.Dark
            : AppThemeMode.Light;
    }

    private static void ShowProjectDetails(
        ProjectDetailsViewModel viewModel)
    {
        var window = new ProjectDetailsWindow(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };
        try
        {
            _ = window.ShowDialog();
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    private static void ShowGenerateProjectFiles(
        GenerateProjectFilesViewModel viewModel)
    {
        var window = new GenerateProjectFilesWindow(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };
        _ = window.ShowDialog();
    }

    private static async Task SaveViewStateAfterAsync(
        Task previousSave,
        IProjectOperations operations,
        ProjectSortDefinition activeSort,
        VisibleFilterState visibleFilters)
    {
        try
        {
            await previousSave;
        }
        catch (IOException)
        {
            // The next state still gets a chance to persist.
        }

        _ = await operations.SaveViewStateAsync(
            activeSort,
            visibleFilters,
            columnLayout: null);
    }

    private sealed class BestEffortProjectCacheRepository(
        IProjectCacheRepository inner,
        IAppLogger logger) : IProjectCacheRepository
    {
        public Task<ProjectCacheDocument> LoadAsync(
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(cancellationToken);

        public async Task SaveAsync(
            ProjectCacheDocument document,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await inner.SaveAsync(document, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or SecurityException)
            {
                logger.Error("Intermediate project cache save failed.", exception);
            }
        }
    }
}
