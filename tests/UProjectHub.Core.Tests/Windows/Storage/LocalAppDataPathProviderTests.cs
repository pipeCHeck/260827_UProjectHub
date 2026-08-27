using UProjectHub.Windows.Storage;

namespace UProjectHub.Core.Tests.Windows.Storage;

[TestClass]
public sealed class LocalAppDataPathProviderTests
{
    [TestMethod]
    public void InjectedBaseProducesAllSpecifiedUProjectHubPaths()
    {
        const string localAppData = @"C:\Users\Test\AppData\Local";
        var provider = new LocalAppDataPathProvider(localAppData);

        var paths = provider.GetPaths();

        var expectedRoot = Path.Combine(localAppData, "UProjectHub");
        Assert.AreEqual(expectedRoot, paths.RootDirectory);
        Assert.AreEqual(
            Path.Combine(expectedRoot, "settings.json"),
            paths.SettingsFile);
        Assert.AreEqual(
            Path.Combine(expectedRoot, "settings.json.bak"),
            paths.SettingsBackupFile);
        Assert.AreEqual(
            Path.Combine(expectedRoot, "project-cache.json"),
            paths.ProjectCacheFile);
        Assert.AreEqual(
            Path.Combine(expectedRoot, "engine-cache.json"),
            paths.EngineCacheFile);
        Assert.AreEqual(Path.Combine(expectedRoot, "logs"), paths.LogDirectory);
        Assert.AreEqual(
            Path.Combine(expectedRoot, "logs", "app.log"),
            paths.LogFile);
    }

    [TestMethod]
    public void GettingPathsDoesNotCreateDirectoriesOrFiles()
    {
        var basePath = Path.Combine(
            Path.GetTempPath(),
            "UProjectHub.Tests",
            "AppDataPaths",
            Guid.NewGuid().ToString("N"));
        var provider = new LocalAppDataPathProvider(basePath);

        var paths = provider.GetPaths();

        Assert.IsFalse(Directory.Exists(basePath));
        Assert.IsFalse(Directory.Exists(paths.RootDirectory));
        Assert.IsFalse(File.Exists(paths.SettingsFile));
        Assert.IsFalse(File.Exists(paths.ProjectCacheFile));
        Assert.IsFalse(File.Exists(paths.EngineCacheFile));
        Assert.IsFalse(File.Exists(paths.LogFile));
    }
}
