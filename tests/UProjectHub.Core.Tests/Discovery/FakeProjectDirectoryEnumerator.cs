using UProjectHub.Core.Discovery;

namespace UProjectHub.Core.Tests.Discovery;

public sealed class FakeProjectDirectoryEnumerator : IProjectDirectoryEnumerator
{
    private readonly Dictionary<string, DirectoryNode> _directories =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> TraversedDirectories =>
        _directories.Values
            .Where(node => node.WasTraversed)
            .Select(node => node.Path)
            .ToArray();

    public void AddRoot(string path)
    {
        GetOrAdd(path);
    }

    public void AddDirectory(
        string parentPath,
        string childPath,
        bool isReparsePoint = false)
    {
        var parent = GetOrAdd(parentPath);
        var child = GetOrAdd(childPath);
        child.IsReparsePoint = isReparsePoint;
        parent.Directories.Add(child.Path);
    }

    public void AddProjectFile(string directoryPath, string projectFilePath)
    {
        GetOrAdd(directoryPath).ProjectFiles.Add(projectFilePath);
    }

    public void SetInaccessible(string directoryPath)
    {
        GetOrAdd(directoryPath).IsInaccessible = true;
    }

    public bool DirectoryExists(string directoryPath) =>
        _directories.ContainsKey(Normalize(directoryPath));

    public bool IsReparsePoint(string directoryPath) =>
        GetRequired(directoryPath).IsReparsePoint;

    public IEnumerable<string> EnumerateProjectFiles(string directoryPath)
    {
        var node = BeginTraversal(directoryPath);
        return node.ProjectFiles.ToArray();
    }

    public IEnumerable<string> EnumerateDirectories(string directoryPath)
    {
        var node = BeginTraversal(directoryPath);
        return node.Directories.ToArray();
    }

    private DirectoryNode BeginTraversal(string directoryPath)
    {
        var node = GetRequired(directoryPath);
        node.WasTraversed = true;
        if (node.IsInaccessible)
        {
            throw new UnauthorizedAccessException($"Inaccessible: {node.Path}");
        }

        return node;
    }

    private DirectoryNode GetOrAdd(string path)
    {
        var normalizedPath = Normalize(path);
        if (!_directories.TryGetValue(normalizedPath, out var node))
        {
            node = new DirectoryNode(normalizedPath);
            _directories.Add(normalizedPath, node);
        }

        return node;
    }

    private DirectoryNode GetRequired(string path) =>
        _directories[Normalize(path)];

    private static string Normalize(string path) =>
        Path.GetFullPath(path)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private sealed class DirectoryNode(string path)
    {
        public string Path { get; } = path;

        public List<string> ProjectFiles { get; } = [];

        public List<string> Directories { get; } = [];

        public bool IsReparsePoint { get; set; }

        public bool IsInaccessible { get; set; }

        public bool WasTraversed { get; set; }
    }
}
