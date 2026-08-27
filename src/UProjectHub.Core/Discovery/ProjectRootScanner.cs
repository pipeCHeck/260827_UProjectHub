using System.Security;
using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Discovery;

public sealed class ProjectRootScanner
{
    private readonly IProjectDirectoryEnumerator _directoryEnumerator;

    public ProjectRootScanner(IProjectDirectoryEnumerator directoryEnumerator)
    {
        ArgumentNullException.ThrowIfNull(directoryEnumerator);
        _directoryEnumerator = directoryEnumerator;
    }

    public Task<ProjectRootScanResult> ScanAsync(
        IEnumerable<string> rootPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rootPaths);

        return Task.Run(
            () => Scan(rootPaths, cancellationToken),
            cancellationToken);
    }

    private ProjectRootScanResult Scan(
        IEnumerable<string> rootPaths,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ProjectCandidate>();
        var issues = new List<ProjectDiscoveryIssue>();
        var knownPaths = new HashSet<ProjectPath>();

        foreach (var configuredRoot in rootPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string rootPath;
            try
            {
                rootPath = Path.GetFullPath(configuredRoot);
            }
            catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
            {
                issues.Add(CreateRootIssue(configuredRoot, exception.Message));
                continue;
            }

            if (!_directoryEnumerator.DirectoryExists(rootPath))
            {
                issues.Add(CreateRootIssue(rootPath, "Project search root was not found."));
                continue;
            }

            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(rootPath);

            while (pendingDirectories.TryPop(out var directoryPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (_directoryEnumerator.IsReparsePoint(directoryPath))
                    {
                        continue;
                    }

                    foreach (var projectFilePath in
                             _directoryEnumerator.EnumerateProjectFiles(directoryPath))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        AddCandidate(projectFilePath, rootPath, knownPaths, candidates);
                    }

                    foreach (var childDirectory in
                             _directoryEnumerator.EnumerateDirectories(directoryPath))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var fullChildPath = Path.GetFullPath(childDirectory);
                        if (IsWithinRoot(rootPath, fullChildPath))
                        {
                            pendingDirectories.Push(fullChildPath);
                        }
                    }
                }
                catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
                {
                    issues.Add(CreateRootIssue(directoryPath, exception.Message));
                }
            }
        }

        return new ProjectRootScanResult(
            Array.AsReadOnly(candidates.ToArray()),
            Array.AsReadOnly(issues.ToArray()));
    }

    private static void AddCandidate(
        string projectFilePath,
        string rootPath,
        ISet<ProjectPath> knownPaths,
        ICollection<ProjectCandidate> candidates)
    {
        var path = new ProjectPath(projectFilePath);
        if (IsWithinRoot(rootPath, path.Value) && knownPaths.Add(path))
        {
            candidates.Add(new ProjectCandidate(path));
        }
    }

    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var relativePath = Path.GetRelativePath(rootPath, candidatePath);
        return !Path.IsPathRooted(relativePath)
               && !string.Equals(relativePath, "..", StringComparison.Ordinal)
               && !relativePath.StartsWith(
                   $"..{Path.DirectorySeparatorChar}",
                   StringComparison.Ordinal);
    }

    private static ProjectDiscoveryIssue CreateRootIssue(
        string path,
        string message) =>
        new(
            TryGetFullPath(path),
            ProjectDiscoveryIssueKind.RootScan,
            message);

    private static string TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
            return path;
        }
    }

    private static bool IsExpectedFileSystemFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException;
}
