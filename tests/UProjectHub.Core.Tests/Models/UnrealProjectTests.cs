using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Tests.Models;

[TestClass]
public sealed class UnrealProjectTests
{
    [TestMethod]
    public void UnrealProjectIsAnImmutableMetadataSnapshot()
    {
        var projectPath = new ProjectPath(@"C:\Projects\Sample\Sample.uproject");
        var lastModified = new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero);
        var lastLaunched = new DateTimeOffset(2026, 8, 21, 10, 45, 0, TimeSpan.Zero);

        var project = new UnrealProject(
            Name: "Sample",
            ProjectFilePath: projectPath,
            EngineAssociation: "5.8",
            EngineDisplayVersion: "5.8.2",
            ProjectType: ProjectType.Cpp,
            LastModified: lastModified,
            LastLaunched: lastLaunched,
            IsFavorite: false,
            ProjectState: ProjectState.Available,
            EngineState: EngineResolutionState.Resolved);

        var favoriteSnapshot = project with { IsFavorite = true };

        Assert.AreEqual("Sample", project.Name);
        Assert.AreEqual(projectPath, project.ProjectFilePath);
        Assert.AreEqual(@"C:\Projects\Sample", project.ProjectDirectory);
        Assert.AreEqual("5.8", project.EngineAssociation);
        Assert.AreEqual("5.8.2", project.EngineDisplayVersion);
        Assert.AreEqual(ProjectType.Cpp, project.ProjectType);
        Assert.AreEqual(lastModified, project.LastModified);
        Assert.AreEqual(lastLaunched, project.LastLaunched);
        Assert.IsFalse(project.IsFavorite);
        Assert.IsTrue(favoriteSnapshot.IsFavorite);
        Assert.AreEqual(ProjectState.Available, project.ProjectState);
        Assert.AreEqual(EngineResolutionState.Resolved, project.EngineState);
    }

    [TestMethod]
    public void InstalledEngineIsAnImmutableDiscoverySnapshot()
    {
        var engine = new InstalledEngine(
            DisplayName: "Unreal Engine 5.8",
            Association: "5.8",
            DisplayVersion: "5.8.2",
            RootPath: @"C:\Epic\UE_5.8",
            EditorPath: @"C:\Epic\UE_5.8\Engine\Binaries\Win64\UnrealEditor.exe",
            Source: EngineSource.Launcher,
            IsUsable: true);

        var unavailableSnapshot = engine with { IsUsable = false };

        Assert.AreEqual("Unreal Engine 5.8", engine.DisplayName);
        Assert.AreEqual("5.8", engine.Association);
        Assert.AreEqual("5.8.2", engine.DisplayVersion);
        Assert.AreEqual(@"C:\Epic\UE_5.8", engine.RootPath);
        Assert.AreEqual(
            @"C:\Epic\UE_5.8\Engine\Binaries\Win64\UnrealEditor.exe",
            engine.EditorPath);
        Assert.AreEqual(EngineSource.Launcher, engine.Source);
        Assert.IsTrue(engine.IsUsable);
        Assert.IsFalse(unavailableSnapshot.IsUsable);
    }
}
