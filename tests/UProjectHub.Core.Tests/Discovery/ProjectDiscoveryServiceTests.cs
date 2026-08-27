using UProjectHub.Core.Activity;
using UProjectHub.Core.Discovery;
using UProjectHub.Core.Models;
using UProjectHub.Core.Parsing;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Settings;

namespace UProjectHub.Core.Tests.Discovery;

[TestClass]
public sealed class ProjectDiscoveryServiceTests
{
    private static readonly DateTimeOffset Baseline =
        new(2026, 8, 27, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task DiscoverIsolatesBrokenProjectAndMergesUserStateAsync()
    {
        using var fixture = TemporaryDiscoveryFixture.Create();
        fixture.SetAllFileTimestamps(Baseline);
        var validPath = new ProjectPath(fixture.ProjectPath("Valid", "Valid.uproject"));
        var lastLaunched = Baseline.AddDays(-1);
        var settings = new AppSettings
        {
            ProjectUserStates =
            [
                new ProjectUserState(
                    new ProjectPath(validPath.Value.ToUpperInvariant()),
                    IsFavorite: true,
                    LastLaunched: lastLaunched),
            ],
        };
        var service = CreateService();

        var result = await service.DiscoverAsync([fixture.RootPath], settings);

        Assert.HasCount(3, result.Projects);
        Assert.HasCount(1, result.Issues);

        var valid = result.Projects.Single(project => project.Name == "Valid");
        Assert.AreEqual(ProjectState.Available, valid.ProjectState);
        Assert.AreEqual(ProjectType.Cpp, valid.ProjectType);
        Assert.AreEqual("5.10", valid.EngineAssociation);
        Assert.AreEqual("5.10", valid.EngineDisplayVersion);
        Assert.AreEqual(EngineResolutionState.Unknown, valid.EngineState);
        Assert.AreEqual(Baseline, valid.LastModified);
        Assert.IsTrue(valid.IsFavorite);
        Assert.AreEqual(lastLaunched, valid.LastLaunched);

        var nested = result.Projects.Single(project => project.Name == "Nested");
        Assert.AreEqual(ProjectState.Available, nested.ProjectState);
        Assert.AreEqual(ProjectType.Blueprint, nested.ProjectType);
        Assert.AreEqual("5.9", nested.EngineDisplayVersion);
        Assert.AreEqual(Baseline, nested.LastModified);
        Assert.IsFalse(nested.IsFavorite);
        Assert.IsNull(nested.LastLaunched);

        var broken = result.Projects.Single(project => project.Name == "Broken");
        Assert.AreEqual(ProjectState.Broken, broken.ProjectState);
        Assert.AreEqual(ProjectType.Blueprint, broken.ProjectType);
        Assert.IsNull(broken.EngineAssociation);
        Assert.IsNull(broken.EngineDisplayVersion);
        Assert.AreEqual(EngineResolutionState.Unknown, broken.EngineState);
        Assert.AreEqual(Baseline, broken.LastModified);
        Assert.AreEqual(
            ProjectDiscoveryIssueKind.MetadataLoad,
            result.Issues[0].Kind);
        Assert.AreEqual(broken.ProjectFilePath.Value, result.Issues[0].Path);
    }

    [TestMethod]
    public async Task MissingRootIssueDoesNotPreventOtherRootDiscoveryAsync()
    {
        using var fixture = TemporaryDiscoveryFixture.Create();
        fixture.SetAllFileTimestamps(Baseline);
        var missingRoot = Path.Combine(fixture.RootPath, "DoesNotExist");
        var service = CreateService();

        var result = await service.DiscoverAsync(
            [missingRoot, fixture.RootPath],
            new AppSettings());

        Assert.HasCount(3, result.Projects);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Kind == ProjectDiscoveryIssueKind.RootScan
            && string.Equals(
                issue.Path,
                Path.GetFullPath(missingRoot),
                StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task CancellationIsPropagatedAsync()
    {
        using var fixture = TemporaryDiscoveryFixture.Create();
        var service = CreateService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.DiscoverAsync(
                [fixture.RootPath],
                new AppSettings(),
                cancellation.Token));
    }

    private static ProjectDiscoveryService CreateService()
    {
        var scanner = new ProjectRootScanner(new SystemProjectDirectoryEnumerator());
        var loader = new ProjectMetadataLoader(
            new UProjectParser(),
            new ProjectActivityDetector(new ProjectActivityPolicy()));
        return new ProjectDiscoveryService(scanner, loader);
    }

    private sealed class TemporaryDiscoveryFixture : IDisposable
    {
        private TemporaryDiscoveryFixture(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TemporaryDiscoveryFixture Create()
        {
            var sourceRoot = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "Fixtures",
                "Discovery",
                "MixedRoot"));
            var temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                "Discovery",
                Guid.NewGuid().ToString("N"));
            CopyDirectory(sourceRoot, temporaryRoot);
            return new TemporaryDiscoveryFixture(temporaryRoot);
        }

        public string ProjectPath(params string[] relativePath) =>
            Path.Combine([RootPath, .. relativePath]);

        public void SetAllFileTimestamps(DateTimeOffset timestamp)
        {
            foreach (var filePath in Directory.EnumerateFiles(
                RootPath,
                "*",
                SearchOption.AllDirectories))
            {
                File.SetLastWriteTimeUtc(filePath, timestamp.UtcDateTime);
            }
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var directoryPath in Directory.EnumerateDirectories(
                source,
                "*",
                SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(
                    destination,
                    Path.GetRelativePath(source, directoryPath)));
            }

            foreach (var filePath in Directory.EnumerateFiles(
                source,
                "*",
                SearchOption.AllDirectories))
            {
                File.Copy(
                    filePath,
                    Path.Combine(destination, Path.GetRelativePath(source, filePath)));
            }
        }
    }
}
