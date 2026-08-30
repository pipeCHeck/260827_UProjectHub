using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Cleanup;

public enum ProjectCleanupTargetKind
{
    Intermediate,
    DerivedDataCache,
    VisualStudioWorkspace,
    Binaries,
    Solution,
}

public enum ProjectCleanupItemStatus
{
    Deleted,
    NotFound,
    Unavailable,
    Failed,
}

public sealed record ProjectCleanupItemInspection(
    ProjectCleanupTargetKind Kind,
    string? Path,
    bool Exists,
    bool CanDelete,
    long FileSizeBytes,
    string? ErrorMessage,
    IReadOnlyList<string> CandidatePaths);

public sealed record ProjectCleanupInspection(
    UnrealProject Project,
    IReadOnlyList<ProjectCleanupItemInspection> Items);

public sealed record ProjectCleanupRequest(
    UnrealProject Project,
    IReadOnlyCollection<ProjectCleanupTargetKind> Targets);

public sealed record ProjectCleanupItemResult(
    ProjectCleanupTargetKind Kind,
    string? Path,
    ProjectCleanupItemStatus Status,
    long DeletedBytes,
    string? ErrorMessage);

public sealed record ProjectCleanupResult(
    IReadOnlyList<ProjectCleanupItemResult> Items)
{
    public long DeletedBytes => Items.Sum(item => item.DeletedBytes);
}

public interface IProjectCleanupService
{
    Task<ProjectCleanupInspection> InspectAsync(
        UnrealProject project,
        CancellationToken cancellationToken = default);

    Task<ProjectCleanupResult> CleanupAsync(
        ProjectCleanupRequest request,
        CancellationToken cancellationToken = default);
}
