using System.Collections.Concurrent;
using System.Windows;
using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Activity;
using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Discovery;
using UProjectHub.Core.Engines;
using UProjectHub.Core.Filtering;
using UProjectHub.Core.Models;
using UProjectHub.Core.Parsing;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Searching;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;
using UProjectHub.Core.Storage;
using UProjectHub.Core.Tests.Time;
using UProjectHub.Windows.Engines;
using UProjectHub.Windows.Launching;
using UProjectHub.Windows.Projects;
using AppThemeMode = UProjectHub.Core.Settings.ThemeMode;

namespace UProjectHub.Core.Tests.Integration;

[TestClass]
public sealed class MvpWorkflowTests
{
    private static readonly DateTimeOffset CachedTimestamp =
        new(2026, 8, 20, 1, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset PreviousLaunch =
        new(2026, 8, 21, 2, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset SuccessfulLaunch =
        new(2026, 8, 28, 6, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task FixtureWorkflow_VerifiesTheFullReadOnlyMvpLifecycle()
    {
        using var workspace = TemporaryWorkspace.Create();
        var gameAcademyRoot = workspace.GameAcademyRoot;
        var cppPath = new ProjectPath(Path.Combine(
            gameAcademyRoot,
            "CppGame",
            "CppGame.uproject"));
        var blueprintPath = new ProjectPath(Path.Combine(
            gameAcademyRoot,
            "BlueprintGame",
            "BlueprintGame.uproject"));
        var brokenPath = new ProjectPath(Path.Combine(
            gameAcademyRoot,
            "BrokenGame",
            "BrokenGame.uproject"));
        var rootProjectPath = workspace.CreateRootProject();
        var deepProjectPath = workspace.CreateDeepProject();
        var missingPath = workspace.CreateMissingProjectMarker();
        var fixtureContentsBefore = workspace.ReadFixtureFiles();

        var writer = new AtomicJsonFileWriter();
        var settingsRepository = new JsonSettingsRepository(
            workspace.SettingsFilePath,
            writer);
        var projectCacheRepository = new JsonProjectCacheRepository(
            workspace.ProjectCacheFilePath,
            writer);
        var engineCacheRepository = new JsonEngineCacheRepository(
            workspace.EngineCacheFilePath,
            writer);
        var settings = new AppSettings
        {
            ProjectSearchRoots = [gameAcademyRoot],
            ProjectUserStates =
            [
                new ProjectUserState(cppPath),
                new ProjectUserState(
                    blueprintPath,
                    LastLaunched: PreviousLaunch),
            ],
        };
        await settingsRepository.SaveAsync(settings);
        await projectCacheRepository.SaveAsync(new ProjectCacheDocument
        {
            Projects =
            [
                CreateCacheEntry(cppPath, "CppGame", "5.9", ProjectType.Cpp),
                CreateCacheEntry(brokenPath, "BrokenGame", "5.8", ProjectType.Blueprint),
                CreateCacheEntry(missingPath, "MissingGame", "5.8", ProjectType.Blueprint),
            ],
        });
        await engineCacheRepository.SaveAsync(new EngineCacheDocument());

        Assert.IsTrue(File.Exists(workspace.SettingsFilePath));
        Assert.IsTrue(File.Exists(workspace.ProjectCacheFilePath));
        Assert.IsTrue(File.Exists(workspace.EngineCacheFilePath));

        var signalingEngineCache = new SignalingEngineCacheRepository(
            engineCacheRepository);
        var catalog = new ProjectCatalog();
        var currentEngines = new CurrentEngineSnapshot();
        var metadataLoader = new ProjectMetadataLoader(
            new UProjectParser(),
            new ProjectActivityDetector(new ProjectActivityPolicy()));
        var discoveryService = new ProjectDiscoveryService(
            new ProjectRootScanner(new SystemProjectDirectoryEnumerator()),
            metadataLoader);
        var refreshService = new ProjectRefreshService(
            catalog,
            metadataLoader,
            projectCacheRepository);
        var rescanService = new ProjectRescanService(
            catalog,
            discoveryService,
            projectCacheRepository);
        var freshEngines = workspace.CreateInstalledEngines();
        var statusBar = new StatusBarViewModel();
        var projectList = new ProjectListViewModel();
        var searchFilter = new SearchFilterViewModel(
            projectList,
            new ProjectQueryParser(),
            new ProjectFilterService(
                new ProjectSearchService(new FakeClock(SuccessfulLaunch))),
            new ProjectSortService());
        var main = new MainViewModel(
            statusBar,
            projectList: projectList,
            searchFilter: searchFilter);
        var dispatcher = new ImmediateDispatcher();
        var logger = new RecordingLogger();
        var themeService = new ThemeService(
            new ResourceDictionary(),
            () => AppThemeMode.Light,
            source => new ResourceDictionary { ["Source"] = source.OriginalString });
        var refreshStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStartupRefresh = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCalls = 0;
        var rescanCalls = 0;
        var shallowDiscoveryCalls = 0;

        async Task<ProjectRefreshResult> RefreshKnownAsync(
            AppSettings currentSettings,
            IProgress<ProjectRefreshUpdate>? progress,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref refreshCalls) == 1)
            {
                refreshStarted.TrySetResult();
                await releaseStartupRefresh.Task.WaitAsync(cancellationToken);
            }

            return await refreshService.RefreshKnownAsync(
                currentSettings,
                progress,
                cancellationToken);
        }

        Task<ProjectRefreshResult> RescanAsync(
            IReadOnlyList<string> roots,
            AppSettings currentSettings,
            IProgress<ProjectRefreshUpdate>? progress,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref rescanCalls);
            return rescanService.RescanAsync(
                roots,
                currentSettings,
                progress,
                cancellationToken);
        }

        Task<ProjectDiscoveryResult> DiscoverShallowAsync(
            IReadOnlyList<string> roots,
            AppSettings currentSettings,
            IReadOnlyCollection<ProjectPath> excludedProjects,
            CancellationToken cancellationToken,
            Action<ProjectMetadataLoadResult>? projectLoaded)
        {
            Interlocked.Increment(ref shallowDiscoveryCalls);
            return discoveryService.DiscoverShallowAsync(
                roots,
                currentSettings,
                excludedProjects,
                cancellationToken,
                projectLoaded);
        }

        var backgroundRefresh = new BackgroundRefreshService(
            catalog,
            currentEngines,
            RefreshKnownAsync,
            RescanAsync,
            DiscoverShallowAsync,
            (_, _) => Task.FromResult(new EngineDiscoveryResult(freshEngines, [])),
            new FixedKnownRootProvider(gameAcademyRoot),
            dispatcher,
            main.SetProjects,
            batchSize: 32);
        var catalogOperationGate = new ProjectCatalogOperationGate();
        var coordinator = new ApplicationCoordinator(
            settingsRepository,
            projectCacheRepository,
            signalingEngineCache,
            catalog,
            catalogOperationGate,
            currentEngines,
            themeService,
            main,
            statusBar,
            backgroundRefresh,
            dispatcher,
            logger);

        try
        {
            await coordinator.StartAsync();
            await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.AreEqual(3, main.ProjectCount, "Cached rows must publish before Refresh completes.");
            CollectionAssert.AreEquivalent(
                new[] { "CppGame", "BrokenGame", "MissingGame" },
                main.ProjectList.Rows.Select(row => row.Name).ToArray());
            Assert.IsTrue(statusBar.IsOperationActive);
            Assert.AreEqual(0, rescanCalls, "Startup must never invoke full Rescan.");

            releaseStartupRefresh.TrySetResult();
            await signalingEngineCache.Saved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => !statusBar.IsOperationActive);

            var startupSnapshot = catalog.GetSnapshot();
            AssertProjectState(startupSnapshot, cppPath, ProjectState.Available);
            AssertProjectState(startupSnapshot, blueprintPath, ProjectState.Available);
            AssertProjectState(startupSnapshot, brokenPath, ProjectState.Broken);
            AssertProjectState(startupSnapshot, missingPath, ProjectState.Missing);
            AssertProjectState(startupSnapshot, rootProjectPath, ProjectState.Available);
            Assert.IsFalse(startupSnapshot.Projects.Any(project =>
                project.ProjectFilePath.Equals(deepProjectPath)));
            Assert.AreEqual(1, shallowDiscoveryCalls);
            Assert.AreEqual(0, rescanCalls);
            Assert.AreEqual(
                startupSnapshot.Projects.Count,
                startupSnapshot.Projects
                    .Select(project => project.ProjectFilePath)
                    .Distinct()
                    .Count(),
                "Configured, known, and cached sources must deduplicate by ProjectPath.");
            Assert.AreEqual(
                1,
                startupSnapshot.Projects.Count(project =>
                    project.ProjectFilePath.Equals(cppPath)));
            Assert.IsTrue(logger.Messages.Any(message =>
                message.Contains("BrokenGame", StringComparison.OrdinalIgnoreCase)));
            var startupCache = await projectCacheRepository.LoadAsync();
            Assert.IsTrue(startupCache.Projects.Any(entry =>
                entry.ProjectFilePath.Equals(blueprintPath)));
            Assert.IsTrue(startupCache.Projects.Any(entry =>
                entry.ProjectFilePath.Equals(rootProjectPath)));

            var shallowCallsBeforeRefresh = shallowDiscoveryCalls;
            Assert.IsTrue(await coordinator.RefreshAsync());
            Assert.AreEqual(shallowCallsBeforeRefresh, shallowDiscoveryCalls);
            Assert.AreEqual(0, rescanCalls, "F5 Refresh must not discover or Rescan.");

            var rescanResult = await coordinator.RescanAsync();
            Assert.IsTrue(rescanResult.IsSuccess);
            Assert.AreEqual(1, rescanCalls);
            AssertProjectState(
                catalog.GetSnapshot(),
                deepProjectPath,
                ProjectState.Available);

            VerifySearchFilterAndSort(main, gameAcademyRoot);
            VerifyEngineResolution(catalog.GetSnapshot(), currentEngines);

            var processLauncher = new RecordingProcessLauncher();
            var actionService = new ProjectActionService(
                catalog,
                new SettingsMutationService(settingsRepository),
                new ManagedProjectRemovalService(
                    catalog,
                    projectCacheRepository,
                    settingsRepository,
                    catalogOperationGate),
                new UnrealEditorLauncher(
                    processLauncher,
                    new FakeClock(SuccessfulLaunch)),
                new ExplorerLauncher(processLauncher),
                new VisualStudioLauncher(processLauncher),
                new RecordingClipboardService(),
                currentEngines.Resolve,
                logger);
            actionService.CatalogChanged += main.SetProjects;

            var cppProject = GetProject(catalog.GetSnapshot(), cppPath);
            var favoriteResult = await actionService.ToggleFavoriteAsync(cppProject);
            Assert.IsTrue(favoriteResult.IsSuccess);
            var favoriteSettings = await settingsRepository.LoadAsync();
            Assert.IsTrue(favoriteSettings.ProjectUserStates.Single(state =>
                state.ProjectPath.Equals(cppPath)).IsFavorite);
            searchFilter.FavoritesOnly = true;
            CollectionAssert.AreEqual(
                new[] { "CppGame" },
                projectList.Rows.Select(row => row.Name).ToArray());
            searchFilter.ResetCommand.Execute(null);

            var blueprintProject = GetProject(catalog.GetSnapshot(), blueprintPath);
            var ambiguousLaunch = await actionService.OpenProjectAsync(blueprintProject);
            Assert.IsFalse(ambiguousLaunch.IsSuccess);
            var missingProject = GetProject(catalog.GetSnapshot(), missingPath);
            var missingLaunch = await actionService.OpenProjectAsync(missingProject);
            Assert.IsFalse(missingLaunch.IsSuccess);
            Assert.IsEmpty(processLauncher.Requests);

            cppProject = GetProject(catalog.GetSnapshot(), cppPath);
            var launchResult = await actionService.OpenProjectAsync(cppProject);
            Assert.IsTrue(launchResult.IsSuccess);
            Assert.HasCount(1, processLauncher.Requests);
            Assert.AreEqual(
                currentEngines.Resolve(cppProject).ResolvedCandidate!.EditorPath,
                processLauncher.Requests.Single().FileName);
            CollectionAssert.AreEqual(
                new[] { cppPath.Value },
                processLauncher.Requests.Single().ArgumentList.ToArray());
            var launchedSettings = await settingsRepository.LoadAsync();
            var launchedState = launchedSettings.ProjectUserStates.Single(state =>
                state.ProjectPath.Equals(cppPath));
            Assert.AreEqual(SuccessfulLaunch, launchedState.LastLaunched);
            Assert.IsTrue(launchedState.IsFavorite);
            Assert.AreEqual(
                SuccessfulLaunch,
                GetProject(catalog.GetSnapshot(), cppPath).LastLaunched);

            EnsureSort(searchFilter, ProjectSortColumn.LastLaunched, SortDirection.Descending);
            Assert.AreEqual("CppGame", projectList.Rows.First().Name);

            missingProject = GetProject(catalog.GetSnapshot(), missingPath);
            var removalResult = await actionService.RemoveMissingAsync(missingProject);
            Assert.IsTrue(removalResult.IsSuccess);
            Assert.IsFalse(catalog.GetSnapshot().Projects.Any(project =>
                project.ProjectFilePath.Equals(missingPath)));
            Assert.IsTrue(File.Exists(workspace.MissingMarkerFilePath));
            var cacheAfterRemoval = await projectCacheRepository.LoadAsync();
            Assert.IsFalse(cacheAfterRemoval.Projects.Any(entry =>
                entry.ProjectFilePath.Equals(missingPath)));

            CollectionAssert.AreEqual(
                fixtureContentsBefore.Keys.OrderBy(value => value).ToArray(),
                workspace.ReadFixtureFiles().Keys.OrderBy(value => value).ToArray());
            foreach (var (relativePath, expectedBytes) in fixtureContentsBefore)
            {
                CollectionAssert.AreEqual(
                    expectedBytes,
                    workspace.ReadFixtureFiles()[relativePath],
                    $"Fixture file changed: {relativePath}");
            }
        }
        finally
        {
            releaseStartupRefresh.TrySetResult();
            await coordinator.StopAsync();
        }
    }

    private static void VerifySearchFilterAndSort(
        MainViewModel main,
        string rootPath)
    {
        var search = main.SearchFilter!;
        var list = main.ProjectList;

        search.SearchText = "CppGame";
        CollectionAssert.AreEqual(
            new[] { "CppGame" },
            list.Rows.Select(row => row.Name).ToArray());

        search.SearchText = $"path:\"{rootPath}\"";
        Assert.IsGreaterThanOrEqualTo(5, list.VisibleCount);
        search.SearchText = "version:5.9 type:cpp";
        CollectionAssert.AreEqual(
            new[] { "CppGame" },
            list.Rows.Select(row => row.Name).ToArray());

        search.SearchText = string.Empty;
        search.SelectedEngine = "5.9";
        CollectionAssert.AreEqual(
            new[] { "CppGame" },
            list.Rows.Select(row => row.Name).ToArray());
        search.SelectedProjectType = ProjectType.Cpp;
        CollectionAssert.AreEqual(
            new[] { "CppGame" },
            list.Rows.Select(row => row.Name).ToArray());
        search.ResetCommand.Execute(null);

        EnsureSort(search, ProjectSortColumn.Name, SortDirection.Ascending);
        CollectionAssert.AreEqual(
            list.Rows.Select(row => row.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            list.Rows.Select(row => row.Name).ToArray());

        EnsureSort(search, ProjectSortColumn.EngineVersion, SortDirection.Ascending);
        var engineOrder = list.Rows
            .Where(row => row.EngineDisplay is "5.9" or "5.10")
            .Select(row => row.EngineDisplay)
            .Distinct()
            .ToArray();
        CollectionAssert.AreEqual(new[] { "5.9", "5.10" }, engineOrder);

        EnsureSort(search, ProjectSortColumn.ProjectType, SortDirection.Ascending);
        Assert.IsTrue(IsNonDecreasing(list.Rows.Select(row => row.Project.ProjectType)));
        EnsureSort(search, ProjectSortColumn.LastModified, SortDirection.Ascending);
        Assert.IsTrue(IsNonDecreasing(list.Rows.Select(row => row.LastModified)));
        EnsureSort(search, ProjectSortColumn.LastLaunched, SortDirection.Descending);
        Assert.AreEqual("BlueprintGame", list.Rows.First().Name);
    }

    private static void VerifyEngineResolution(
        ProjectCatalogSnapshot snapshot,
        CurrentEngineSnapshot engines)
    {
        var cpp = snapshot.Projects.Single(project => project.Name == "CppGame");
        var blueprint = snapshot.Projects.Single(project => project.Name == "BlueprintGame");
        var deep = snapshot.Projects.Single(project => project.Name == "DeepGame");

        Assert.AreEqual(EngineResolutionState.Resolved, engines.Resolve(cpp).State);
        Assert.AreEqual(EngineResolutionState.Ambiguous, engines.Resolve(blueprint).State);
        Assert.AreEqual(EngineResolutionState.Missing, engines.Resolve(deep).State);
    }

    private static void EnsureSort(
        SearchFilterViewModel search,
        ProjectSortColumn column,
        SortDirection direction)
    {
        if (search.ActiveSort.Column != column)
        {
            search.RequestSort(column);
        }

        if (search.ActiveSort.Direction != direction)
        {
            search.RequestSort(column);
        }

        Assert.AreEqual(new ProjectSortDefinition(column, direction), search.ActiveSort);
    }

    private static bool IsNonDecreasing<T>(IEnumerable<T> values)
    {
        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return true;
        }

        var previous = enumerator.Current;
        while (enumerator.MoveNext())
        {
            if (Comparer<T>.Default.Compare(previous, enumerator.Current) > 0)
            {
                return false;
            }

            previous = enumerator.Current;
        }

        return true;
    }

    private static ProjectCacheEntry CreateCacheEntry(
        ProjectPath path,
        string name,
        string association,
        ProjectType projectType) => new(
        path,
        name,
        association,
        association,
        projectType,
        CachedTimestamp,
        ProjectState.Available,
        EngineResolutionState.Unknown);

    private static void AssertProjectState(
        ProjectCatalogSnapshot snapshot,
        ProjectPath path,
        ProjectState state) =>
        Assert.AreEqual(state, GetProject(snapshot, path).ProjectState);

    private static UnrealProject GetProject(
        ProjectCatalogSnapshot snapshot,
        ProjectPath path) =>
        snapshot.Projects.Single(project => project.ProjectFilePath.Equals(path));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
            {
                Assert.Fail("Timed out waiting for the background operation to finish.");
            }

            await Task.Delay(10);
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

    private sealed class FixedKnownRootProvider(string rootPath)
        : IUnrealKnownProjectRootProvider
    {
        public Task<UnrealKnownProjectRootsResult> GetKnownRootsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new UnrealKnownProjectRootsResult(
                [new ProjectPath(rootPath)],
                []));
        }
    }

    private sealed class SignalingEngineCacheRepository(IEngineCacheRepository inner)
        : IEngineCacheRepository
    {
        public TaskCompletionSource Saved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<EngineCacheDocument> LoadAsync(
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(cancellationToken);

        public async Task SaveAsync(
            EngineCacheDocument document,
            CancellationToken cancellationToken = default)
        {
            await inner.SaveAsync(document, cancellationToken);
            Saved.TrySetResult();
        }
    }

    private sealed class RecordingProcessLauncher : IProcessLauncher
    {
        private readonly List<ProcessRequest> _requests = [];

        public IReadOnlyList<ProcessRequest> Requests => _requests.AsReadOnly();

        public LaunchResult Launch(ProcessRequest request)
        {
            _requests.Add(request);
            return LaunchResult.Succeeded();
        }
    }

    private sealed class RecordingClipboardService : IClipboardService
    {
        public string? Text { get; private set; }

        public void SetText(string text) => Text = text;
    }

    private sealed class RecordingLogger : IAppLogger
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages.ToArray();

        public void Info(string message) => _messages.Enqueue(message);

        public void Warning(string message) => _messages.Enqueue(message);

        public void Error(string message) => _messages.Enqueue(message);

        public void Error(string message, Exception exception) =>
            _messages.Enqueue($"{message} {exception.GetType().Name}: {exception.Message}");
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string path, string gameAcademyRoot)
        {
            Path = path;
            GameAcademyRoot = gameAcademyRoot;
            var appData = System.IO.Path.Combine(path, "AppData");
            SettingsFilePath = System.IO.Path.Combine(appData, "settings.json");
            ProjectCacheFilePath = System.IO.Path.Combine(appData, "projects.cache.json");
            EngineCacheFilePath = System.IO.Path.Combine(appData, "engines.cache.json");
            MissingMarkerFilePath = string.Empty;
        }

        public string Path { get; }

        public string GameAcademyRoot { get; }

        public string SettingsFilePath { get; }

        public string ProjectCacheFilePath { get; }

        public string EngineCacheFilePath { get; }

        public string MissingMarkerFilePath { get; private set; }

        public static TemporaryWorkspace Create()
        {
            var source = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "Fixtures",
                "Integration",
                "GameAcademy"));
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "UProjectHub.Tests",
                "MvpWorkflow",
                Guid.NewGuid().ToString("N"));
            var gameAcademyRoot = System.IO.Path.Combine(path, "Game Academy");
            CopyTree(source, gameAcademyRoot);
            CreateMarker(gameAcademyRoot, "CppGame");
            CreateMarker(gameAcademyRoot, "BlueprintGame");
            return new TemporaryWorkspace(path, gameAcademyRoot);
        }

        public ProjectPath CreateRootProject()
        {
            var path = System.IO.Path.Combine(GameAcademyRoot, "RootGame.uproject");
            File.WriteAllText(path, BlueprintDescriptor("5.10"));
            return new ProjectPath(path);
        }

        public ProjectPath CreateDeepProject()
        {
            var directory = System.IO.Path.Combine(
                GameAcademyRoot,
                "School",
                "2026",
                "DeepGame");
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "DeepGame.uproject");
            File.WriteAllText(path, BlueprintDescriptor("5.8"));
            return new ProjectPath(path);
        }

        public ProjectPath CreateMissingProjectMarker()
        {
            var directory = System.IO.Path.Combine(GameAcademyRoot, "MissingGame");
            Directory.CreateDirectory(System.IO.Path.Combine(directory, "Content"));
            MissingMarkerFilePath = System.IO.Path.Combine(
                directory,
                "Content",
                "Keep.uasset");
            File.WriteAllText(MissingMarkerFilePath, "must remain after Remove from List");
            return new ProjectPath(System.IO.Path.Combine(directory, "MissingGame.uproject"));
        }

        public IReadOnlyList<InstalledEngine> CreateInstalledEngines()
        {
            return
            [
                CreateInstalledEngine("5.9", "UE_5.9"),
                CreateInstalledEngine("5.10", "UE_5.10_A"),
                CreateInstalledEngine("5.10", "UE_5.10_B"),
            ];
        }

        public IReadOnlyDictionary<string, byte[]> ReadFixtureFiles()
        {
            return Directory
                .EnumerateFiles(GameAcademyRoot, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    file => System.IO.Path.GetRelativePath(GameAcademyRoot, file),
                    File.ReadAllBytes,
                    StringComparer.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }

        private InstalledEngine CreateInstalledEngine(string version, string name)
        {
            var root = System.IO.Path.Combine(Path, "Engines", name);
            var editor = System.IO.Path.Combine(
                root,
                "Engine",
                "Binaries",
                "Win64",
                "UnrealEditor.exe");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(editor)!);
            File.WriteAllBytes(editor, []);
            return new InstalledEngine(
                $"Unreal Engine {version}",
                version,
                version,
                root,
                editor,
                EngineSource.Manual,
                IsUsable: true);
        }

        private static void CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.EnumerateDirectories(
                         source,
                         "*",
                         SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(System.IO.Path.Combine(
                    destination,
                    System.IO.Path.GetRelativePath(source, directory)));
            }

            foreach (var file in Directory.EnumerateFiles(
                         source,
                         "*",
                         SearchOption.AllDirectories))
            {
                var target = System.IO.Path.Combine(
                    destination,
                    System.IO.Path.GetRelativePath(source, file));
                File.Copy(file, target);
            }
        }

        private static void CreateMarker(string root, string projectName)
        {
            var content = System.IO.Path.Combine(root, projectName, "Content");
            Directory.CreateDirectory(content);
            File.WriteAllText(
                System.IO.Path.Combine(content, "Keep.uasset"),
                $"{projectName} content marker");
        }

        private static string BlueprintDescriptor(string association) =>
            $$"""
            {
              "FileVersion": 3,
              "EngineAssociation": "{{association}}",
              "Category": "",
              "Description": "Task 28 generated temp fixture"
            }
            """;
    }
}
