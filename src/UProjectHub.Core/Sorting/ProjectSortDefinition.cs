namespace UProjectHub.Core.Sorting;

public sealed record ProjectSortDefinition(
    ProjectSortColumn Column = ProjectSortColumn.LastModified,
    SortDirection Direction = SortDirection.Descending);
