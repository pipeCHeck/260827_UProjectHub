using UProjectHub.Core.Activity;

namespace UProjectHub.Core.Tests.Activity;

[TestClass]
public sealed class ProjectActivityDetectorTests
{
    private static readonly DateTimeOffset Baseline =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ProjectDescriptorTimestampIsIncludedAsync()
    {
        using var project = TemporaryActivityProject.Create();
        project.SetAllFileTimestamps(Baseline);
        var expected = Baseline.AddHours(1);
        project.SetTimestamp("ActivityProject.uproject", expected);

        var actual = await CreateDetector().GetLastModifiedUtcAsync(project.ProjectFilePath);

        AssertTimestamp(expected, actual);
    }

    [TestMethod]
    [DataRow("Content", "Asset.uasset")]
    [DataRow("Config", "DefaultGame.ini")]
    [DataRow("Source", "Game.cpp")]
    [DataRow("Plugins", "TestPlugin", "Content", "PluginAsset.uasset")]
    public async Task IncludedRootTimestampIsReflectedAsync(params string[] relativePath)
    {
        using var project = TemporaryActivityProject.Create();
        project.SetAllFileTimestamps(Baseline);
        var expected = Baseline.AddHours(2);
        project.SetTimestamp(relativePath, expected);

        var actual = await CreateDetector().GetLastModifiedUtcAsync(project.ProjectFilePath);

        AssertTimestamp(expected, actual);
    }

    [TestMethod]
    public async Task SavedAndIntermediateTimestampsAreExcludedAsync()
    {
        using var project = TemporaryActivityProject.Create();
        project.SetAllFileTimestamps(Baseline);
        var expected = Baseline.AddHours(1);
        project.SetTimestamp(["Content", "Asset.uasset"], expected);
        project.SetTimestamp(["Saved", "Logs", "Latest.log"], Baseline.AddDays(2));
        project.SetTimestamp(["Intermediate", "Generated.txt"], Baseline.AddDays(3));

        var actual = await CreateDetector().GetLastModifiedUtcAsync(project.ProjectFilePath);

        AssertTimestamp(expected, actual);
    }

    [TestMethod]
    public async Task ExcludedDirectoryNamesAreMatchedAsExactSegmentsAsync()
    {
        using var project = TemporaryActivityProject.Create();
        project.SetAllFileTimestamps(Baseline);
        var expected = Baseline.AddHours(2);
        project.CreateFile(["Content", "SavedGame", "Meaningful.uasset"], expected);

        string[] excludedDirectoryNames =
        [
            "Binaries",
            "DerivedDataCache",
            "Intermediate",
            "Saved",
            ".vs",
            ".idea",
            ".vscode",
            ".git",
        ];

        foreach (var directoryName in excludedDirectoryNames)
        {
            project.CreateFile(
                ["Content", directoryName, "Ignored.dat"],
                Baseline.AddDays(5));
        }

        var actual = await CreateDetector().GetLastModifiedUtcAsync(project.ProjectFilePath);

        AssertTimestamp(expected, actual);
    }

    [TestMethod]
    public void ReparsePointDirectoriesAreNotTraversed()
    {
        var policy = new ProjectActivityPolicy();

        Assert.IsFalse(policy.ShouldTraverseDirectory(
            "ExternalContent",
            FileAttributes.Directory | FileAttributes.ReparsePoint));
        Assert.IsTrue(policy.ShouldTraverseDirectory(
            "ExternalContent",
            FileAttributes.Directory));
    }

    [TestMethod]
    public async Task CancellationIsObservedAsync()
    {
        using var project = TemporaryActivityProject.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateDetector().GetLastModifiedUtcAsync(
                project.ProjectFilePath,
                cancellation.Token));
    }

    [TestMethod]
    public async Task ResultUsesUtcTimestampAsync()
    {
        using var project = TemporaryActivityProject.Create();
        project.SetAllFileTimestamps(Baseline);
        var localTimestamp = new DateTimeOffset(2026, 1, 2, 9, 30, 0, TimeSpan.FromHours(9));
        project.SetTimestamp(["Config", "DefaultGame.ini"], localTimestamp);

        var actual = await CreateDetector().GetLastModifiedUtcAsync(project.ProjectFilePath);

        AssertTimestamp(localTimestamp.ToUniversalTime(), actual);
        Assert.AreEqual(TimeSpan.Zero, actual!.Value.Offset);
    }

    [TestMethod]
    public async Task MissingIncludedRootsAreSkippedAsync()
    {
        using var project = TemporaryActivityProject.Create();
        project.SetAllFileTimestamps(Baseline);
        Directory.Delete(Path.Combine(project.ProjectDirectory, "Source"), recursive: true);
        Directory.Delete(Path.Combine(project.ProjectDirectory, "Plugins"), recursive: true);

        var actual = await CreateDetector().GetLastModifiedUtcAsync(project.ProjectFilePath);

        AssertTimestamp(Baseline, actual);
    }

    private static ProjectActivityDetector CreateDetector() =>
        new(new ProjectActivityPolicy());

    private static void AssertTimestamp(DateTimeOffset expected, DateTimeOffset? actual)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.ToUniversalTime(), actual.Value);
    }

    private sealed class TemporaryActivityProject : IDisposable
    {
        private TemporaryActivityProject(string projectDirectory)
        {
            ProjectDirectory = projectDirectory;
        }

        public string ProjectDirectory { get; }

        public string ProjectFilePath =>
            Path.Combine(ProjectDirectory, "ActivityProject.uproject");

        public static TemporaryActivityProject Create()
        {
            var fixtureRoot = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "Fixtures",
                "Activity",
                "ActivityProject"));
            var temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                Guid.NewGuid().ToString("N"));

            CopyDirectory(fixtureRoot, temporaryRoot);
            return new TemporaryActivityProject(temporaryRoot);
        }

        public void SetAllFileTimestamps(DateTimeOffset timestamp)
        {
            foreach (var filePath in Directory.EnumerateFiles(
                ProjectDirectory,
                "*",
                SearchOption.AllDirectories))
            {
                File.SetLastWriteTimeUtc(filePath, timestamp.UtcDateTime);
            }
        }

        public void SetTimestamp(string relativePath, DateTimeOffset timestamp) =>
            SetTimestamp([relativePath], timestamp);

        public void SetTimestamp(string[] relativePath, DateTimeOffset timestamp)
        {
            File.SetLastWriteTimeUtc(
                Path.Combine([ProjectDirectory, .. relativePath]),
                timestamp.UtcDateTime);
        }

        public void CreateFile(string[] relativePath, DateTimeOffset timestamp)
        {
            var filePath = Path.Combine([ProjectDirectory, .. relativePath]);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "activity fixture");
            File.SetLastWriteTimeUtc(filePath, timestamp.UtcDateTime);
        }

        public void Dispose()
        {
            Directory.Delete(ProjectDirectory, recursive: true);
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (var directoryPath in Directory.EnumerateDirectories(
                sourceDirectory,
                "*",
                SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(
                    destinationDirectory,
                    Path.GetRelativePath(sourceDirectory, directoryPath)));
            }

            foreach (var filePath in Directory.EnumerateFiles(
                sourceDirectory,
                "*",
                SearchOption.AllDirectories))
            {
                File.Copy(
                    filePath,
                    Path.Combine(
                        destinationDirectory,
                        Path.GetRelativePath(sourceDirectory, filePath)));
            }
        }
    }
}
