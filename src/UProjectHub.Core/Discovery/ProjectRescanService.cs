using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Settings;

namespace UProjectHub.Core.Discovery;

public sealed class ProjectRescanService
{
    private readonly ProjectCatalog _catalog;
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly IProjectCacheRepository _cacheRepository;

    public ProjectRescanService(
        ProjectCatalog catalog,
        ProjectDiscoveryService discoveryService,
        IProjectCacheRepository cacheRepository)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(discoveryService);
        ArgumentNullException.ThrowIfNull(cacheRepository);
        _catalog = catalog;
        _discoveryService = discoveryService;
        _cacheRepository = cacheRepository;
    }

    public async Task<ProjectRefreshResult> RescanAsync(
        IEnumerable<string> rootPaths,
        AppSettings settings,
        IProgress<ProjectRefreshUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rootPaths);
        ArgumentNullException.ThrowIfNull(settings);

        var discoveryResult = await _discoveryService.DiscoverAsync(
            rootPaths,
            settings,
            cancellationToken).ConfigureAwait(false);
        var updates = new List<ProjectRefreshUpdate>();

        foreach (var project in discoveryResult.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var issue = FindProjectIssue(
                discoveryResult.Issues,
                project.ProjectFilePath.Value);
            _catalog.Upsert(project);
            var update = new ProjectRefreshUpdate(
                project.ProjectFilePath,
                project,
                issue);
            updates.Add(update);
            progress?.Report(update);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _cacheRepository.SaveAsync(
            ProjectCatalogCacheDocumentFactory.Create(_catalog.GetSnapshot()),
            cancellationToken).ConfigureAwait(false);

        return new ProjectRefreshResult(
            Array.AsReadOnly(updates.ToArray()),
            discoveryResult.Issues);
    }

    private static ProjectDiscoveryIssue? FindProjectIssue(
        IReadOnlyList<ProjectDiscoveryIssue> issues,
        string projectFilePath) =>
        issues.FirstOrDefault(issue =>
            issue.Kind == ProjectDiscoveryIssueKind.MetadataLoad
            && string.Equals(
                issue.Path,
                projectFilePath,
                StringComparison.OrdinalIgnoreCase));
}
