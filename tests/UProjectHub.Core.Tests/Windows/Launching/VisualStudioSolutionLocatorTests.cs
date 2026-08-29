using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.Windows.Launching;

[TestClass]
public sealed class VisualStudioSolutionLocatorTests
{
    [TestMethod]
    public void LocatePrefersProjectNamedSolutionWhenSeveralExist()
    {
        using var fixture = TemporaryProject.Create("MyGame");
        var expected = fixture.CreateSolution("mygame.SLN");
        fixture.CreateSolution("Tools.sln");
        var locator = new VisualStudioSolutionLocator();

        var result = locator.Locate(fixture.Project);

        Assert.AreEqual(VisualStudioSolutionState.Available, result.State);
        Assert.AreEqual(expected, result.SolutionPath);
        Assert.HasCount(2, result.CandidatePaths);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void LocateReturnsMissingWhenNoTopLevelSolutionExists()
    {
        using var fixture = TemporaryProject.Create("MissingSolution");
        var locator = new VisualStudioSolutionLocator();

        var result = locator.Locate(fixture.Project);

        Assert.AreEqual(VisualStudioSolutionState.Missing, result.State);
        Assert.IsNull(result.SolutionPath);
        Assert.IsEmpty(result.CandidatePaths);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void LocateReturnsMultipleWhenNoUniqueSolutionCanBeSelected()
    {
        using var fixture = TemporaryProject.Create("NoNamedSolution");
        var first = fixture.CreateSolution("First.sln");
        var second = fixture.CreateSolution("Second.sln");
        var locator = new VisualStudioSolutionLocator();

        var result = locator.Locate(fixture.Project);

        Assert.AreEqual(VisualStudioSolutionState.Multiple, result.State);
        Assert.IsNull(result.SolutionPath);
        CollectionAssert.AreEquivalent(
            new[] { first, second },
            result.CandidatePaths.ToArray());
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void LocateReturnsInaccessibleWhenProjectDirectoryDoesNotExist()
    {
        using var fixture = TemporaryProject.Create("Unavailable");
        Directory.Delete(fixture.Project.ProjectDirectory, recursive: true);
        var locator = new VisualStudioSolutionLocator();

        var result = locator.Locate(fixture.Project);

        Assert.AreEqual(VisualStudioSolutionState.Inaccessible, result.State);
        Assert.IsNull(result.SolutionPath);
        Assert.IsEmpty(result.CandidatePaths);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    private sealed class TemporaryProject : IDisposable
    {
        private TemporaryProject(string rootPath, UnrealProject project)
        {
            RootPath = rootPath;
            Project = project;
        }

        public string RootPath { get; }

        public UnrealProject Project { get; }

        public static TemporaryProject Create(string projectName)
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                nameof(VisualStudioSolutionLocatorTests),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var projectPath = Path.Combine(rootPath, $"{projectName}.uproject");
            File.WriteAllText(projectPath, "{}");
            var project = new UnrealProject(
                projectName,
                new ProjectPath(projectPath),
                "5.8",
                "5.8",
                ProjectType.Cpp,
                DateTimeOffset.UnixEpoch,
                LastLaunched: null,
                IsFavorite: false,
                ProjectState.Available,
                EngineResolutionState.Resolved);
            return new TemporaryProject(rootPath, project);
        }

        public string CreateSolution(string fileName)
        {
            var path = Path.GetFullPath(Path.Combine(RootPath, fileName));
            File.WriteAllText(path, "Microsoft Visual Studio Solution File");
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
