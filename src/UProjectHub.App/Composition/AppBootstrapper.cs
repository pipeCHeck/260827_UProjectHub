using System.IO;
using System.Windows;
using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.App.Views;
using UProjectHub.Core.Activity;
using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Discovery;
using UProjectHub.Core.Engines;
using UProjectHub.Core.Filtering;
using UProjectHub.Core.Models;
using UProjectHub.Core.Parsing;
using UProjectHub.Core.Searching;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;
using UProjectHub.Core.Storage;
using UProjectHub.Core.Time;
using UProjectHub.Windows.Launching;
using UProjectHub.Windows.Engines.Manual;
using UProjectHub.Windows.Registry;
using UProjectHub.Windows.Storage;
using AppThemeMode = UProjectHub.Core.Settings.ThemeMode;

namespace UProjectHub.App.Composition;

public sealed class AppBootstrapper
{
    public MainViewModel Build()
    {
        var statusBar = new StatusBarViewModel();
        var applicationResources = Application.Current?.Resources
            ?? new ResourceDictionary();
        var registryReader = new WindowsRegistryReader();
        var themeService = new ThemeService(
            applicationResources,
            () => ResolveSystemTheme(registryReader));
        themeService.ApplySettings(new AppSettings());

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
        var catalog = new ProjectCatalog();
        var removalService = new ManagedProjectRemovalService(
            catalog,
            projectCacheRepository,
            settingsRepository);
        var processLauncher = new ProcessLauncher();
        var projectActions = new ProjectActionService(
            catalog,
            settingsRepository,
            removalService,
            new UnrealEditorLauncher(processLauncher, clock),
            new ExplorerLauncher(processLauncher),
            new VisualStudioLauncher(processLauncher),
            new WpfClipboardService(),
            _ => EngineResolver.Resolve(
                rawAssociation: null,
                Array.Empty<InstalledEngine>()));
        var projectList = new ProjectListViewModel(project =>
            new ProjectContextActionsViewModel(
                project,
                projectActions,
                ShowProjectInformation));
        var searchService = new ProjectSearchService(clock);
        var searchFilter = new SearchFilterViewModel(
            projectList,
            new ProjectQueryParser(),
            new ProjectFilterService(searchService),
            new ProjectSortService());

        var discoveryService = new ProjectDiscoveryService(
            new ProjectRootScanner(new SystemProjectDirectoryEnumerator()),
            new ProjectMetadataLoader(
                new UProjectParser(),
                new ProjectActivityDetector(new ProjectActivityPolicy())));
        var rescanService = new ProjectRescanService(
            catalog,
            discoveryService,
            projectCacheRepository);
        var projectOperations = new ProjectOperations(
            settingsRepository,
            new ManualEngineValidator(),
            themeService,
            catalog,
            (roots, settings, cancellationToken) => rescanService.RescanAsync(
                roots,
                settings,
                cancellationToken: cancellationToken));

        MainViewModel? mainViewModel = null;
        void ShowSettings()
        {
            var settingsViewModel = new SettingsViewModel(
                projectOperations,
                new FolderPickerService(),
                settings => mainViewModel!.ApplySettings(settings),
                snapshot => mainViewModel!.SetProjects(snapshot));
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
            projectActions: projectActions);

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

        return mainViewModel;
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

    private static void ShowProjectInformation(
        ProjectInformationViewModel viewModel)
    {
        var window = new ProjectInformationWindow(viewModel)
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
}
