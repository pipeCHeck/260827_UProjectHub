using UProjectHub.Windows.Engines.Launcher;

namespace UProjectHub.Core.Tests.Windows.Engines;

[TestClass]
public sealed class LauncherEngineManifestParserTests
{
    [TestMethod]
    public void ValidLauncherEngineManifestParsesRequiredProperties()
    {
        var parser = new LauncherInstalledManifestParser();
        var json = File.ReadAllText(GetFixturePath("LauncherInstalled.valid.json"));

        var result = parser.Parse(json);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Manifest);
        Assert.IsNull(result.Error);
        Assert.HasCount(8, result.Manifest.InstallationList);

        var first = result.Manifest.InstallationList[0];
        Assert.AreEqual("UE_5.8", first.AppName);
        Assert.AreEqual("__UE58_ROOT__", first.InstallLocation);
        Assert.AreEqual(
            "5.8.0-12345678+++UE5+Release-5.8",
            first.AppVersion);
    }

    [TestMethod]
    public void MalformedLauncherEngineManifestReturnsFailureResult()
    {
        var parser = new LauncherInstalledManifestParser();
        var json = File.ReadAllText(
            GetFixturePath("LauncherInstalled.malformed.json"));

        var result = parser.Parse(json);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Manifest);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error));
    }

    private static string GetFixturePath(string fileName) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Fixtures",
            "Windows",
            "Epic",
            fileName));
}
