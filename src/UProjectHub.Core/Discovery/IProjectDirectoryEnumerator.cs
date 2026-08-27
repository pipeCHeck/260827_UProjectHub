namespace UProjectHub.Core.Discovery;

public interface IProjectDirectoryEnumerator
{
    bool DirectoryExists(string directoryPath);

    bool IsReparsePoint(string directoryPath);

    IEnumerable<string> EnumerateProjectFiles(string directoryPath);

    IEnumerable<string> EnumerateDirectories(string directoryPath);
}
