using UProjectHub.Core.Catalog;
using UProjectHub.Core.Discovery;
using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Settings;
using UProjectHub.Windows.Engines;
using UProjectHub.Windows.Projects;

namespace UProjectHub.App.Services;

public sealed record BackgroundRefreshResult(
    ProjectCatalogSnapshot Snapshot,
    IReadOnlyList<InstalledEngine> Engines,
    IReadOnlyList<ProjectDiscoveryIssue> ProjectIssues,
    IReadOnlyList<EngineProviderIssue> EngineIssues,
    IReadOnlyList<UnrealKnownProjectRootIssue> KnownRootIssues);

public sealed class BackgroundRefreshService
{
    public const int DefaultBatchSize = 32;

    private readonly ProjectCatalog _catalog;
    private readonly CurrentEngineSnapshot _engines;
    private readonly Func<AppSettings, IProgress<ProjectRefreshUpdate>?, CancellationToken, Task<ProjectRefreshResult>> _refreshKnown;
    private readonly Func<IReadOnlyList<string>, AppSettings, IProgress<ProjectRefreshUpdate>?, CancellationToken, Task<ProjectRefreshResult>> _rescan;
    private readonly Func<IReadOnlyList<string>, AppSettings, IReadOnlyCollection<ProjectPath>, CancellationToken, Action<ProjectMetadataLoadResult>?, Task<ProjectDiscoveryResult>> _discoverLightweight;
    private readonly Func<AppSettings, CancellationToken, Task<EngineDiscoveryResult>> _discoverEngines;
    private readonly IUnrealKnownProjectRootProvider _knownRootProvider;
    private readonly IUiDispatcher _dispatcher;
    private readonly Action<ProjectCatalogSnapshot> _publishSnapshot;
    private readonly int _batchSize;

    public BackgroundRefreshService(
        ProjectCatalog catalog,
        CurrentEngineSnapshot engines,
        Func<AppSettings, IProgress<ProjectRefreshUpdate>?, CancellationToken, Task<ProjectRefreshResult>> refreshKnown,
        Func<IReadOnlyList<string>, AppSettings, IProgress<ProjectRefreshUpdate>?, CancellationToken, Task<ProjectRefreshResult>> rescan,
        Func<IReadOnlyList<string>, AppSettings, IReadOnlyCollection<ProjectPath>, CancellationToken, Action<ProjectMetadataLoadResult>?, Task<ProjectDiscoveryResult>> discoverLightweight,
        Func<AppSettings, CancellationToken, Task<EngineDiscoveryResult>> discoverEngines,
        IUnrealKnownProjectRootProvider knownRootProvider,
        IUiDispatcher dispatcher,
        Action<ProjectCatalogSnapshot> publishSnapshot,
        int batchSize = DefaultBatchSize)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _engines = engines ?? throw new ArgumentNullException(nameof(engines));
        _refreshKnown = refreshKnown ?? throw new ArgumentNullException(nameof(refreshKnown));
        _rescan = rescan ?? throw new ArgumentNullException(nameof(rescan));
        _discoverLightweight = discoverLightweight ?? throw new ArgumentNullException(nameof(discoverLightweight));
        _discoverEngines = discoverEngines ?? throw new ArgumentNullException(nameof(discoverEngines));
        _knownRootProvider = knownRootProvider ?? throw new ArgumentNullException(nameof(knownRootProvider));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _publishSnapshot = publishSnapshot ?? throw new ArgumentNullException(nameof(publishSnapshot));
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        _batchSize = batchSize;
    }

    public Task<BackgroundRefreshResult> RefreshAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default) =>
        RunAsync(settings, OperationKind.Refresh, cancellationToken);

    public Task<BackgroundRefreshResult> StartupRefreshAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default) =>
        RunAsync(settings, OperationKind.Startup, cancellationToken);

    public Task<BackgroundRefreshResult> RescanAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default) =>
        RunAsync(settings, OperationKind.Rescan, cancellationToken);

    private async Task<BackgroundRefreshResult> RunAsync(
        AppSettings settings,
        OperationKind operationKind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var batcher = new UiUpdateBatcher<ProjectRefreshUpdate>(
            _batchSize,
            (_, token) => _dispatcher.InvokeAsync(
                () => _publishSnapshot(_catalog.GetSnapshot()),
                token));
        var progress = new InlineProgress<ProjectRefreshUpdate>(update =>
            batcher.Add(update, cancellationToken));

        ProjectRefreshResult projectResult;
        if (operationKind == OperationKind.Rescan)
        {
            projectResult = await Task.Run(
                () => _rescan(
                    settings.ProjectSearchRoots.ToArray(),
                    settings,
                    progress,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            projectResult = await Task.Run(
                () => _refreshKnown(settings, progress, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        var projectIssues = new List<ProjectDiscoveryIssue>(projectResult.Issues);
        var knownRoots = new UnrealKnownProjectRootsResult([], []);
        if (operationKind == OperationKind.Startup)
        {
            knownRoots = await _knownRootProvider
                .GetKnownRootsAsync(cancellationToken)
                .ConfigureAwait(false);
            var roots = settings.ProjectSearchRoots
                .Concat(knownRoots.Roots.Select(root => root.Value))
                .ToArray();
            var excludedProjects = _catalog
                .GetSnapshot()
                .Projects
                .Select(project => project.ProjectFilePath)
                .ToArray();
            var discoveryResult = await _discoverLightweight(
                roots,
                settings,
                excludedProjects,
                cancellationToken,
                loadResult =>
                {
                    _catalog.Upsert(loadResult.Project);
                    progress.Report(new ProjectRefreshUpdate(
                        loadResult.Project.ProjectFilePath,
                        loadResult.Project,
                        loadResult.Issue));
                }).ConfigureAwait(false);
            projectIssues.AddRange(discoveryResult.Issues);
        }

        await batcher.FlushAsync(cancellationToken).ConfigureAwait(false);

        var engineResult = await Task.Run(
            () => _discoverEngines(settings, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        _engines.Replace(engineResult.Engines);
        ApplyEngineResolution();

        var finalSnapshot = _catalog.GetSnapshot();
        await _dispatcher.InvokeAsync(
            () => _publishSnapshot(finalSnapshot),
            cancellationToken).ConfigureAwait(false);

        return new BackgroundRefreshResult(
            finalSnapshot,
            _engines.Engines,
            Array.AsReadOnly(projectIssues.ToArray()),
            engineResult.Issues,
            knownRoots.Issues);
    }

    private void ApplyEngineResolution()
    {
        foreach (var project in _catalog.GetSnapshot().Projects)
        {
            var resolution = _engines.Resolve(project);
            _catalog.Upsert(project with
            {
                EngineState = resolution.State,
                EngineDisplayVersion = resolution.ResolvedCandidate?.DisplayVersion
                    ?? project.EngineDisplayVersion,
            });
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private enum OperationKind
    {
        Startup,
        Refresh,
        Rescan,
    }
}
