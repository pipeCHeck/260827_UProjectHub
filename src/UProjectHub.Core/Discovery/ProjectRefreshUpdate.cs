using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Discovery;

public sealed record ProjectRefreshUpdate(
    ProjectPath ProjectFilePath,
    UnrealProject Project,
    ProjectDiscoveryIssue? Issue);

public sealed record ProjectRefreshResult(
    IReadOnlyList<ProjectRefreshUpdate> Updates,
    IReadOnlyList<ProjectDiscoveryIssue> Issues);

internal static class ProjectCatalogCacheDocumentFactory
{
    public static ProjectCacheDocument Create(ProjectCatalogSnapshot snapshot) =>
        new()
        {
            Projects = snapshot.Projects
                .OrderBy(
                    project => project.ProjectFilePath.Value,
                    StringComparer.OrdinalIgnoreCase)
                .Select(project => new ProjectCacheEntry(
                    project.ProjectFilePath,
                    project.Name,
                    project.EngineAssociation,
                    project.EngineDisplayVersion,
                    project.ProjectType,
                    project.LastModified,
                    project.ProjectState,
                    project.EngineState))
                .ToArray(),
        };
}
