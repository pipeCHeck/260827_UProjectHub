using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Settings;

namespace UProjectHub.Core.Discovery;

public sealed class ProjectRefreshService
{
    private readonly ProjectCatalog _catalog;
    private readonly ProjectMetadataLoader _metadataLoader;
    private readonly IProjectCacheRepository _cacheRepository;

    public ProjectRefreshService(
        ProjectCatalog catalog,
        ProjectMetadataLoader metadataLoader,
        IProjectCacheRepository cacheRepository)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(metadataLoader);
        ArgumentNullException.ThrowIfNull(cacheRepository);
        _catalog = catalog;
        _metadataLoader = metadataLoader;
        _cacheRepository = cacheRepository;
    }

    public async Task<ProjectRefreshResult> RefreshKnownAsync(
        AppSettings settings,
        IProgress<ProjectRefreshUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var updates = new List<ProjectRefreshUpdate>();
        var issues = new List<ProjectDiscoveryIssue>();
        var knownProjects = _catalog.GetSnapshot().Projects;

        foreach (var knownProject in knownProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ProjectRefreshUpdate update;
            if (!File.Exists(knownProject.ProjectFilePath.Value))
            {
                if (!_catalog.MarkMissing(knownProject.ProjectFilePath)
                    || !_catalog.TryGet(
                        knownProject.ProjectFilePath,
                        out var missingProject))
                {
                    continue;
                }

                update = new ProjectRefreshUpdate(
                    missingProject.ProjectFilePath,
                    missingProject,
                    Issue: null);
            }
            else
            {
                var loadResult = await _metadataLoader.LoadAsync(
                    new ProjectCandidate(knownProject.ProjectFilePath),
                    settings,
                    cancellationToken).ConfigureAwait(false);
                var refreshedProject = _catalog.UpsertPreservingUserState(
                    loadResult.Project);
                update = new ProjectRefreshUpdate(
                    refreshedProject.ProjectFilePath,
                    refreshedProject,
                    loadResult.Issue);
            }

            updates.Add(update);
            if (update.Issue is not null)
            {
                issues.Add(update.Issue);
            }

            progress?.Report(update);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _cacheRepository.SaveAsync(
            ProjectCatalogCacheDocumentFactory.Create(_catalog.GetSnapshot()),
            cancellationToken).ConfigureAwait(false);

        return CreateResult(updates, issues);
    }

    private static ProjectRefreshResult CreateResult(
        List<ProjectRefreshUpdate> updates,
        List<ProjectDiscoveryIssue> issues) =>
        new(
            Array.AsReadOnly(updates.ToArray()),
            Array.AsReadOnly(issues.ToArray()));
}
