using System.Windows;
using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Discovery;
using UProjectHub.Core.Engines;
using UProjectHub.Core.Filtering;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Searching;
using UProjectHub.Core.Settings;
using UProjectHub.Windows.SourceControl;
using UProjectHub.Core.Sorting;
using UProjectHub.Core.Time;
using UProjectHub.Windows.Engines;
using UProjectHub.Windows.Projects;
using UProjectHub.Windows.Engines.Manual;
using UProjectHub.Windows.Launching;
using AppThemeMode = UProjectHub.Core.Settings.ThemeMode;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ApplicationCoordinatorTests
{
    [TestMethod]
    public async Task MainViewModelRefreshCommand_InvokesOnlyTheInjectedRefreshCallback()
    {
        var calls = 0;
        var main = new MainViewModel(
            new StatusBarViewModel(),
            refreshAction: () =>
            {
                calls++;
                return Task.CompletedTask;
            });

        await main.RefreshCommand.ExecuteAsync();

        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task MainViewModelRefreshCommand_DoesNotLeakNormalCancellationAsync()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var main = new MainViewModel(
            new StatusBarViewModel(),
            refreshAction: () => Task.FromCanceled(cancellation.Token));

        await main.RefreshCommand.ExecuteAsync();

        Assert.IsFalse(main.RefreshCommand.IsExecuting);
    }

    [TestMethod]
    public async Task Start_LoadsSettingsThenCachesPublishesRowsBeforeBackgroundRefreshAndDoesNotRescan()
    {
        var order = new List<string>();
        var settings = CreateSettings();
        var project = CreateCacheEntry(ProjectState.Missing);
        var engine = CreateEngineEntry("5.8", @"C:\CachedUE58");
        var fixture = CreateFixture(settings, [project], [engine], order);
        fixture.BlockRefresh = true;

        var start = fixture.Coordinator.StartAsync();
        await fixture.RefreshStarted.Task;

        Assert.IsTrue(start.IsCompletedSuccessfully);
        await start;
        CollectionAssert.AreEqual(
            new[] { "settings", "project-cache", "engine-cache", "publish", "refresh" },
            order.Take(5).ToArray());
        Assert.AreEqual(1, fixture.Main.ProjectCount);
        Assert.AreEqual(ProjectState.Missing, fixture.Main.ProjectList.Rows.Single().ProjectState);
        Assert.HasCount(2, fixture.Main.NewProject!.EngineOptions);
        Assert.AreEqual("Unreal Engine 5.8", fixture.Main.NewProject.EngineOptions[1].Label);
        Assert.IsNull(fixture.Main.NewProject.SelectedEngine);
        Assert.AreEqual(0, fixture.RescanCalls);

        fixture.ReleaseRefresh.TrySetResult();
        await fixture.EngineCache.Saved.Task;
        Assert.AreEqual(1, fixture.LightweightDiscoveryCalls);
        await fixture.Coordinator.StopAsync();
    }

    [TestMethod]
    public async Task Start_CachedCppRowsPublishBeforeDiagnosticsAndRefreshCalculatesThemOnce()
    {
        var cachedEngine = CreateEngineEntry("5.8", @"C:\CachedUE58");
        var freshEngine = CreateEngine("5.8", @"D:\FreshUE58");
        var fixture = CreateFixture(
            CreateSettings(),
            [CreateCacheEntry(ProjectState.Available, projectType: ProjectType.Cpp)],
            [cachedEngine]);
        fixture.FreshEngines = [freshEngine];
        fixture.BlockRefresh = true;

        var start = fixture.Coordinator.StartAsync();
        await fixture.RefreshStarted.Task;

        Assert.IsTrue(start.IsCompletedSuccessfully);
        await start;
        Assert.HasCount(1, fixture.Main.ProjectList.Rows);
        Assert.AreEqual(0, fixture.SolutionLocator.LocateCount);
        Assert.IsNull(fixture.Main.ProjectList.Rows.Single().DiagnosticReport);

        fixture.ReleaseRefresh.TrySetResult();
        await fixture.EngineCache.Saved.Task;
        await WaitForOperationReadyAsync(fixture.Status);

        Assert.AreEqual(1, fixture.SolutionLocator.LocateCount);
        Assert.AreEqual(
            ProjectDiagnosticSeverity.Info,
            fixture.Main.ProjectList.Rows.Single().DiagnosticSeverity);
        await fixture.Coordinator.StopAsync();
    }

    [TestMethod]
    public async Task Start_MergesUserStateUsesCachedEnginesAndAppliesPersistedPresentation()
    {
        var launched = new DateTimeOffset(2026, 8, 20, 4, 5, 6, TimeSpan.Zero);
        var path = new ProjectPath(@"C:\Cached\Game.uproject");
        var settings = CreateSettings() with
        {
            ThemeMode = AppThemeMode.Dark,
            RowDensity = RowDensity.Compact,
            Language = AppLanguage.Korean,
            VisibleFilters = new VisibleFilterState(null, null, true),
            ActiveSort = new ProjectSortDefinition(ProjectSortColumn.Name, SortDirection.Ascending),
            ColumnLayout = [new ColumnLayoutState("ProjectType", false, 111)],
            ProjectUserStates =
            [
                new ProjectUserState(path, true, launched)
                {
                    Tags = ["Pinned", "Client"],
                    Note = "Cached metadata",
                },
            ],
        };
        var fixture = CreateFixture(
            settings,
            [CreateCacheEntry(ProjectState.Available, path)],
            [CreateEngineEntry("5.8", @"C:\CachedUE58")]);
        fixture.BlockRefresh = true;

        await fixture.Coordinator.StartAsync();
        await fixture.RefreshStarted.Task;

        var cached = fixture.Catalog.GetSnapshot().Projects.Single();
        Assert.IsTrue(cached.IsFavorite);
        Assert.AreEqual(launched, cached.LastLaunched);
        CollectionAssert.AreEqual(new[] { "Pinned", "Client" }, cached.Tags.ToArray());
        Assert.AreEqual("Cached metadata", cached.Note);
        Assert.AreEqual(EngineResolutionState.Resolved, cached.EngineState);
        Assert.AreEqual(AppThemeMode.Dark, fixture.Theme.EffectiveTheme);
        Assert.AreEqual(RowDensity.Compact, fixture.Theme.ActiveDensity);
        Assert.AreEqual(AppLanguage.Korean, fixture.Localization.CurrentLanguage);
        Assert.AreEqual("Unreal 프로젝트", fixture.Main.Title);
        Assert.IsTrue(fixture.Main.SearchFilter!.FavoritesOnly);
        Assert.AreEqual(settings.ActiveSort, fixture.Main.SearchFilter.ActiveSort);
        Assert.AreEqual(settings.ColumnLayout.Single(), fixture.Main.ProjectList.ColumnLayout.Single());
        fixture.ReleaseRefresh.TrySetResult();
        await fixture.Coordinator.StopAsync();
    }

    [TestMethod]
    public async Task BackgroundCompletion_ReplacesCachedEnginesAndSavesPostResolutionCaches()
    {
        var cachedEngine = CreateEngineEntry("5.7", @"C:\CachedUE57");
        var freshEngine = CreateEngine("5.8", @"D:\FreshUE58");
        var fixture = CreateFixture(
            CreateSettings(),
            [CreateCacheEntry(ProjectState.Available)],
            [cachedEngine]);
        fixture.FreshEngines = [freshEngine];

        await fixture.Coordinator.StartAsync();
        await fixture.EngineCache.Saved.Task;

        Assert.AreEqual(freshEngine, fixture.Engines.Engines.Single());
        Assert.HasCount(2, fixture.Main.NewProject!.EngineOptions);
        Assert.AreEqual(
            freshEngine.EditorPath,
            fixture.Main.NewProject.EngineOptions[1].Engine!.EditorPath);
        Assert.IsFalse(fixture.Main.NewProject.EngineOptions.Any(option =>
            string.Equals(
                option.Engine?.EditorPath,
                cachedEngine.EditorPath,
                StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(
            EngineResolutionState.Resolved,
            fixture.ProjectCache.LastSaved!.Projects.Single().EngineState);
        Assert.AreEqual("5.8", fixture.ProjectCache.LastSaved.Projects.Single().EngineDisplayVersion);
        Assert.AreEqual(freshEngine.EditorPath, fixture.EngineCache.LastSaved!.Engines.Single().EditorPath);
        await fixture.Coordinator.StopAsync();
    }

    [TestMethod]
    public async Task DiagnosticCancellationDoesNotDiscardCompletedRefreshCachesAsync()
    {
        var fixture = CreateFixture(
            CreateSettings(),
            [
                CreateCacheEntry(
                    ProjectState.Available,
                    new ProjectPath(@"C:\Cached\First.uproject"),
                    ProjectType.Cpp),
                CreateCacheEntry(
                    ProjectState.Available,
                    new ProjectPath(@"C:\Cached\Second.uproject"),
                    ProjectType.Cpp),
            ],
            [CreateEngineEntry("5.8", @"C:\CachedUE58")]);
        fixture.FreshEngines = [CreateEngine("5.8", @"D:\FreshUE58")];
        await fixture.Coordinator.StartAsync();
        await fixture.EngineCache.Saved.Task;
        await WaitForOperationReadyAsync(fixture.Status);
        var projectSaveCount = fixture.ProjectCache.SaveCount;
        var engineSaveCount = fixture.EngineCache.SaveCount;
        using var cancellation = new CancellationTokenSource();
        fixture.SolutionLocator.OnLocate = cancellation.Cancel;

        var refreshed = await fixture.Coordinator.RefreshAsync(
            cancellation.Token);

        Assert.IsTrue(refreshed);
        Assert.AreEqual(projectSaveCount + 1, fixture.ProjectCache.SaveCount);
        Assert.AreEqual(engineSaveCount + 1, fixture.EngineCache.SaveCount);
        Assert.IsNotNull(fixture.ProjectCache.LastSaved);
        Assert.IsNotNull(fixture.EngineCache.LastSaved);
        await fixture.Coordinator.StopAsync();
    }

    [TestMethod]
    public async Task DiagnosticFailureDoesNotDiscardCompletedRefreshCachesAsync()
    {
        var fixture = CreateFixture(
            CreateSettings(),
            [CreateCacheEntry(ProjectState.Available)],
            [CreateEngineEntry("5.8", @"C:\CachedUE58")]);
        await fixture.Coordinator.StartAsync();
        await fixture.EngineCache.Saved.Task;
        await WaitForOperationReadyAsync(fixture.Status);
        var projectSaveCount = fixture.ProjectCache.SaveCount;
        var engineSaveCount = fixture.EngineCache.SaveCount;
        fixture.DiagnosticStore.SnapshotChanged += (_, _) =>
            throw new IOException("diagnostic presentation failed");

        var refreshed = await fixture.Coordinator.RefreshAsync();

        Assert.IsTrue(refreshed);
        Assert.AreEqual(projectSaveCount + 1, fixture.ProjectCache.SaveCount);
        Assert.AreEqual(engineSaveCount + 1, fixture.EngineCache.SaveCount);
        Assert.IsTrue(fixture.Logger.Messages.Any(message =>
            message.Contains(
                "Diagnostics refresh failed",
                StringComparison.Ordinal)));
        await fixture.Coordinator.StopAsync();
    }

    [TestMethod]
    public async Task StartupLightweightDiscoveryAddsNewProjectToFinalCache()
    {
        var fixture = CreateFixture(CreateSettings(), [], []);
        fixture.LightweightProjects =
        [
            new UnrealProject(
                "Discovered",
                new ProjectPath(@"C:\Configured\Discovered\Discovered.uproject"),
                "5.8",
                "5.8",
                ProjectType.Blueprint,
                new DateTimeOffset(2026, 8, 28, 1, 0, 0, TimeSpan.Zero),
                null,
                false,
                ProjectState.Available,
                EngineResolutionState.Unknown),
        ];

        await fixture.Coordinator.StartAsync();
        await fixture.EngineCache.Saved.Task;

        Assert.AreEqual(1, fixture.LightweightDiscoveryCalls);
        Assert.IsTrue(fixture.ProjectCache.LastSaved!.Projects.Any(entry => entry.Name == "Discovered"));
        await fixture.Coordinator.StopAsync();
    }

    [TestMethod]
    public async Task CacheSaveFailure_KeepsPublishedStateAndLogsFailure()
    {
        var fixture = CreateFixture(
            CreateSettings(),
            [CreateCacheEntry(ProjectState.Available)],
            []);
        fixture.ProjectCache.SaveException = new IOException("cache unavailable");

        await fixture.Coordinator.StartAsync();
        await fixture.EngineCache.Saved.Task;

        Assert.AreEqual(1, fixture.Main.ProjectCount);
        Assert.IsTrue(fixture.Logger.Messages.Any(message =>
            message.Contains("project cache", StringComparison.OrdinalIgnoreCase)));
        await fixture.Coordinator.StopAsync();
    }

    [TestMethod]
    public async Task F5RefreshNeverCallsRescanAndExplicitRescanUsesCurrentPersistedRoots()
    {
        var fixture = CreateFixture(CreateSettings(), [], []);
        await fixture.Coordinator.StartAsync();
        await fixture.EngineCache.Saved.Task;
        await WaitForOperationReadyAsync(fixture.Status);
        fixture.ResetOperationCounts();

        var refreshed = await fixture.Coordinator.RefreshAsync();
        var rescanned = await fixture.Coordinator.RescanAsync();

        Assert.IsTrue(refreshed);
        Assert.IsTrue(rescanned.IsSuccess);
        Assert.AreEqual(1, fixture.RefreshCalls);
        Assert.AreEqual(1, fixture.RescanCalls);
        Assert.AreEqual(0, fixture.LightweightDiscoveryCalls);
        CollectionAssert.AreEqual(
            CreateSettings().ProjectSearchRoots.ToArray(),
            fixture.LastRescanRoots!.ToArray());
        await fixture.Coordinator.StopAsync();
    }

    [TestMethod]
    public async Task SuccessfulF5RefreshRevalidatesGitOnceAfterFinalSnapshotAsync()
    {
        var fixture = CreateFixture(
            CreateSettings(),
            [CreateCacheEntry(ProjectState.Available)],
            [],
            includeGitStatuses: true);
        await fixture.Coordinator.StartAsync();
        await fixture.EngineCache.Saved.Task;
        await WaitForOperationReadyAsync(fixture.Status);
        await fixture.Git!.WaitForCallCountAsync(1);
        var callsBeforeF5 = fixture.Git.CallCount;

        var refreshed = await fixture.Coordinator.RefreshAsync();
        await fixture.Git.WaitForCallCountAsync(callsBeforeF5 + 1);

        Assert.IsTrue(refreshed);
        Assert.AreEqual(callsBeforeF5 + 1, fixture.Git.CallCount);
        await fixture.Coordinator.StopAsync();
    }

    [TestMethod]
    public async Task ProjectOperations_UsesApplicationCoordinatorForExplicitRescan()
    {
        var fixture = CreateFixture(CreateSettings(), [], []);
        await fixture.Coordinator.StartAsync();
        await fixture.EngineCache.Saved.Task;
        await WaitForOperationReadyAsync(fixture.Status);
        fixture.ResetOperationCounts();
        var legacyRescanCalls = 0;
        var operations = new ProjectOperations(
            new SettingsMutationService(fixture.Settings),
            new ManualEngineValidator(),
            fixture.Theme,
            new LocalizationService(
                new ResourceDictionary(),
                source => new ResourceDictionary
                {
                    ["Test.Source"] = source.OriginalString,
                }),
            fixture.Catalog,
            (roots, settings, cancellationToken) =>
            {
                legacyRescanCalls++;
                return Task.FromResult(new ProjectRefreshResult([], []));
            },
            coordinatedRescan: fixture.Coordinator.RescanAsync);

        var result = await operations.RescanAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, fixture.RescanCalls);
        Assert.AreEqual(0, legacyRescanCalls);
        await fixture.Coordinator.StopAsync();
    }

    [TestMethod]
    public async Task OverlappingOperationsAreRejectedAndStatusAlwaysReturnsToReady()
    {
        var fixture = CreateFixture(CreateSettings(), [], []);
        fixture.BlockRefresh = true;
        await fixture.Coordinator.StartAsync();
        await fixture.RefreshStarted.Task;

        var refreshAccepted = await fixture.Coordinator.RefreshAsync();
        var rescan = await fixture.Coordinator.RescanAsync();

        Assert.IsFalse(refreshAccepted);
        Assert.IsFalse(rescan.IsSuccess);
        Assert.IsTrue(fixture.Status.IsOperationActive);
        fixture.ReleaseRefresh.TrySetResult();
        await fixture.Coordinator.StopAsync();
        Assert.IsFalse(fixture.Status.IsOperationActive);
        Assert.AreEqual("Ready", fixture.Status.StatusText);
    }

    [TestMethod]
    public async Task StopCancelsBackgroundRefreshWithoutUnhandledException()
    {
        var fixture = CreateFixture(CreateSettings(), [], []);
        fixture.WaitForCancellation = true;
        await fixture.Coordinator.StartAsync();
        await fixture.RefreshStarted.Task;

        await fixture.Coordinator.StopAsync();

        Assert.IsTrue(fixture.RefreshCancellationObserved);
        Assert.IsFalse(fixture.Status.IsOperationActive);
        Assert.IsTrue(fixture.Logger.Messages.Any(message =>
            message.Contains("cancel", StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task WaitForOperationReadyAsync(
        StatusBarViewModel status)
    {
        if (!status.IsOperationActive)
        {
            return;
        }

        var ready = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        System.ComponentModel.PropertyChangedEventHandler? handler = null;
        handler = (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(StatusBarViewModel.IsOperationActive)
                && !status.IsOperationActive)
            {
                ready.TrySetResult();
            }
        };
        status.PropertyChanged += handler;
        try
        {
            if (!status.IsOperationActive)
            {
                return;
            }

            await ready.Task;
        }
        finally
        {
            status.PropertyChanged -= handler;
        }
    }

    private static Fixture CreateFixture(
        AppSettings settings,
        IReadOnlyList<ProjectCacheEntry> projects,
        IReadOnlyList<EngineCacheEntry> engines,
        List<string>? order = null,
        bool includeGitStatuses = false)
    {
        var settingsRepository = new FakeSettingsRepository(settings, order);
        var projectCache = new FakeProjectCacheRepository(projects, order);
        var engineCache = new FakeEngineCacheRepository(engines, order);
        var catalog = new ProjectCatalog();
        var currentEngines = new CurrentEngineSnapshot();
        var resources = new ResourceDictionary();
        var theme = new ThemeService(
            resources,
            () => AppThemeMode.Light,
            source => new ResourceDictionary { ["Source"] = source.OriginalString });
        var localization = new LocalizationService(
            resources,
            source => new ResourceDictionary
            {
                ["String.AppTitle"] = source.OriginalString.Contains("ko", StringComparison.OrdinalIgnoreCase)
                    ? "Unreal 프로젝트"
                    : "Unreal Projects",
                ["String.StatusReady"] = "Ready",
            });
        var status = new StatusBarViewModel(localization);
        var solutionLocator = new CountingSolutionLocator();
        var diagnosticStore = new ProjectDiagnosticSnapshotStore(
            new ProjectDiagnosticsService(
                new BasicProjectDiagnosticsService(new SystemClock()),
                solutionLocator,
                _ => true));
        var git = includeGitStatuses ? new RecordingGitStatusService() : null;
        var gitStatuses = git is null
            ? null
            : new ProjectGitStatusStore(git, new ImmediateDispatcher());
        var projectList = new ProjectListViewModel(
            localization: localization,
            diagnostics: diagnosticStore,
            gitStatuses: gitStatuses);
        var search = new SearchFilterViewModel(
            projectList,
            new ProjectQueryParser(),
            new ProjectFilterService(new ProjectSearchService(new SystemClock())),
            new ProjectSortService(),
            localization);
        var newProject = new NewProjectViewModel(
            new FakeUnrealEditorLauncher(),
            status,
            localization);
        var main = new MainViewModel(
            status,
            projectList: projectList,
            searchFilter: search,
            newProject: newProject,
            localization: localization);
        var dispatcher = new RecordingDispatcher(order, main);
        var catalogOperationGate = new ProjectCatalogOperationGate();
        var fixture = new Fixture(
            settingsRepository,
            projectCache,
            engineCache,
            catalog,
            currentEngines,
            theme,
            localization,
            status,
            main,
            dispatcher,
            solutionLocator,
            diagnosticStore,
            git);
        var background = new BackgroundRefreshService(
            catalog,
            currentEngines,
            fixture.RefreshAsync,
            fixture.RescanAsync,
            fixture.DiscoverLightweightAsync,
            fixture.DiscoverEnginesAsync,
            new FakeKnownRootProvider(),
            dispatcher,
            main.SetProjects,
            batchSize: 32);
        fixture.Coordinator = new ApplicationCoordinator(
            settingsRepository,
            projectCache,
            engineCache,
            catalog,
            catalogOperationGate,
            currentEngines,
            theme,
            main,
            status,
            background,
            dispatcher,
            fixture.Logger,
            localization,
            diagnosticStore,
            gitStatuses);
        return fixture;
    }

    private static AppSettings CreateSettings() => new()
    {
        ProjectSearchRoots = [@"C:\Configured", @"D:\Projects"],
    };

    private static ProjectCacheEntry CreateCacheEntry(
        ProjectState state,
        ProjectPath? path = null,
        ProjectType projectType = ProjectType.Blueprint) => new(
        path ?? new ProjectPath(@"C:\Cached\Game.uproject"),
        "Game",
        "5.8",
        "5.8",
        projectType,
        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        state,
        EngineResolutionState.Unknown);

    private static EngineCacheEntry CreateEngineEntry(string version, string root) => new(
        $"Unreal Engine {version}",
        version,
        version,
        root,
        Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor.exe"),
        EngineSource.Launcher,
        true);

    private static InstalledEngine CreateEngine(string version, string root) => new(
        $"Unreal Engine {version}",
        version,
        version,
        root,
        Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor.exe"),
        EngineSource.Launcher,
        true);

    private sealed class Fixture(
        FakeSettingsRepository settings,
        FakeProjectCacheRepository projectCache,
        FakeEngineCacheRepository engineCache,
        ProjectCatalog catalog,
        CurrentEngineSnapshot engines,
        ThemeService theme,
        LocalizationService localization,
        StatusBarViewModel status,
        MainViewModel main,
        RecordingDispatcher dispatcher,
        CountingSolutionLocator solutionLocator,
        ProjectDiagnosticSnapshotStore diagnosticStore,
        RecordingGitStatusService? git)
    {
        public ApplicationCoordinator Coordinator { get; set; } = null!;
        public FakeSettingsRepository Settings { get; } = settings;
        public FakeProjectCacheRepository ProjectCache { get; } = projectCache;
        public FakeEngineCacheRepository EngineCache { get; } = engineCache;
        public ProjectCatalog Catalog { get; } = catalog;
        public CurrentEngineSnapshot Engines { get; } = engines;
        public ThemeService Theme { get; } = theme;
        public LocalizationService Localization { get; } = localization;
        public StatusBarViewModel Status { get; } = status;
        public MainViewModel Main { get; } = main;
        public CountingSolutionLocator SolutionLocator { get; } = solutionLocator;
        public ProjectDiagnosticSnapshotStore DiagnosticStore { get; } = diagnosticStore;
        public RecordingGitStatusService? Git { get; } = git;
        public RecordingLogger Logger { get; } = new();
        public int RefreshCalls { get; private set; }
        public int RescanCalls { get; private set; }
        public int LightweightDiscoveryCalls { get; private set; }
        public IReadOnlyList<string>? LastRescanRoots { get; private set; }
        public bool BlockRefresh { get; set; }
        public bool WaitForCancellation { get; set; }
        public bool RefreshCancellationObserved { get; private set; }
        public IReadOnlyList<InstalledEngine> FreshEngines { get; set; } = [];
        public IReadOnlyList<UnrealProject> LightweightProjects { get; set; } = [];
        public TaskCompletionSource RefreshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRefresh { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProjectRefreshResult> RefreshAsync(
            AppSettings appSettings,
            IProgress<ProjectRefreshUpdate>? progress,
            CancellationToken cancellationToken)
        {
            RefreshCalls++;
            dispatcher.Order?.Add("refresh");
            RefreshStarted.TrySetResult();
            if (WaitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    RefreshCancellationObserved = true;
                    throw;
                }
            }

            if (BlockRefresh)
            {
                await ReleaseRefresh.Task.WaitAsync(cancellationToken);
            }

            return new ProjectRefreshResult([], []);
        }

        public Task<ProjectRefreshResult> RescanAsync(
            IReadOnlyList<string> roots,
            AppSettings appSettings,
            IProgress<ProjectRefreshUpdate>? progress,
            CancellationToken cancellationToken)
        {
            RescanCalls++;
            LastRescanRoots = roots;
            return Task.FromResult(new ProjectRefreshResult([], []));
        }

        public Task<EngineDiscoveryResult> DiscoverEnginesAsync(
            AppSettings appSettings,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EngineDiscoveryResult(FreshEngines, []));

        public Task<ProjectDiscoveryResult> DiscoverLightweightAsync(
            IReadOnlyList<string> roots,
            AppSettings appSettings,
            IReadOnlyCollection<ProjectPath> excludedProjectPaths,
            CancellationToken cancellationToken,
            Action<ProjectMetadataLoadResult>? projectLoaded)
        {
            LightweightDiscoveryCalls++;
            foreach (var project in LightweightProjects.Where(project =>
                !excludedProjectPaths.Contains(project.ProjectFilePath)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                projectLoaded?.Invoke(new ProjectMetadataLoadResult(project, null));
            }

            return Task.FromResult(new ProjectDiscoveryResult(LightweightProjects, []));
        }

        public void ResetOperationCounts()
        {
            RefreshCalls = 0;
            RescanCalls = 0;
            LightweightDiscoveryCalls = 0;
        }
    }

    private sealed class RecordingGitStatusService : IGitStatusService
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<GitProjectStatus> GetStatusAsync(
            string projectDirectory,
            bool includeRemotes = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new GitProjectStatus(GitProjectState.Clean));
        }

        public async Task WaitForCallCountAsync(int expected)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (CallCount >= expected)
                {
                    return;
                }

                await Task.Delay(10);
            }

            throw new TimeoutException("The expected Git refresh did not start.");
        }
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsRepository(AppSettings settings, List<string>? order)
        : ISettingsRepository
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            order?.Add("settings");
            return Task.FromResult(settings);
        }

        public Task SaveAsync(AppSettings value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeProjectCacheRepository(
        IReadOnlyList<ProjectCacheEntry> entries,
        List<string>? order) : IProjectCacheRepository
    {
        public Exception? SaveException { get; set; }
        public ProjectCacheDocument? LastSaved { get; private set; }
        public int SaveCount { get; private set; }

        public Task<ProjectCacheDocument> LoadAsync(CancellationToken cancellationToken = default)
        {
            order?.Add("project-cache");
            return Task.FromResult(new ProjectCacheDocument { Projects = entries });
        }

        public Task SaveAsync(ProjectCacheDocument document, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (SaveException is not null)
            {
                return Task.FromException(SaveException);
            }

            LastSaved = document;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEngineCacheRepository(
        IReadOnlyList<EngineCacheEntry> entries,
        List<string>? order) : IEngineCacheRepository
    {
        public EngineCacheDocument? LastSaved { get; private set; }
        public int SaveCount { get; private set; }
        public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<EngineCacheDocument> LoadAsync(CancellationToken cancellationToken = default)
        {
            order?.Add("engine-cache");
            return Task.FromResult(new EngineCacheDocument { Engines = entries });
        }

        public Task SaveAsync(EngineCacheDocument document, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            LastSaved = document;
            Saved.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDispatcher(List<string>? order, MainViewModel main) : IUiDispatcher
    {
        public List<string>? Order { get; } = order;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = main.ProjectCount;
            action();
            if (main.ProjectCount != before && Order is not null)
            {
                Order.Add("publish");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Messages { get; } = [];
        public void Info(string message) => Messages.Add(message);
        public void Warning(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
        public void Error(string message, Exception exception) => Messages.Add($"{message} {exception.Message}");
    }

    private sealed class FakeKnownRootProvider : IUnrealKnownProjectRootProvider
    {
        public Task<UnrealKnownProjectRootsResult> GetKnownRootsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UnrealKnownProjectRootsResult([], []));
    }

    private sealed class CountingSolutionLocator : IVisualStudioSolutionLocator
    {
        public int LocateCount { get; private set; }
        public Action? OnLocate { get; set; }

        public VisualStudioSolutionSelection Locate(UnrealProject project)
        {
            LocateCount++;
            OnLocate?.Invoke();
            return VisualStudioSolutionSelection.Missing();
        }
    }

    private sealed class FakeUnrealEditorLauncher : IUnrealEditorLauncher
    {
        public LaunchResult Launch(
            UnrealProject project,
            EngineResolution engineResolution) =>
            throw new InvalidOperationException("Project launch was not expected.");

        public LaunchResult LaunchNewProject(InstalledEngine engine) =>
            LaunchResult.Succeeded();
    }
}
