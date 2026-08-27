namespace UProjectHub.Core.Discovery;

public sealed class SystemProjectDirectoryEnumerator : IProjectDirectoryEnumerator
{
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public bool DirectoryExists(string directoryPath) =>
        Directory.Exists(directoryPath);

    public bool IsReparsePoint(string directoryPath) =>
        (File.GetAttributes(directoryPath) & FileAttributes.ReparsePoint) != 0;

    public IEnumerable<string> EnumerateProjectFiles(string directoryPath) =>
        Directory.EnumerateFiles(directoryPath, "*.uproject", EnumerationOptions);

    public IEnumerable<string> EnumerateDirectories(string directoryPath) =>
        Directory.EnumerateDirectories(directoryPath, "*", EnumerationOptions);
}
