namespace UProjectHub.Windows.Storage;

public sealed class LocalAppDataPathProvider : ILocalAppDataPathProvider
{
    private readonly string _localApplicationDataDirectory;

    public LocalAppDataPathProvider()
        : this(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData))
    {
    }

    public LocalAppDataPathProvider(string localApplicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);
        _localApplicationDataDirectory = Path.GetFullPath(
            localApplicationDataDirectory);
    }

    public AppDataPaths GetPaths()
    {
        var rootDirectory = Path.Combine(
            _localApplicationDataDirectory,
            "UProjectHub");
        var logDirectory = Path.Combine(rootDirectory, "logs");

        return new AppDataPaths(
            rootDirectory,
            Path.Combine(rootDirectory, "settings.json"),
            Path.Combine(rootDirectory, "settings.json.bak"),
            Path.Combine(rootDirectory, "project-cache.json"),
            Path.Combine(rootDirectory, "engine-cache.json"),
            logDirectory,
            Path.Combine(logDirectory, "app.log"));
    }
}
