using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Windows.Cleanup;
using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.Windows.Cleanup;

[TestClass]
public sealed class ProjectCleanupServiceTests
{
    [TestMethod]
    public async Task InspectReportsOnlyFixedTargetsWithExactPathsAndSizesAsync()
    {
        using var fixture = TemporaryProject.Create("InspectGame");
        fixture.Write("Intermediate/a.bin", 11);
        fixture.Write("Intermediate/Nested/b.bin", 13);
        fixture.Write("DerivedDataCache/cache.bin", 17);
        fixture.Write(".vs/state.bin", 19);
        fixture.Write("Binaries/Win64/game.dll", 23);
        var solution = fixture.Write("InspectGame.sln", 29);
        fixture.Write("Content/Keep.uasset", 31);
        var service = new ProjectCleanupService(new VisualStudioSolutionLocator());

        var inspection = await service.InspectAsync(fixture.Project);

        Assert.HasCount(5, inspection.Items);
        AssertItem(inspection, ProjectCleanupTargetKind.Intermediate,
            Path.Combine(fixture.RootPath, "Intermediate"), 24);
        AssertItem(inspection, ProjectCleanupTargetKind.DerivedDataCache,
            Path.Combine(fixture.RootPath, "DerivedDataCache"), 17);
        AssertItem(inspection, ProjectCleanupTargetKind.VisualStudioWorkspace,
            Path.Combine(fixture.RootPath, ".vs"), 19);
        AssertItem(inspection, ProjectCleanupTargetKind.Binaries,
            Path.Combine(fixture.RootPath, "Binaries"), 23);
        AssertItem(inspection, ProjectCleanupTargetKind.Solution, solution, 29);
    }

    [TestMethod]
    public async Task CleanupDeletesOnlySelectedFixedRootTargetsAndUniqueSolutionAsync()
    {
        using var fixture = TemporaryProject.Create("DeleteGame");
        fixture.Write("Intermediate/delete.bin", 11);
        fixture.Write("DerivedDataCache/keep.bin", 13);
        fixture.Write(".vs/delete.bin", 17);
        fixture.Write("Binaries/Win64/delete.dll", 19);
        fixture.Write("Plugins/Example/Binaries/Win64/keep.dll", 23);
        fixture.Write("Content/Keep.uasset", 29);
        fixture.Write("Config/DefaultGame.ini", 31);
        fixture.Write("Source/Keep.cpp", 37);
        fixture.Write("Saved/Keep.log", 41);
        var solution = fixture.Write("DeleteGame.sln", 43);
        var service = new ProjectCleanupService(new VisualStudioSolutionLocator());

        var result = await service.CleanupAsync(new ProjectCleanupRequest(
            fixture.Project,
            [
                ProjectCleanupTargetKind.Intermediate,
                ProjectCleanupTargetKind.VisualStudioWorkspace,
                ProjectCleanupTargetKind.Binaries,
                ProjectCleanupTargetKind.Solution,
            ]));

        Assert.AreEqual(90, result.DeletedBytes);
        Assert.IsTrue(result.Items.All(item =>
            item.Status == ProjectCleanupItemStatus.Deleted));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.RootPath, "Intermediate")));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.RootPath, ".vs")));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.RootPath, "Binaries")));
        Assert.IsFalse(File.Exists(solution));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.RootPath, "DerivedDataCache", "keep.bin")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.RootPath, "Plugins", "Example", "Binaries", "Win64", "keep.dll")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.RootPath, "Content", "Keep.uasset")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.RootPath, "Config", "DefaultGame.ini")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.RootPath, "Source", "Keep.cpp")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.RootPath, "Saved", "Keep.log")));
        Assert.IsTrue(File.Exists(fixture.Project.ProjectFilePath.Value));
    }

    [TestMethod]
    public async Task MultipleSolutionsAreUnavailableAndNeverDeletedAsync()
    {
        using var fixture = TemporaryProject.Create("AmbiguousGame");
        var first = fixture.Write("First.sln", 5);
        var second = fixture.Write("Second.sln", 7);
        var service = new ProjectCleanupService(new VisualStudioSolutionLocator());

        var inspection = await service.InspectAsync(fixture.Project);
        var result = await service.CleanupAsync(new ProjectCleanupRequest(
            fixture.Project,
            [ProjectCleanupTargetKind.Solution]));

        var item = inspection.Items.Single(entry =>
            entry.Kind == ProjectCleanupTargetKind.Solution);
        Assert.IsFalse(item.CanDelete);
        CollectionAssert.AreEquivalent(
            new[] { first, second },
            item.CandidatePaths.ToArray());
        Assert.AreEqual(ProjectCleanupItemStatus.Unavailable, result.Items.Single().Status);
        Assert.IsTrue(File.Exists(first));
        Assert.IsTrue(File.Exists(second));
    }

    [TestMethod]
    public async Task BlueprintProjectNeverOffersOrDeletesSolutionAsync()
    {
        using var fixture = TemporaryProject.Create("BlueprintGame", ProjectType.Blueprint);
        var solution = fixture.Write("BlueprintGame.sln", 17);
        var service = new ProjectCleanupService(new VisualStudioSolutionLocator());

        var inspection = await service.InspectAsync(fixture.Project);
        var result = await service.CleanupAsync(new ProjectCleanupRequest(
            fixture.Project,
            [ProjectCleanupTargetKind.Solution]));

        var item = inspection.Items.Single(entry =>
            entry.Kind == ProjectCleanupTargetKind.Solution);
        Assert.IsFalse(item.CanDelete);
        Assert.AreEqual(ProjectCleanupItemStatus.Unavailable, result.Items.Single().Status);
        Assert.IsTrue(File.Exists(solution));
    }

    [TestMethod]
    public async Task SolutionOutsideProjectRootIsRejectedEvenWhenLocatorClaimsAvailableAsync()
    {
        using var fixture = TemporaryProject.Create("OutsideSolutionGame");
        var outside = Path.Combine(fixture.ParentPath, "Outside.sln");
        File.WriteAllBytes(outside, new byte[47]);
        var locator = new FixedSolutionLocator(
            VisualStudioSolutionSelection.Available(outside, [outside]));
        var service = new ProjectCleanupService(locator);

        var result = await service.CleanupAsync(new ProjectCleanupRequest(
            fixture.Project,
            [ProjectCleanupTargetKind.Solution]));

        Assert.AreEqual(ProjectCleanupItemStatus.Failed, result.Items.Single().Status);
        Assert.IsTrue(File.Exists(outside));
        File.Delete(outside);
    }

    [TestMethod]
    public async Task InternalReparsePointRejectsTargetWithoutTraversingItsContentsAsync()
    {
        using var fixture = TemporaryProject.Create("ReparseGame");
        fixture.Write("Intermediate/ordinary.bin", 11);
        var linkPath = fixture.CreateDirectory("Intermediate/ExternalLink");
        fixture.Write("Intermediate/ExternalLink/must-not-count.bin", 101);
        var service = new ProjectCleanupService(
            new VisualStudioSolutionLocator(),
            path => string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(linkPath),
                    StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Directory | FileAttributes.ReparsePoint
                : File.GetAttributes(path));

        var inspection = await service.InspectAsync(fixture.Project);
        var result = await service.CleanupAsync(new ProjectCleanupRequest(
            fixture.Project,
            [ProjectCleanupTargetKind.Intermediate]));

        var item = inspection.Items.Single(entry =>
            entry.Kind == ProjectCleanupTargetKind.Intermediate);
        Assert.IsFalse(item.CanDelete);
        Assert.AreEqual(0, item.FileSizeBytes);
        Assert.AreEqual(ProjectCleanupItemStatus.Failed, result.Items.Single().Status);
        Assert.IsTrue(File.Exists(Path.Combine(linkPath, "must-not-count.bin")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.RootPath, "Intermediate", "ordinary.bin")));
    }

    [TestMethod]
    public async Task RealDirectoryJunctionIsNotMeasuredOrFollowedDuringCleanupAsync()
    {
        using var fixture = TemporaryProject.Create("RealLinkGame");
        var externalDirectory = Path.Combine(fixture.ParentPath, "ExternalTarget");
        Directory.CreateDirectory(externalDirectory);
        var externalFile = Path.Combine(externalDirectory, "must-survive.bin");
        File.WriteAllBytes(externalFile, new byte[103]);
        var intermediate = fixture.CreateDirectory("Intermediate");
        var linkPath = Path.Combine(intermediate, "ExternalLink");
        CreateDirectoryJunction(linkPath, externalDirectory);
        try
        {
            var service = new ProjectCleanupService(new VisualStudioSolutionLocator());

            var inspection = await service.InspectAsync(fixture.Project);
            var result = await service.CleanupAsync(new ProjectCleanupRequest(
                fixture.Project,
                [ProjectCleanupTargetKind.Intermediate]));

            var item = inspection.Items.Single(entry =>
                entry.Kind == ProjectCleanupTargetKind.Intermediate);
            Assert.IsFalse(item.CanDelete);
            Assert.AreEqual(0, item.FileSizeBytes);
            Assert.AreEqual(ProjectCleanupItemStatus.Failed, result.Items.Single().Status);
            Assert.IsTrue(File.Exists(externalFile));
            Assert.IsTrue(Directory.Exists(linkPath));
        }
        finally
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }
        }
    }

    [TestMethod]
    public async Task FailureInOneTargetDoesNotPreventLaterSelectedTargetAsync()
    {
        using var fixture = TemporaryProject.Create("PartialGame");
        var lockedPath = fixture.Write("Intermediate/locked.bin", 11);
        fixture.Write("DerivedDataCache/delete.bin", 13);
        var service = new ProjectCleanupService(new VisualStudioSolutionLocator());
        using var locked = new FileStream(
            lockedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var result = await service.CleanupAsync(new ProjectCleanupRequest(
            fixture.Project,
            [
                ProjectCleanupTargetKind.Intermediate,
                ProjectCleanupTargetKind.DerivedDataCache,
            ]));

        Assert.AreEqual(ProjectCleanupItemStatus.Failed, result.Items[0].Status);
        Assert.AreEqual(ProjectCleanupItemStatus.Deleted, result.Items[1].Status);
        Assert.IsTrue(File.Exists(lockedPath));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.RootPath, "DerivedDataCache")));
    }

    private static void AssertItem(
        ProjectCleanupInspection inspection,
        ProjectCleanupTargetKind kind,
        string expectedPath,
        long expectedSize)
    {
        var item = inspection.Items.Single(entry => entry.Kind == kind);
        Assert.IsTrue(item.Exists);
        Assert.IsTrue(item.CanDelete);
        Assert.AreEqual(Path.GetFullPath(expectedPath), item.Path);
        Assert.AreEqual(expectedSize, item.FileSizeBytes);
    }

    private static void CreateDirectoryJunction(string linkPath, string targetPath)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start junction fixture setup.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not create junction fixture: {process.StandardError.ReadToEnd()}");
        }
    }

    private sealed class FixedSolutionLocator(
        VisualStudioSolutionSelection selection) : IVisualStudioSolutionLocator
    {
        public VisualStudioSolutionSelection Locate(UnrealProject project) => selection;
    }

    private sealed class TemporaryProject : IDisposable
    {
        private TemporaryProject(string parentPath, string rootPath, UnrealProject project)
        {
            ParentPath = parentPath;
            RootPath = rootPath;
            Project = project;
        }

        public string ParentPath { get; }

        public string RootPath { get; }

        public UnrealProject Project { get; }

        public static TemporaryProject Create(
            string projectName,
            ProjectType projectType = ProjectType.Cpp)
        {
            var parentPath = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                nameof(ProjectCleanupServiceTests),
                Guid.NewGuid().ToString("N"));
            var rootPath = Path.Combine(parentPath, projectName);
            Directory.CreateDirectory(rootPath);
            var projectPath = Path.Combine(rootPath, $"{projectName}.uproject");
            File.WriteAllText(projectPath, "{}");
            var project = new UnrealProject(
                projectName,
                new ProjectPath(projectPath),
                "5.8",
                "5.8",
                projectType,
                DateTimeOffset.UnixEpoch,
                LastLaunched: null,
                IsFavorite: false,
                ProjectState.Available,
                EngineResolutionState.Resolved);
            return new TemporaryProject(parentPath, rootPath, project);
        }

        public string CreateDirectory(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(path);
            return path;
        }

        public string Write(string relativePath, int size)
        {
            var path = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[size]);
            return Path.GetFullPath(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(ParentPath))
            {
                Directory.Delete(ParentPath, recursive: true);
            }
        }
    }
}
