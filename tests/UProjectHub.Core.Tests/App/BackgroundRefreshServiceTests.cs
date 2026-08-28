using UProjectHub.Core.Activity;
using UProjectHub.App.Services;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Discovery;
using UProjectHub.Core.Models;
using UProjectHub.Core.Parsing;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Settings;
using UProjectHub.Windows.Engines;
using UProjectHub.Windows.Projects;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class BackgroundRefreshServiceTests
{
    [TestMethod]
    public async Task UiUpdateBatcher_UsesThirtyTwoItemBatchesAndFlushesFinalPartialBatch()
    {
        var batches = new List<IReadOnlyList<int>>();
        var batcher = new UiUpdateBatcher<int>(
            32,
            (batch, _) =>
            {
                batches.Add(batch);
                return Task.CompletedTask;
            });

        for (var index = 0; index < 1000; index++)
        {
            batcher.Add(index);
        }

        await batcher.FlushAsync();

        Assert.HasCount(32, batches);
        Assert.IsTrue(batches.Take(31).All(batch => batch.Count == 32));
        Assert.HasCount(8, batches[^1]);
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 1000).ToArray(),
            batches.SelectMany(batch => batch).ToArray());
    }

    [TestMethod]
    public async Task Refresh_BatchesOneThousandUpdatesWithoutPerItemDispatcherPosts()
    {
        var catalog = CreateCatalog(1000);
        var dispatcher = new RecordingDispatcher();
        var refreshCalls = 0;
        var rescanCalls = 0;
        var lightweightCalls = 0;
        var service = CreateService(
            catalog,
            dispatcher,
            refresh: (settings, progress, cancellationToken) =>
            {
                refreshCalls++;
                foreach (var project in catalog.GetSnapshot().Projects)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new ProjectRefreshUpdate(
                        project.ProjectFilePath,
                        project,
                        null));
                }

                return Task.FromResult(new ProjectRefreshResult([], []));
            },
            rescan: (roots, settings, progress, cancellationToken) =>
            {
                rescanCalls++;
                return Task.FromResult(new ProjectRefreshResult([], []));
            },
            discoverLightweight: (roots, settings, excluded, cancellationToken, loaded) =>
            {
                lightweightCalls++;
                return Task.FromResult(new ProjectDiscoveryResult([], []));
            });

        var result = await service.RefreshAsync(new AppSettings());

        Assert.AreEqual(1, refreshCalls);
        Assert.AreEqual(0, rescanCalls);
        Assert.AreEqual(0, lightweightCalls);
        Assert.AreEqual(33, dispatcher.InvokeCount);
        Assert.HasCount(1000, result.Snapshot.Projects);
    }

    [TestMethod]
    public async Task Refresh_ReplacesEngineSnapshotResolvesProjectsAndPreservesProviderIssues()
    {
        var project = CreateProject(0, association: "5.8");
        var catalog = new ProjectCatalog();
        catalog.Upsert(project);
        var engines = new CurrentEngineSnapshot();
        var engine = CreateEngine("5.8", @"C:\UE58");
        var service = CreateService(
            catalog,
            new RecordingDispatcher(),
            engines: engines,
            discoverEngines: (_, _) => Task.FromResult(new EngineDiscoveryResult(
                [engine],
                [new EngineProviderIssue("Launcher", "stale entry")])));

        var result = await service.RefreshAsync(new AppSettings());

        Assert.AreEqual(EngineResolutionState.Resolved, result.Snapshot.Projects.Single().EngineState);
        Assert.AreEqual("5.8", result.Snapshot.Projects.Single().EngineDisplayVersion);
        Assert.AreEqual(engine, engines.Engines.Single());
        Assert.HasCount(1, result.EngineIssues);
    }

    [TestMethod]
    public async Task StartupRefresh_ReportsKnownRootIssuesWithoutStartingConfiguredRootRescan()
    {
        var rescanCalls = 0;
        var knownRoots = new FakeKnownRootProvider(new UnrealKnownProjectRootsResult(
            [new ProjectPath(@"C:\Known")],
            [new UnrealKnownProjectRootIssue("EditorSettings.ini", "unreadable")]));
        var service = CreateService(
            new ProjectCatalog(),
            new RecordingDispatcher(),
            rescan: (roots, settings, progress, cancellationToken) =>
            {
                rescanCalls++;
                return Task.FromResult(new ProjectRefreshResult([], []));
            },
            knownRoots: knownRoots);

        var result = await service.StartupRefreshAsync(new AppSettings
        {
            ProjectSearchRoots = [@"D:\Configured"],
        });

        Assert.AreEqual(1, knownRoots.Calls);
        Assert.AreEqual(0, rescanCalls);
        Assert.HasCount(1, result.KnownRootIssues);
    }

    [TestMethod]
    public async Task StartupRefreshDiscoversOnlyRootAndImmediateChildrenAcrossConfiguredAndKnownRoots()
    {
        using var tree = new TemporaryLightweightTree();
        var configuredRoot = tree.CreateDirectory("Configured");
        var knownRoot = tree.CreateDirectory("Known");
        tree.WriteProject(configuredRoot, "RootGame.uproject", valid: true);
        tree.WriteProject(Path.Combine(configuredRoot, "Game"), "Game.uproject", valid: true);
        tree.WriteProject(Path.Combine(configuredRoot, "Broken"), "Broken.uproject", valid: false);
        tree.WriteProject(Path.Combine(configuredRoot, "School", "2026", "Deep"), "Deep.uproject", valid: true);
        var knownProjectPath = tree.WriteProject(
            Path.Combine(knownRoot, "KnownGame"),
            "KnownGame.uproject",
            valid: true);
        var launched = new DateTimeOffset(2026, 8, 20, 1, 2, 3, TimeSpan.Zero);
        var settings = new AppSettings
        {
            ProjectSearchRoots = [configuredRoot],
            ProjectUserStates = [new ProjectUserState(new ProjectPath(knownProjectPath), true, launched)],
        };
        var catalog = new ProjectCatalog();
        var discovery = new ProjectDiscoveryService(
            new ProjectRootScanner(new SystemProjectDirectoryEnumerator()),
            new ProjectMetadataLoader(
                new UProjectParser(),
                new ProjectActivityDetector(new ProjectActivityPolicy())));
        var service = CreateService(
            catalog,
            new RecordingDispatcher(),
            discoverLightweight: (roots, currentSettings, excluded, cancellationToken, loaded) =>
                discovery.DiscoverShallowAsync(
                    roots,
                    currentSettings,
                    excluded,
                    cancellationToken,
                    loaded),
            knownRoots: new FakeKnownRootProvider(new UnrealKnownProjectRootsResult(
                [new ProjectPath(knownRoot), new ProjectPath(configuredRoot)],
                [])));

        var result = await service.StartupRefreshAsync(settings);

        CollectionAssert.AreEquivalent(
            new[] { "RootGame", "Game", "Broken", "KnownGame" },
            result.Snapshot.Projects.Select(project => project.Name).ToArray());
        Assert.IsFalse(result.Snapshot.Projects.Any(project => project.Name == "Deep"));
        var known = result.Snapshot.Projects.Single(project => project.Name == "KnownGame");
        Assert.IsTrue(known.IsFavorite);
        Assert.AreEqual(launched, known.LastLaunched);
        Assert.AreEqual(ProjectState.Broken, result.Snapshot.Projects.Single(project => project.Name == "Broken").ProjectState);
        Assert.IsTrue(result.ProjectIssues.Any(issue => issue.Kind == ProjectDiscoveryIssueKind.MetadataLoad));
    }

    [TestMethod]
    public async Task Cancellation_PreventsAnyUiPublish()
    {
        var dispatcher = new RecordingDispatcher();
        var service = CreateService(new ProjectCatalog(), dispatcher);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.RefreshAsync(new AppSettings(), cancellation.Token));

        Assert.AreEqual(0, dispatcher.InvokeCount);
    }

    private static BackgroundRefreshService CreateService(
        ProjectCatalog catalog,
        IUiDispatcher dispatcher,
        CurrentEngineSnapshot? engines = null,
        Func<AppSettings, IProgress<ProjectRefreshUpdate>?, CancellationToken, Task<ProjectRefreshResult>>? refresh = null,
        Func<IReadOnlyList<string>, AppSettings, IProgress<ProjectRefreshUpdate>?, CancellationToken, Task<ProjectRefreshResult>>? rescan = null,
        Func<IReadOnlyList<string>, AppSettings, IReadOnlyCollection<ProjectPath>, CancellationToken, Action<ProjectMetadataLoadResult>?, Task<ProjectDiscoveryResult>>? discoverLightweight = null,
        Func<AppSettings, CancellationToken, Task<EngineDiscoveryResult>>? discoverEngines = null,
        IUnrealKnownProjectRootProvider? knownRoots = null)
    {
        return new BackgroundRefreshService(
            catalog,
            engines ?? new CurrentEngineSnapshot(),
            refresh ?? ((_, _, _) => Task.FromResult(new ProjectRefreshResult([], []))),
            rescan ?? ((_, _, _, _) => Task.FromResult(new ProjectRefreshResult([], []))),
            discoverLightweight ?? ((_, _, _, _, _) => Task.FromResult(new ProjectDiscoveryResult([], []))),
            discoverEngines ?? ((_, _) => Task.FromResult(new EngineDiscoveryResult([], []))),
            knownRoots ?? new FakeKnownRootProvider(new UnrealKnownProjectRootsResult([], [])),
            dispatcher,
            _ => { },
            batchSize: 32);
    }

    private static ProjectCatalog CreateCatalog(int count)
    {
        var catalog = new ProjectCatalog();
        for (var index = 0; index < count; index++)
        {
            catalog.Upsert(CreateProject(index));
        }

        return catalog;
    }

    private static UnrealProject CreateProject(int index, string? association = null) => new(
        $"Project{index:D4}",
        new ProjectPath($@"C:\Projects\Project{index:D4}\Project{index:D4}.uproject"),
        association,
        association,
        ProjectType.Blueprint,
        new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero).AddMinutes(index),
        null,
        false,
        ProjectState.Available,
        EngineResolutionState.Unknown);

    private static InstalledEngine CreateEngine(string version, string root) => new(
        $"Unreal Engine {version}",
        version,
        version,
        root,
        Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor.exe"),
        EngineSource.Launcher,
        true);

    private sealed class RecordingDispatcher : IUiDispatcher
    {
        public int InvokeCount { get; private set; }

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvokeCount++;
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeKnownRootProvider(UnrealKnownProjectRootsResult result)
        : IUnrealKnownProjectRootProvider
    {
        public int Calls { get; private set; }

        public Task<UnrealKnownProjectRootsResult> GetKnownRootsAsync(
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class TemporaryLightweightTree : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "UProjectHub.Tests",
            "Lightweight",
            Guid.NewGuid().ToString("N"));

        public string CreateDirectory(params string[] parts)
        {
            var path = Path.Combine([_root, .. parts]);
            Directory.CreateDirectory(path);
            return path;
        }

        public string WriteProject(string directory, string fileName, bool valid)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(
                path,
                valid
                    ? "{ \"FileVersion\": 3, \"EngineAssociation\": \"5.8\" }"
                    : "{ malformed");
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
