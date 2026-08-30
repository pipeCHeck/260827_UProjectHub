namespace UProjectHub.Windows.SourceControl;

public enum GitProjectState
{
    NotRepository,
    Clean,
    Changed,
    Failed,
    GitUnavailable,
}

public sealed record GitRemote(
    string Name,
    string Url,
    string? WebUrl);

public sealed record GitProjectStatus(
    GitProjectState State,
    string? RepositoryRoot = null,
    IReadOnlyList<GitRemote>? RemoteEntries = null,
    string? ErrorMessage = null,
    string? RemoteErrorMessage = null)
{
    public IReadOnlyList<GitRemote> Remotes { get; } =
        RemoteEntries ?? Array.Empty<GitRemote>();
}

public interface IGitStatusService
{
    Task<GitProjectStatus> GetStatusAsync(
        string projectDirectory,
        bool includeRemotes = false,
        CancellationToken cancellationToken = default);
}
