using UProjectHub.Core.Models;
using UProjectHub.Core.Settings;

namespace UProjectHub.Core.Discovery;

public sealed class ProjectDiscoveryService
{
    private readonly ProjectRootScanner _scanner;
    private readonly ProjectMetadataLoader _metadataLoader;

    public ProjectDiscoveryService(
        ProjectRootScanner scanner,
        ProjectMetadataLoader metadataLoader)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(metadataLoader);
        _scanner = scanner;
        _metadataLoader = metadataLoader;
    }

    public async Task<ProjectDiscoveryResult> DiscoverAsync(
        IEnumerable<string> rootPaths,
        AppSettings settings,
        CancellationToken cancellationToken = default,
        Action<ProjectMetadataLoadResult>? projectLoaded = null)
    {
        ArgumentNullException.ThrowIfNull(rootPaths);
        ArgumentNullException.ThrowIfNull(settings);

        var scanResult = await _scanner.ScanAsync(
            rootPaths,
            cancellationToken).ConfigureAwait(false);
        var projects = new List<UnrealProject>();
        var issues = new List<ProjectDiscoveryIssue>(scanResult.Issues);

        foreach (var candidate in scanResult.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loadResult = await _metadataLoader.LoadAsync(
                candidate,
                settings,
                cancellationToken).ConfigureAwait(false);
            projects.Add(loadResult.Project);
            if (loadResult.Issue is not null)
            {
                issues.Add(loadResult.Issue);
            }

            projectLoaded?.Invoke(loadResult);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return new ProjectDiscoveryResult(
            Array.AsReadOnly(projects.ToArray()),
            Array.AsReadOnly(issues.ToArray()));
    }
}
