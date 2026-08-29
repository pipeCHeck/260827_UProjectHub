using UProjectHub.Core.Sorting;

namespace UProjectHub.Core.Settings;

public sealed record AppSettings
{
    public IReadOnlyList<string> ProjectSearchRoots { get; init; } = [];

    public IReadOnlyList<string> ManualEngineRoots { get; init; } = [];

    public IReadOnlyList<ProjectUserState> ProjectUserStates { get; init; } = [];

    public ThemeMode ThemeMode { get; init; } = ThemeMode.System;

    public RowDensity RowDensity { get; init; } = RowDensity.Normal;

    public AppLanguage Language { get; init; } = AppLanguage.English;

    public ProjectSortDefinition ActiveSort { get; init; } = new();

    public VisibleFilterState VisibleFilters { get; init; } = new();

    public IReadOnlyList<ColumnLayoutState> ColumnLayout { get; init; } = [];
}
