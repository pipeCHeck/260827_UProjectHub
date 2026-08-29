using UProjectHub.Core.Catalog;
using UProjectHub.Core.Discovery;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;

namespace UProjectHub.App.Services;

public interface IProjectOperations
{
    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    Task<ProjectOperationResult> AddProjectSearchRootAsync(string root, CancellationToken cancellationToken = default);

    Task<ProjectOperationResult> RemoveProjectSearchRootAsync(string root, CancellationToken cancellationToken = default);

    Task<ProjectOperationResult> AddManualEngineRootAsync(string root, CancellationToken cancellationToken = default);

    Task<ProjectOperationResult> RemoveManualEngineRootAsync(string root, CancellationToken cancellationToken = default);

    Task<ProjectOperationResult> SaveAppearanceAsync(
        ThemeMode themeMode,
        RowDensity rowDensity,
        AppLanguage language,
        CancellationToken cancellationToken = default);

    Task<ProjectOperationResult> SaveViewStateAsync(
        ProjectSortDefinition activeSort,
        VisibleFilterState visibleFilters,
        IReadOnlyList<ColumnLayoutState>? columnLayout = null,
        CancellationToken cancellationToken = default);

    Task<ProjectRescanOperationResult> RescanAsync(CancellationToken cancellationToken = default);
}

public sealed record ProjectOperationResult(
    bool IsSuccess,
    bool Changed,
    AppSettings? Settings,
    string? Message);

public sealed record ProjectRescanOperationResult(
    bool IsSuccess,
    ProjectCatalogSnapshot? Snapshot,
    IReadOnlyList<ProjectDiscoveryIssue> Issues,
    string? Message);
