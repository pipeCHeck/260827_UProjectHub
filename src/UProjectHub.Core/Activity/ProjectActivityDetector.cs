namespace UProjectHub.Core.Activity;

public sealed class ProjectActivityDetector
{
    private readonly ProjectActivityPolicy _policy;

    public ProjectActivityDetector(ProjectActivityPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
    }

    public Task<DateTimeOffset?> GetLastModifiedUtcAsync(
        string projectFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);

        return Task.Run(
            () => GetLastModifiedUtc(Path.GetFullPath(projectFilePath), cancellationToken),
            cancellationToken);
    }

    private DateTimeOffset? GetLastModifiedUtc(
        string projectFilePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset? latestTimestamp = null;
        if (File.Exists(projectFilePath))
        {
            latestTimestamp = ToUtcTimestamp(File.GetLastWriteTimeUtc(projectFilePath));
        }

        var projectDirectory = Path.GetDirectoryName(projectFilePath);
        if (string.IsNullOrEmpty(projectDirectory))
        {
            return latestTimestamp;
        }

        foreach (var rootName in _policy.IncludedRootNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var root = new DirectoryInfo(Path.Combine(projectDirectory, rootName));
            if (!root.Exists || !_policy.ShouldTraverseDirectory(root.Name, root.Attributes))
            {
                continue;
            }

            latestTimestamp = GetLatestTimestamp(
                root,
                latestTimestamp,
                cancellationToken);
        }

        return latestTimestamp;
    }

    private DateTimeOffset? GetLatestTimestamp(
        DirectoryInfo root,
        DateTimeOffset? latestTimestamp,
        CancellationToken cancellationToken)
    {
        var pendingDirectories = new Stack<DirectoryInfo>();
        pendingDirectories.Push(root);

        while (pendingDirectories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var file in directory.EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                latestTimestamp = LaterOf(
                    latestTimestamp,
                    ToUtcTimestamp(file.LastWriteTimeUtc));
            }

            foreach (var childDirectory in directory.EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_policy.ShouldTraverseDirectory(
                    childDirectory.Name,
                    childDirectory.Attributes))
                {
                    pendingDirectories.Push(childDirectory);
                }
            }
        }

        return latestTimestamp;
    }

    private static DateTimeOffset LaterOf(
        DateTimeOffset? current,
        DateTimeOffset candidate) =>
        current is null || candidate > current.Value ? candidate : current.Value;

    private static DateTimeOffset ToUtcTimestamp(DateTime timestamp) =>
        new(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc));
}
