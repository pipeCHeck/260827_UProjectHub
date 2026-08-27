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
}
