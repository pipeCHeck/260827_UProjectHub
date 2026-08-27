using System.Text.Json;
using UProjectHub.Core.Models;
using UProjectHub.Windows.Engines.Launcher;

namespace UProjectHub.Core.Tests.Windows.Engines;

[TestClass]
public sealed class LauncherEngineProviderTests
{
    [TestMethod]
    public async Task DiscoversLauncherEnginesAndIsolatesInvalidEntriesAsync()
    {
        using var fixture = TemporaryProgramData.Create();
        var engine58Root = fixture.CreateEngineRoot("UE_5.8", createEditor: true);
        var engine510Root = fixture.CreateEngineRoot("UE_5.10", createEditor: true);
        var missingEditorRoot = fixture.CreateEngineRoot(
            "UE_5.9",
            createEditor: false);
        var engine58ManifestRoot = $"{engine58Root.Replace('\\', '/')}/Unused/../";
        var manifestContents = fixture.WriteValidManifest(
            engine58ManifestRoot,
            engine510Root,
            missingEditorRoot);
        var provider = new LauncherEngineProvider(
            fixture.ManifestPath,
            new LauncherInstalledManifestParser());

        var result = await provider.DiscoverAsync();

        Assert.HasCount(3, result.Engines);
        Assert.HasCount(5, result.Issues);

        var engine58 = FindEngine(result, "5.8");
        AssertLauncherEngine(
            engine58,
            "5.8",
            engine58Root,
            isUsable: true);

        var engine510 = FindEngine(result, "5.10");
        AssertLauncherEngine(
            engine510,
            "5.10",
            engine510Root,
            isUsable: true);

        var engine59 = FindEngine(result, "5.9");
        AssertLauncherEngine(
            engine59,
            "5.9",
            missingEditorRoot,
            isUsable: false);

        Assert.IsFalse(result.Engines.Any(engine =>
            string.Equals(
                engine.DisplayName,
                "Fortnite",
                StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(
            manifestContents,
            File.ReadAllText(fixture.ManifestPath));
    }

    [TestMethod]
    public async Task MalformedLauncherEngineManifestReturnsEmptyResultWithIssueAsync()
    {
        using var fixture = TemporaryProgramData.Create();
        fixture.CopyManifestFixture("LauncherInstalled.malformed.json");
        var provider = new LauncherEngineProvider(
            fixture.ManifestPath,
            new LauncherInstalledManifestParser());

        var result = await provider.DiscoverAsync();

        Assert.HasCount(0, result.Engines);
        Assert.HasCount(1, result.Issues);
        Assert.AreEqual(fixture.ManifestPath, result.Issues[0].Context);
    }

    [TestMethod]
    public async Task MissingLauncherEngineManifestReturnsQuietEmptyResultAsync()
    {
        using var fixture = TemporaryProgramData.Create();
        var provider = new LauncherEngineProvider(
            fixture.ManifestPath,
            new LauncherInstalledManifestParser());

        var result = await provider.DiscoverAsync();

        Assert.HasCount(0, result.Engines);
        Assert.HasCount(0, result.Issues);
    }

    [TestMethod]
    public async Task LauncherEngineDiscoveryPropagatesCancellationAsync()
    {
        using var fixture = TemporaryProgramData.Create();
        fixture.WriteValidManifest(
            fixture.CreateEngineRoot("UE_5.8", createEditor: true),
            fixture.CreateEngineRoot("UE_5.10", createEditor: true),
            fixture.CreateEngineRoot("UE_5.9", createEditor: false));
        var provider = new LauncherEngineProvider(
            fixture.ManifestPath,
            new LauncherInstalledManifestParser());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.DiscoverAsync(cancellation.Token));
    }

    private static InstalledEngine FindEngine(
        UProjectHub.Windows.Engines.EngineProviderResult result,
        string association) =>
        result.Engines.Single(engine => engine.Association == association);

    private static void AssertLauncherEngine(
        InstalledEngine engine,
        string version,
        string expectedRoot,
        bool isUsable)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(expectedRoot));
        Assert.AreEqual($"Unreal Engine {version}", engine.DisplayName);
        Assert.AreEqual(version, engine.Association);
        Assert.AreEqual(version, engine.DisplayVersion);
        Assert.AreEqual(normalizedRoot, engine.RootPath);
        Assert.AreEqual(
            Path.Combine(
                normalizedRoot,
                "Engine",
                "Binaries",
                "Win64",
                "UnrealEditor.exe"),
            engine.EditorPath);
        Assert.AreEqual(EngineSource.Launcher, engine.Source);
        Assert.AreEqual(isUsable, engine.IsUsable);
    }

    private sealed class TemporaryProgramData : IDisposable
    {
        private TemporaryProgramData(string rootPath)
        {
            RootPath = rootPath;
            ManifestPath = Path.Combine(
                rootPath,
                "Epic",
                "UnrealEngineLauncher",
                "LauncherInstalled.dat");
        }

        public string RootPath { get; }

        public string ManifestPath { get; }

        public static TemporaryProgramData Create()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                "LauncherProvider",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return new TemporaryProgramData(rootPath);
        }

        public string CreateEngineRoot(string name, bool createEditor)
        {
            var rootPath = Path.Combine(RootPath, "Engines", name);
            Directory.CreateDirectory(rootPath);
            if (createEditor)
            {
                var editorPath = Path.Combine(
                    rootPath,
                    "Engine",
                    "Binaries",
                    "Win64",
                    "UnrealEditor.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(editorPath)!);
                File.WriteAllText(editorPath, string.Empty);
            }

            return rootPath;
        }

        public string WriteValidManifest(
            string engine58Root,
            string engine510Root,
            string missingEditorRoot)
        {
            var contents = File.ReadAllText(
                GetManifestFixturePath("LauncherInstalled.valid.json"));
            contents = ReplaceJsonToken(contents, "__UE58_ROOT__", engine58Root);
            contents = ReplaceJsonToken(contents, "__UE510_ROOT__", engine510Root);
            contents = ReplaceJsonToken(
                contents,
                "__MISSING_EDITOR_ROOT__",
                missingEditorRoot);
            contents = ReplaceJsonToken(
                contents,
                "__NON_UNREAL_ROOT__",
                Path.Combine(RootPath, "EpicApps", "Fortnite"));
            contents = ReplaceJsonToken(
                contents,
                "__INVALID_VERSION_ROOT__",
                Path.Combine(RootPath, "Engines", "Preview"));
            Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
            File.WriteAllText(ManifestPath, contents);
            return contents;
        }

        public void CopyManifestFixture(string fileName)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
            File.Copy(GetManifestFixturePath(fileName), ManifestPath);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }

        private static string ReplaceJsonToken(
            string contents,
            string token,
            string value) =>
            contents.Replace(
                $"\"{token}\"",
                JsonSerializer.Serialize(value),
                StringComparison.Ordinal);

        private static string GetManifestFixturePath(string fileName) =>
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
}
