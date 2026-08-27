using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;
using UProjectHub.Core.Tests.Windows.Registry;
using UProjectHub.Windows.Engines;
using UProjectHub.Windows.Engines.SourceBuild;
using UProjectHub.Windows.Registry;

namespace UProjectHub.Core.Tests.Windows.Engines;

[TestClass]
public sealed class SourceBuildEngineProviderTests
{
    private const string RegisteredBuildsSubKey =
        @"SOFTWARE\Epic Games\Unreal Engine\Builds";

    [TestMethod]
    public async Task ValidRegisteredBuildsAreNormalizedAndAllRetainedAsync()
    {
        using var fixture = TemporaryEngineTree.Create();
        var firstRoot = fixture.CreateEngineRoot("First", createEditor: true);
        var secondRoot = fixture.CreateEngineRoot("Second", createEditor: true);
        var firstRegistryPath = $"{firstRoot.Replace('\\', '/')}/Unused/../";
        var reader = FakeRegistryReader.ForCurrentUserKey(
            RegisteredBuildsSubKey,
            new RegistryValueEntry(
                "{01234567-89AB-CDEF-0123-456789ABCDEF}",
                firstRegistryPath),
            new RegistryValueEntry(
                "89abcdef-0123-4567-89ab-cdef01234567",
                secondRoot));
        IEngineProvider provider = new SourceBuildEngineProvider(reader);

        var result = await provider.DiscoverAsync();

        Assert.HasCount(2, result.Engines);
        Assert.HasCount(0, result.Issues);
        AssertEngine(
            result.Engines[0],
            "{01234567-89ab-cdef-0123-456789abcdef}",
            firstRoot,
            isUsable: true);
        AssertEngine(
            result.Engines[1],
            "{89abcdef-0123-4567-89ab-cdef01234567}",
            secondRoot,
            isUsable: true);
        Assert.IsInstanceOfType<GuidEngineAssociation>(
            EngineAssociationParser.Parse(result.Engines[0].Association));
    }

    [TestMethod]
    public async Task MissingEditorKeepsUnusableCandidateAndReportsIssueAsync()
    {
        using var fixture = TemporaryEngineTree.Create();
        var root = fixture.CreateEngineRoot("Stale", createEditor: false);
        var reader = FakeRegistryReader.ForCurrentUserKey(
            RegisteredBuildsSubKey,
            new RegistryValueEntry(
                "{11111111-2222-3333-4444-555555555555}",
                root));
        var provider = new SourceBuildEngineProvider(reader);

        var result = await provider.DiscoverAsync();

        Assert.HasCount(1, result.Engines);
        Assert.IsFalse(result.Engines[0].IsUsable);
        Assert.HasCount(1, result.Issues);
        Assert.AreEqual(result.Engines[0].EditorPath, result.Issues[0].Context);
    }

    [TestMethod]
    public async Task InvalidRegistryValuesDoNotDiscardValidCandidateAsync()
    {
        using var fixture = TemporaryEngineTree.Create();
        var validRoot = fixture.CreateEngineRoot("Valid", createEditor: true);
        var reader = FakeRegistryReader.ForCurrentUserKey(
            RegisteredBuildsSubKey,
            new RegistryValueEntry(
                "{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}",
                validRoot),
            new RegistryValueEntry("MyCustomEngine", validRoot),
            new RegistryValueEntry(
                "{10000000-0000-0000-0000-000000000001}",
                null),
            new RegistryValueEntry(
                "{10000000-0000-0000-0000-000000000002}",
                42),
            new RegistryValueEntry(
                "{10000000-0000-0000-0000-000000000003}",
                "   "),
            new RegistryValueEntry(
                "{10000000-0000-0000-0000-000000000004}",
                @"relative\Engine"));
        var provider = new SourceBuildEngineProvider(reader);

        var result = await provider.DiscoverAsync();

        Assert.HasCount(1, result.Engines);
        Assert.AreEqual(
            "{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}",
            result.Engines[0].Association);
        Assert.HasCount(5, result.Issues);
    }

    [TestMethod]
    public async Task MissingRegistryKeyReturnsQuietEmptyResultAsync()
    {
        var provider = new SourceBuildEngineProvider(FakeRegistryReader.Empty());

        var result = await provider.DiscoverAsync();

        Assert.HasCount(0, result.Engines);
        Assert.HasCount(0, result.Issues);
    }

    [TestMethod]
    public async Task RegistryReadFailureReturnsEmptyResultWithIssueAsync()
    {
        var provider = new SourceBuildEngineProvider(
            FakeRegistryReader.Throwing(
                new UnauthorizedAccessException("Registry access denied.")));

        var result = await provider.DiscoverAsync();

        Assert.HasCount(0, result.Engines);
        Assert.HasCount(1, result.Issues);
        Assert.AreEqual(RegisteredBuildsSubKey, result.Issues[0].Context);
    }

    [TestMethod]
    public async Task CancellationIsPropagatedAsync()
    {
        var provider = new SourceBuildEngineProvider(FakeRegistryReader.Empty());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.DiscoverAsync(cancellation.Token));
    }

    private static void AssertEngine(
        InstalledEngine engine,
        string expectedAssociation,
        string expectedRoot,
        bool isUsable)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(expectedRoot));
        Assert.AreEqual(
            $"Unreal Engine Source Build {expectedAssociation}",
            engine.DisplayName);
        Assert.AreEqual(expectedAssociation, engine.Association);
        Assert.IsNull(engine.DisplayVersion);
        Assert.AreEqual(normalizedRoot, engine.RootPath);
        Assert.AreEqual(
            Path.Combine(
                normalizedRoot,
                "Engine",
                "Binaries",
                "Win64",
                "UnrealEditor.exe"),
            engine.EditorPath);
        Assert.AreEqual(EngineSource.SourceBuild, engine.Source);
        Assert.AreEqual(isUsable, engine.IsUsable);
    }

    private sealed class TemporaryEngineTree : IDisposable
    {
        private TemporaryEngineTree(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TemporaryEngineTree Create()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                "SourceBuildProvider",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return new TemporaryEngineTree(rootPath);
        }

        public string CreateEngineRoot(string name, bool createEditor)
        {
            var rootPath = Path.Combine(RootPath, name);
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

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
