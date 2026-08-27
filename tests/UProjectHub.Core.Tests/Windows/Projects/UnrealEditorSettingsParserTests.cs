using UProjectHub.Core.Paths;
using UProjectHub.Windows.Projects;

namespace UProjectHub.Core.Tests.Windows.Projects;

[TestClass]
public sealed class UnrealEditorSettingsParserTests
{
    [TestMethod]
    public void ParsesRepeatedCreatedProjectPathsAndIgnoresOtherIniContent()
    {
        var parser = new UnrealEditorSettingsParser();
        var contents = File.ReadAllText(GetFixturePath());

        var roots = parser.ParseCreatedProjectPaths(contents);

        Assert.HasCount(3, roots);
        CollectionAssert.AreEqual(
            new[]
            {
                @"D:\Unreal",
                @"D:\Game Academy",
                "C:/Users/Test/Documents/Unreal Projects",
            },
            roots.ToArray());
    }

    [TestMethod]
    public void TrimsKeyAndValueWhitespaceAndIgnoresEmptyValues()
    {
        var parser = new UnrealEditorSettingsParser();
        const string contents =
            "  CreatedProjectPaths  =  D:\\Game Academy  \r\n"
            + "CreatedProjectPaths=   \r\n";

        var roots = parser.ParseCreatedProjectPaths(contents);

        Assert.HasCount(1, roots);
        Assert.AreEqual(@"D:\Game Academy", roots[0]);
    }

    [TestMethod]
    public async Task ProviderReadsModernAndLegacySettingsAcrossVersionsAndDeduplicatesAsync()
    {
        using var fixture = TemporaryLocalAppData.Create();
        var modern58 = fixture.CopyFixture("5.8", "WindowsEditor");
        fixture.WriteSettings(
            "5.8",
            "Windows",
            """
            [/Script/UnrealEd.EditorSettings]
            CreatedProjectPaths=d:/unreal/
            CreatedProjectPaths=E:\Legacy Root
            """);
        fixture.WriteSettings(
            "5.9",
            "WindowsEditor",
            """
            [/Script/UnrealEd.EditorSettings]
            CreatedProjectPaths=D:\Games\..\Unreal
            CreatedProjectPaths=F:\Other
            """);
        fixture.WriteSettings(
            "NotAVersion",
            "WindowsEditor",
            @"CreatedProjectPaths=Z:\Ignored");
        fixture.CreateVersionDirectory("5.10");
        fixture.WriteUnrelatedProjectFile(
            "5.9",
            @"CreatedProjectPaths=Z:\NotScanned");
        var originalModernContents = File.ReadAllText(modern58);
        var provider = new UnrealKnownProjectRootProvider(
            fixture.RootPath,
            new UnrealEditorSettingsParser());

        var result = await provider.GetKnownRootsAsync();

        Assert.HasCount(5, result.Roots);
        Assert.HasCount(0, result.Issues);
        AssertContains(result.Roots, @"D:\Unreal");
        AssertContains(result.Roots, @"D:\Game Academy");
        AssertContains(result.Roots, "C:/Users/Test/Documents/Unreal Projects");
        AssertContains(result.Roots, @"E:\Legacy Root");
        AssertContains(result.Roots, @"F:\Other");
        AssertDoesNotContain(result.Roots, @"Z:\Ignored");
        AssertDoesNotContain(result.Roots, @"Z:\NotScanned");
        Assert.AreEqual(originalModernContents, File.ReadAllText(modern58));
    }

    [TestMethod]
    public async Task MissingUnrealEngineDirectoryReturnsEmptyResultAsync()
    {
        using var fixture = TemporaryLocalAppData.Create();
        var provider = new UnrealKnownProjectRootProvider(
            fixture.RootPath,
            new UnrealEditorSettingsParser());

        var result = await provider.GetKnownRootsAsync();

        Assert.HasCount(0, result.Roots);
        Assert.HasCount(0, result.Issues);
    }

    [TestMethod]
    public async Task UnreadableSettingsFileDoesNotDiscardOtherVersionRootsAsync()
    {
        using var fixture = TemporaryLocalAppData.Create();
        fixture.WriteSettings(
            "5.8",
            "WindowsEditor",
            @"CreatedProjectPaths=D:\Available");
        var unreadablePath = fixture.WriteSettings(
            "5.9",
            "WindowsEditor",
            @"CreatedProjectPaths=E:\Unavailable");
        using var lockStream = new FileStream(
            unreadablePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        var provider = new UnrealKnownProjectRootProvider(
            fixture.RootPath,
            new UnrealEditorSettingsParser());

        var result = await provider.GetKnownRootsAsync();

        Assert.HasCount(1, result.Roots);
        AssertContains(result.Roots, @"D:\Available");
        Assert.HasCount(1, result.Issues);
        Assert.AreEqual(unreadablePath, result.Issues[0].Path);
        Assert.IsTrue(File.Exists(unreadablePath));
    }

    [TestMethod]
    public async Task CancellationIsPropagatedAsync()
    {
        using var fixture = TemporaryLocalAppData.Create();
        fixture.WriteSettings(
            "5.8",
            "WindowsEditor",
            @"CreatedProjectPaths=D:\Unreal");
        var provider = new UnrealKnownProjectRootProvider(
            fixture.RootPath,
            new UnrealEditorSettingsParser());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GetKnownRootsAsync(cancellation.Token));
    }

    private static string GetFixturePath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Fixtures",
            "Windows",
            "UnrealEngine",
            "5.8",
            "Saved",
            "Config",
            "WindowsEditor",
            "EditorSettings.ini"));

    private static void AssertContains(
        IReadOnlyList<ProjectPath> roots,
        string expectedPath) =>
        Assert.IsTrue(roots.Contains(CreateRootIdentity(expectedPath)));

    private static void AssertDoesNotContain(
        IReadOnlyList<ProjectPath> roots,
        string unexpectedPath) =>
        Assert.IsFalse(roots.Contains(CreateRootIdentity(unexpectedPath)));

    private static ProjectPath CreateRootIdentity(string path) =>
        new(Path.TrimEndingDirectorySeparator(path));

    private sealed class TemporaryLocalAppData : IDisposable
    {
        private TemporaryLocalAppData(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TemporaryLocalAppData Create()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                "KnownRoots",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return new TemporaryLocalAppData(rootPath);
        }

        public string CopyFixture(string version, string platform)
        {
            var destination = GetSettingsPath(version, platform);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(GetFixturePath(), destination);
            return destination;
        }

        public string WriteSettings(
            string version,
            string platform,
            string contents)
        {
            var path = GetSettingsPath(version, platform);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            return path;
        }

        public void CreateVersionDirectory(string version)
        {
            Directory.CreateDirectory(Path.Combine(RootPath, "UnrealEngine", version));
        }

        public void WriteUnrelatedProjectFile(string version, string contents)
        {
            var projectDirectory = Path.Combine(
                RootPath,
                "UnrealEngine",
                version,
                "NestedProject");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(
                Path.Combine(projectDirectory, "Nested.uproject"),
                contents);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }

        private string GetSettingsPath(string version, string platform) =>
            Path.Combine(
                RootPath,
                "UnrealEngine",
                version,
                "Saved",
                "Config",
                platform,
                "EditorSettings.ini");
    }
}
