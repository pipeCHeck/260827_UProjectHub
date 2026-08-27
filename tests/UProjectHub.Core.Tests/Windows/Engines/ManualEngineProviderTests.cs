using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;
using UProjectHub.Core.Settings;
using UProjectHub.Windows.Engines.Manual;

namespace UProjectHub.Core.Tests.Windows.Engines;

[TestClass]
public sealed class ManualEngineProviderTests
{
    [TestMethod]
    public async Task ValidManualRootsUseBuildVersionMetadataAsync()
    {
        using var fixture = TemporaryManualEngines.Create();
        var engine58 = fixture.CreateEngine(
            "FolderNameMustNotSupplyVersion",
            """
            {
              "MajorVersion": 5,
              "MinorVersion": 8,
              "PatchVersion": 2
            }
            """,
            createEditor: true);
        var engine510 = fixture.CreateEngine(
            "AnotherUnversionedFolder",
            """
            {
              "MajorVersion": 5,
              "MinorVersion": 10
            }
            """,
            createEditor: true);
        var settings = new AppSettings
        {
            ManualEngineRoots = [engine58.RootPath, engine510.RootPath],
        };
        var provider = new ManualEngineProvider(
            settings,
            new ManualEngineValidator());

        var result = await provider.DiscoverAsync();

        Assert.HasCount(2, result.Engines);
        Assert.HasCount(0, result.Issues);
        AssertManualEngine(result.Engines[0], engine58, "5.8.2", true);
        AssertManualEngine(result.Engines[1], engine510, "5.10", true);

        var resolution = EngineResolver.Resolve("5.8", result.Engines);
        Assert.AreEqual(EngineResolutionState.Resolved, resolution.State);
        Assert.AreEqual("5.8.2", resolution.ResolvedCandidate?.DisplayVersion);
    }

    [TestMethod]
    public async Task MissingEditorKeepsVersionedCandidateButMarksItUnusableAsync()
    {
        using var fixture = TemporaryManualEngines.Create();
        var engine = fixture.CreateEngine(
            "MissingEditor",
            """
            {
              "MajorVersion": 5,
              "MinorVersion": 9,
              "PatchVersion": 1
            }
            """,
            createEditor: false);
        var provider = CreateProvider(engine.RootPath);

        var result = await provider.DiscoverAsync();

        Assert.HasCount(1, result.Engines);
        AssertManualEngine(result.Engines[0], engine, "5.9.1", false);
        Assert.HasCount(1, result.Issues);
        Assert.AreEqual(engine.EditorPath, result.Issues[0].Context);
    }

    [TestMethod]
    public async Task MissingOrMalformedBuildVersionKeepsOnlyDiagnosticCandidatesAsync()
    {
        using var fixture = TemporaryManualEngines.Create();
        var missing = fixture.CreateEngine(
            "UE_9.9_FolderNameMustNotBeInferred",
            buildVersionJson: null,
            createEditor: true);
        var malformed = fixture.CreateEngine(
            "Malformed",
            """{ "MajorVersion": "5", "MinorVersion": 8 }""",
            createEditor: true);
        var negative = fixture.CreateEngine(
            "Negative",
            """{ "MajorVersion": 5, "MinorVersion": -1 }""",
            createEditor: true);
        var provider = CreateProvider(
            missing.RootPath,
            malformed.RootPath,
            negative.RootPath);

        var result = await provider.DiscoverAsync();

        Assert.HasCount(3, result.Engines);
        Assert.HasCount(3, result.Issues);
        foreach (var engine in result.Engines)
        {
            Assert.IsNull(engine.Association);
            Assert.IsNull(engine.DisplayVersion);
            Assert.IsFalse(engine.IsUsable);
            Assert.AreEqual(EngineSource.Manual, engine.Source);
        }
    }

    [TestMethod]
    public async Task InvalidRootsDoNotDiscardValidManualEngineAsync()
    {
        using var fixture = TemporaryManualEngines.Create();
        var valid = fixture.CreateEngine(
            "Valid",
            """{ "MajorVersion": 5, "MinorVersion": 8 }""",
            createEditor: true);
        var provider = CreateProvider(
            string.Empty,
            "   ",
            @"relative\Engine",
            valid.RootPath);

        var result = await provider.DiscoverAsync();

        Assert.HasCount(1, result.Engines);
        Assert.AreEqual("5.8", result.Engines[0].DisplayVersion);
        Assert.HasCount(3, result.Issues);
    }

    [TestMethod]
    public async Task DiscoveryPropagatesCancellationAsync()
    {
        var provider = CreateProvider(@"C:\ManualEngine");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.DiscoverAsync(cancellation.Token));
    }

    [TestMethod]
    public async Task DiscoveryDoesNotModifyBuildVersionOrEditorAsync()
    {
        using var fixture = TemporaryManualEngines.Create();
        const string buildVersion =
            """{ "MajorVersion": 5, "MinorVersion": 8, "PatchVersion": 3 }""";
        const string editorContents = "test editor marker";
        var engine = fixture.CreateEngine(
            "ReadOnlyContract",
            buildVersion,
            createEditor: true,
            editorContents);
        var provider = CreateProvider(engine.RootPath);

        await provider.DiscoverAsync();

        Assert.AreEqual(buildVersion, File.ReadAllText(engine.BuildVersionPath));
        Assert.AreEqual(editorContents, File.ReadAllText(engine.EditorPath));
    }

    private static ManualEngineProvider CreateProvider(params string[] roots) =>
        new(
            new AppSettings { ManualEngineRoots = roots },
            new ManualEngineValidator());

    private static void AssertManualEngine(
        InstalledEngine engine,
        ManualEngineFiles expected,
        string expectedVersion,
        bool expectedUsable)
    {
        Assert.AreEqual(
            $"Unreal Engine {expectedVersion} (Manual)",
            engine.DisplayName);
        Assert.AreEqual(expectedVersion, engine.Association);
        Assert.AreEqual(expectedVersion, engine.DisplayVersion);
        Assert.AreEqual(expected.RootPath, engine.RootPath);
        Assert.AreEqual(expected.EditorPath, engine.EditorPath);
        Assert.AreEqual(EngineSource.Manual, engine.Source);
        Assert.AreEqual(expectedUsable, engine.IsUsable);
    }

    private sealed class TemporaryManualEngines : IDisposable
    {
        private TemporaryManualEngines(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TemporaryManualEngines Create()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                "ManualEngineProvider",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return new TemporaryManualEngines(rootPath);
        }

        public ManualEngineFiles CreateEngine(
            string name,
            string? buildVersionJson,
            bool createEditor,
            string editorContents = "")
        {
            var rootPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.Combine(RootPath, name)));
            Directory.CreateDirectory(rootPath);

            var buildVersionPath = Path.Combine(
                rootPath,
                "Engine",
                "Build",
                "Build.version");
            if (buildVersionJson is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(buildVersionPath)!);
                File.WriteAllText(buildVersionPath, buildVersionJson);
            }

            var editorPath = Path.Combine(
                rootPath,
                "Engine",
                "Binaries",
                "Win64",
                "UnrealEditor.exe");
            if (createEditor)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(editorPath)!);
                File.WriteAllText(editorPath, editorContents);
            }

            return new ManualEngineFiles(rootPath, buildVersionPath, editorPath);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }

    private sealed record ManualEngineFiles(
        string RootPath,
        string BuildVersionPath,
        string EditorPath);
}
