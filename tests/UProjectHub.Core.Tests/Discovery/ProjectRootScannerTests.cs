using UProjectHub.Core.Discovery;
using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Tests.Discovery;

[TestClass]
public sealed class ProjectRootScannerTests
{
    [TestMethod]
    public async Task ScanFindsNestedProjectsDeduplicatesAndIsolatesDirectoriesAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "UProjectHub.Tests", "ScanRoot");
        var validDirectory = Path.Combine(root, "Valid");
        var nestedDirectory = Path.Combine(root, "Nested", "Deep");
        var inaccessibleDirectory = Path.Combine(root, "Inaccessible");
        var reparseDirectory = Path.Combine(root, "ExternalLink");
        var missingRoot = Path.Combine(Path.GetTempPath(), "UProjectHub.Tests", "MissingRoot");
        var validProject = Path.Combine(validDirectory, "Valid.uproject");
        var duplicateProject = Path.Combine(
                validDirectory.ToUpperInvariant(),
                "Temporary",
                "..",
                "VALID.UPROJECT")
            .Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var nestedProject = Path.Combine(nestedDirectory, "Nested.uproject");
        var skippedProject = Path.Combine(reparseDirectory, "Skipped.uproject");
        var enumerator = new FakeProjectDirectoryEnumerator();
        enumerator.AddRoot(root);
        enumerator.AddDirectory(root, validDirectory);
        enumerator.AddDirectory(root, Path.GetDirectoryName(nestedDirectory)!);
        enumerator.AddDirectory(Path.GetDirectoryName(nestedDirectory)!, nestedDirectory);
        enumerator.AddDirectory(root, inaccessibleDirectory);
        enumerator.SetInaccessible(inaccessibleDirectory);
        enumerator.AddDirectory(root, reparseDirectory, isReparsePoint: true);
        enumerator.AddProjectFile(validDirectory, validProject);
        enumerator.AddProjectFile(validDirectory, duplicateProject);
        enumerator.AddProjectFile(nestedDirectory, nestedProject);
        enumerator.AddProjectFile(reparseDirectory, skippedProject);
        var scanner = new ProjectRootScanner(enumerator);

        var result = await scanner.ScanAsync([root, missingRoot]);

        Assert.HasCount(2, result.Candidates);
        Assert.IsTrue(result.Candidates.Any(candidate =>
            candidate.ProjectFilePath.Equals(new ProjectPath(validProject))));
        Assert.IsTrue(result.Candidates.Any(candidate =>
            candidate.ProjectFilePath.Equals(new ProjectPath(nestedProject))));
        Assert.IsFalse(result.Candidates.Any(candidate =>
            candidate.ProjectFilePath.Equals(new ProjectPath(skippedProject))));
        Assert.HasCount(2, result.Issues);
        Assert.IsTrue(result.Issues.Any(issue =>
            string.Equals(
                issue.Path,
                Path.GetFullPath(inaccessibleDirectory),
                StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Issues.Any(issue =>
            string.Equals(
                issue.Path,
                Path.GetFullPath(missingRoot),
                StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(enumerator.TraversedDirectories.Any(path =>
            string.Equals(
                path,
                Path.GetFullPath(reparseDirectory),
                StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task CancellationIsPropagatedAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "UProjectHub.Tests", "CancelledRoot");
        var enumerator = new FakeProjectDirectoryEnumerator();
        enumerator.AddRoot(root);
        var scanner = new ProjectRootScanner(enumerator);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            scanner.ScanAsync([root], cancellation.Token));
    }

    [TestMethod]
    public async Task ShallowScanFindsRootAndImmediateChildButNotDeeperProjectsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "UProjectHub.Tests", "ShallowRoot");
        var child = Path.Combine(root, "Game");
        var grandchild = Path.Combine(child, "Nested");
        var rootProject = Path.Combine(root, "RootGame.uproject");
        var childProject = Path.Combine(child, "Game.uproject");
        var deepProject = Path.Combine(grandchild, "Nested.uproject");
        var enumerator = new FakeProjectDirectoryEnumerator();
        enumerator.AddRoot(root);
        enumerator.AddDirectory(root, child);
        enumerator.AddDirectory(child, grandchild);
        enumerator.AddProjectFile(root, rootProject);
        enumerator.AddProjectFile(child, childProject);
        enumerator.AddProjectFile(grandchild, deepProject);
        var scanner = new ProjectRootScanner(enumerator);

        var result = await scanner.ScanShallowAsync([root]);

        CollectionAssert.AreEquivalent(
            new[] { new ProjectPath(rootProject), new ProjectPath(childProject) },
            result.Candidates.Select(candidate => candidate.ProjectFilePath).ToArray());
        Assert.IsFalse(enumerator.TraversedDirectories.Any(path =>
            string.Equals(path, grandchild, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ShallowScanDeduplicatesOverlappingRootsAndExcludesKnownProjectAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "UProjectHub.Tests", "ShallowDedupe");
        var child = Path.Combine(root, "Game");
        var cachedProject = new ProjectPath(Path.Combine(root, "Cached.uproject"));
        var childProject = new ProjectPath(Path.Combine(child, "Game.uproject"));
        var enumerator = new FakeProjectDirectoryEnumerator();
        enumerator.AddRoot(root);
        enumerator.AddDirectory(root, child);
        enumerator.AddProjectFile(root, cachedProject.Value);
        enumerator.AddProjectFile(child, childProject.Value);
        var scanner = new ProjectRootScanner(enumerator);

        var result = await scanner.ScanShallowAsync(
            [root, Path.Combine(root, "."), child],
            [cachedProject]);

        Assert.HasCount(1, result.Candidates);
        Assert.AreEqual(childProject, result.Candidates.Single().ProjectFilePath);
    }

    [TestMethod]
    public async Task ShallowScanIsolatesInaccessibleChildAndContinuesOtherRootsAsync()
    {
        var firstRoot = Path.Combine(Path.GetTempPath(), "UProjectHub.Tests", "BlockedRoot");
        var inaccessible = Path.Combine(firstRoot, "Blocked");
        var secondRoot = Path.Combine(Path.GetTempPath(), "UProjectHub.Tests", "HealthyRoot");
        var healthyProject = new ProjectPath(Path.Combine(secondRoot, "Healthy.uproject"));
        var enumerator = new FakeProjectDirectoryEnumerator();
        enumerator.AddRoot(firstRoot);
        enumerator.AddDirectory(firstRoot, inaccessible);
        enumerator.SetInaccessible(inaccessible);
        enumerator.AddRoot(secondRoot);
        enumerator.AddProjectFile(secondRoot, healthyProject.Value);
        var scanner = new ProjectRootScanner(enumerator);

        var result = await scanner.ScanShallowAsync([firstRoot, secondRoot]);

        Assert.AreEqual(healthyProject, result.Candidates.Single().ProjectFilePath);
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(
            issue.Path,
            Path.GetFullPath(inaccessible),
            StringComparison.OrdinalIgnoreCase)));
    }
}
