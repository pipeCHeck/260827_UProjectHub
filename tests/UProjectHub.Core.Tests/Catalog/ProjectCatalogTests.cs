using UProjectHub.Core.Catalog;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Tests.Catalog;

[TestClass]
public sealed class ProjectCatalogTests
{
    [TestMethod]
    public void UpsertUsesCanonicalCaseInsensitivePathIdentity()
    {
        var catalog = new ProjectCatalog();
        var basePath = Path.Combine(
            Path.GetTempPath(),
            "UProjectHub.Tests",
            "CatalogIdentity",
            "Game",
            "Game.uproject");
        var alternatePath = Path.Combine(
                Path.GetDirectoryName(basePath)!.ToUpperInvariant(),
                "Temporary",
                "..",
                "GAME.UPROJECT")
            .Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        catalog.Upsert(CreateProject(new ProjectPath(basePath), "Original"));

        catalog.Upsert(CreateProject(new ProjectPath(alternatePath), "Updated"));

        var snapshot = catalog.GetSnapshot();
        Assert.HasCount(1, snapshot.Projects);
        Assert.AreEqual("Updated", snapshot.Projects[0].Name);
    }

    [TestMethod]
    public void MarkMissingPreservesMetadataAndKeepsProjectInDefaultSnapshot()
    {
        var catalog = new ProjectCatalog();
        var project = CreateProject(
            new ProjectPath(Path.Combine(Path.GetTempPath(), "Preserved.uproject")),
            "Preserved");
        catalog.Upsert(project);

        var marked = catalog.MarkMissing(project.ProjectFilePath);

        Assert.IsTrue(marked);
        var snapshot = catalog.GetSnapshot();
        Assert.HasCount(1, snapshot.Projects);
        Assert.AreEqual(
            project with { ProjectState = ProjectState.Missing },
            snapshot.Projects[0]);
    }

    [TestMethod]
    public void MarkMissingUnknownPathIsNoOp()
    {
        var catalog = new ProjectCatalog();
        var existing = CreateProject(
            new ProjectPath(Path.Combine(Path.GetTempPath(), "Existing.uproject")),
            "Existing");
        catalog.Upsert(existing);

        var marked = catalog.MarkMissing(
            new ProjectPath(Path.Combine(Path.GetTempPath(), "Unknown.uproject")));

        Assert.IsFalse(marked);
        CollectionAssert.AreEqual(
            new[] { existing },
            catalog.GetSnapshot().Projects.ToArray());
    }

    [TestMethod]
    public void SnapshotIsIndependentAndReadOnly()
    {
        var catalog = new ProjectCatalog();
        var original = CreateProject(
            new ProjectPath(Path.Combine(Path.GetTempPath(), "Snapshot.uproject")),
            "Original");
        catalog.Upsert(original);
        var snapshot = catalog.GetSnapshot();

        catalog.Upsert(original with { Name = "Updated" });

        Assert.HasCount(1, snapshot.Projects);
        Assert.AreEqual("Original", snapshot.Projects[0].Name);
        var collection = (ICollection<UnrealProject>)snapshot.Projects;
        Assert.IsTrue(collection.IsReadOnly);
        Assert.ThrowsExactly<NotSupportedException>(() => collection.Add(original));
    }

    [TestMethod]
    public void RemoveDeletesOnlyMatchingCatalogEntry()
    {
        var catalog = new ProjectCatalog();
        var removed = CreateProject(
            new ProjectPath(Path.Combine(Path.GetTempPath(), "Removed.uproject")),
            "Removed");
        var retained = CreateProject(
            new ProjectPath(Path.Combine(Path.GetTempPath(), "Retained.uproject")),
            "Retained");
        catalog.Upsert(removed);
        catalog.Upsert(retained);

        var result = catalog.Remove(removed.ProjectFilePath);

        Assert.IsTrue(result);
        CollectionAssert.AreEqual(
            new[] { retained },
            catalog.GetSnapshot().Projects.ToArray());
    }

    private static UnrealProject CreateProject(
        ProjectPath projectPath,
        string name,
        ProjectState state = ProjectState.Available) =>
        new(
            name,
            projectPath,
            EngineAssociation: "5.10",
            EngineDisplayVersion: "5.10.1",
            ProjectType.Cpp,
            new DateTimeOffset(2026, 8, 27, 1, 2, 3, TimeSpan.Zero),
            LastLaunched: new DateTimeOffset(2026, 8, 26, 1, 2, 3, TimeSpan.Zero),
            IsFavorite: true,
            state,
            EngineResolutionState.Resolved);
}
