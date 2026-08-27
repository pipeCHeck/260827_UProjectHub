using UProjectHub.Core.Models;
using UProjectHub.Core.Versions;

namespace UProjectHub.Core.Sorting;

public sealed class ProjectSortService
{
    private static readonly StringComparer NameComparer =
        StringComparer.OrdinalIgnoreCase;

    public IReadOnlyList<UnrealProject> Sort(
        IEnumerable<UnrealProject> projects,
        ProjectSortDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(definition);

        var comparer = Comparer<UnrealProject>.Create(
            (left, right) => Compare(left, right, definition));

        return projects
            .OrderBy(project => project, comparer)
            .ToArray();
    }

    private static int Compare(
        UnrealProject left,
        UnrealProject right,
        ProjectSortDefinition definition)
    {
        var primaryComparison = definition.Column switch
        {
            ProjectSortColumn.Name => CompareDirectional(
                left.Name,
                right.Name,
                NameComparer,
                definition.Direction),
            ProjectSortColumn.EngineVersion => CompareDirectional(
                left.EngineDisplayVersion,
                right.EngineDisplayVersion,
                EngineVersionComparer.Instance,
                definition.Direction),
            ProjectSortColumn.ProjectType => CompareDirectional(
                left.ProjectType,
                right.ProjectType,
                Comparer<ProjectType>.Default,
                definition.Direction),
            ProjectSortColumn.LastModified => CompareDirectional(
                left.LastModified,
                right.LastModified,
                Comparer<DateTimeOffset>.Default,
                definition.Direction),
            ProjectSortColumn.LastLaunched => CompareLastLaunched(
                left.LastLaunched,
                right.LastLaunched,
                definition.Direction),
            _ => throw new ArgumentOutOfRangeException(
                nameof(definition),
                definition.Column,
                "Unsupported project sort column."),
        };

        return primaryComparison != 0
            ? primaryComparison
            : NameComparer.Compare(left.Name, right.Name);
    }

    private static int CompareLastLaunched(
        DateTimeOffset? left,
        DateTimeOffset? right,
        SortDirection direction)
    {
        if (left is null)
        {
            return right is null ? 0 : 1;
        }

        if (right is null)
        {
            return -1;
        }

        return CompareDirectional(
            left.Value,
            right.Value,
            Comparer<DateTimeOffset>.Default,
            direction);
    }

    private static int CompareDirectional<T>(
        T left,
        T right,
        IComparer<T> comparer,
        SortDirection direction)
    {
        return direction switch
        {
            SortDirection.Ascending => comparer.Compare(left, right),
            SortDirection.Descending => comparer.Compare(right, left),
            _ => throw new ArgumentOutOfRangeException(
                nameof(direction),
                direction,
                "Unsupported sort direction."),
        };
    }
}
