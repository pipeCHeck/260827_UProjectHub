using System.Collections.ObjectModel;

namespace UProjectHub.Core.Activity;

public sealed class ProjectActivityPolicy
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(
        [
            "Binaries",
            "DerivedDataCache",
            "Intermediate",
            "Saved",
            ".vs",
            ".idea",
            ".vscode",
            ".git",
        ],
        StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> IncludedRootNames { get; } =
        new ReadOnlyCollection<string>(["Content", "Config", "Source", "Plugins"]);

    public bool ShouldTraverseDirectory(string directoryName, FileAttributes attributes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);

        return (attributes & FileAttributes.ReparsePoint) == 0
            && !ExcludedDirectoryNames.Contains(directoryName);
    }
}
