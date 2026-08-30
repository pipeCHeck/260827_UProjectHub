using UProjectHub.Core.Models;
using UProjectHub.Core.Searching;

namespace UProjectHub.Core.Filtering;

public sealed class ProjectFilterService
{
    private readonly ProjectSearchService _searchService;

    public ProjectFilterService(ProjectSearchService searchService)
    {
        ArgumentNullException.ThrowIfNull(searchService);
        _searchService = searchService;
    }

    public bool Matches(UnrealProject project, ProjectFilter filter)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(filter);

        if (!string.IsNullOrWhiteSpace(filter.Engine)
            && !MatchesEngine(project, filter.Engine))
        {
            return false;
        }

        if (filter.ProjectType is { } projectType
            && project.ProjectType != projectType)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Tag)
            && !project.Tags.Contains(filter.Tag, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return !filter.FavoritesOnly || project.IsFavorite;
    }

    public bool Matches(
        UnrealProject project,
        ProjectQuery query,
        ProjectFilter filter)
    {
        return _searchService.Matches(project, query)
            && Matches(project, filter);
    }

    private static bool MatchesEngine(UnrealProject project, string engine)
    {
        return string.Equals(
                project.EngineDisplayVersion,
                engine,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                project.EngineAssociation,
                engine,
                StringComparison.OrdinalIgnoreCase);
    }
}
