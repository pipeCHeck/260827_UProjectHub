using UProjectHub.Core.Paths;

namespace UProjectHub.Windows.Projects;

public interface IUnrealKnownProjectRootProvider
{
    Task<UnrealKnownProjectRootsResult> GetKnownRootsAsync(
        CancellationToken cancellationToken = default);
}

public sealed record UnrealKnownProjectRootIssue(
    string Path,
    string Message);

public sealed record UnrealKnownProjectRootsResult(
    IReadOnlyList<ProjectPath> Roots,
    IReadOnlyList<UnrealKnownProjectRootIssue> Issues);
