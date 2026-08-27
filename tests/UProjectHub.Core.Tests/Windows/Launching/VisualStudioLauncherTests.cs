using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.Windows.Launching;

[TestClass]
public sealed class VisualStudioLauncherTests
{
    [TestMethod]
    public void ProjectNamedSolutionIsPreferredCaseInsensitively()
    {
        using var fixture = TemporarySolutionProject.Create("Game Academy", "MyGame");
        var namedSolution = fixture.CreateSolution("mygame.SLN", "named solution");
        fixture.CreateSolution("Other.sln", "other solution");
        var process = new FakeProcessLauncher(LaunchResult.Succeeded());
        IVisualStudioLauncher launcher = new VisualStudioLauncher(process);

        var result = launcher.OpenSolution(fixture.Project);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, process.Requests);
        var request = process.Requests[0];
        Assert.AreEqual(namedSolution, request.FileName);
        Assert.HasCount(0, request.ArgumentList);
        Assert.IsTrue(request.UseShellExecute);
        Assert.AreEqual("named solution", File.ReadAllText(namedSolution));
    }

    [TestMethod]
    public void ExactlyOneTopLevelSolutionIsUsedAsFallback()
    {
        using var fixture = TemporarySolutionProject.Create("Project", "MyGame");
        var onlySolution = fixture.CreateSolution("Only Solution.sln", "solution");
        var process = new FakeProcessLauncher(LaunchResult.Succeeded());
        var launcher = new VisualStudioLauncher(process);

        var result = launcher.OpenSolution(fixture.Project);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, process.Requests);
        Assert.AreEqual(onlySolution, process.Requests[0].FileName);
        Assert.HasCount(0, process.Requests[0].ArgumentList);
    }

    [TestMethod]
    public void ZeroOrMultipleTopLevelSolutionsAreUnavailable()
    {
        using var emptyFixture = TemporarySolutionProject.Create("Empty", "Empty");
        var emptyProcess = new FakeProcessLauncher(LaunchResult.Succeeded());
        var emptyLauncher = new VisualStudioLauncher(emptyProcess);

        var emptyResult = emptyLauncher.OpenSolution(emptyFixture.Project);

        Assert.IsFalse(emptyResult.IsSuccess);
        Assert.HasCount(0, emptyProcess.Requests);

        using var multipleFixture = TemporarySolutionProject.Create(
            "Multiple",
            "NoNamedSolution");
        multipleFixture.CreateSolution("First.sln", "first");
        multipleFixture.CreateSolution("Second.sln", "second");
        var multipleProcess = new FakeProcessLauncher(LaunchResult.Succeeded());
        var multipleLauncher = new VisualStudioLauncher(multipleProcess);

        var multipleResult = multipleLauncher.OpenSolution(
            multipleFixture.Project);

        Assert.IsFalse(multipleResult.IsSuccess);
        Assert.HasCount(0, multipleProcess.Requests);
    }

    [TestMethod]
    public void BlueprintProjectDoesNotOpenExistingSolution()
    {
        using var fixture = TemporarySolutionProject.Create(
            "Blueprint",
            "BlueprintProject",
            ProjectType.Blueprint);
        fixture.CreateSolution("BlueprintProject.sln", "solution");
        var process = new FakeProcessLauncher(LaunchResult.Succeeded());
        var launcher = new VisualStudioLauncher(process);

        var result = launcher.OpenSolution(fixture.Project);

        Assert.IsFalse(result.IsSuccess);
        Assert.HasCount(0, process.Requests);
    }

    [TestMethod]
    public void ChildDirectorySolutionIsNotDiscoveredRecursively()
    {
        using var fixture = TemporarySolutionProject.Create("Nested", "Nested");
        fixture.CreateNestedSolution("Child", "Nested.sln");
        var process = new FakeProcessLauncher(LaunchResult.Succeeded());
        var launcher = new VisualStudioLauncher(process);

        var result = launcher.OpenSolution(fixture.Project);

        Assert.IsFalse(result.IsSuccess);
        Assert.HasCount(0, process.Requests);
    }

    private sealed class TemporarySolutionProject : IDisposable
    {
        private TemporarySolutionProject(string rootPath, UnrealProject project)
        {
            RootPath = rootPath;
            Project = project;
        }

        public string RootPath { get; }

        public UnrealProject Project { get; }

        public static TemporarySolutionProject Create(
            string directoryName,
            string projectName,
            ProjectType projectType = ProjectType.Cpp)
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                "VisualStudioLauncher",
                Guid.NewGuid().ToString("N"),
                directoryName);
            Directory.CreateDirectory(rootPath);
            var projectPath = Path.Combine(rootPath, $"{projectName}.uproject");
            File.WriteAllText(projectPath, "{}");
            var project = new UnrealProject(
                Name: projectName,
                ProjectFilePath: new ProjectPath(projectPath),
                EngineAssociation: "5.8",
                EngineDisplayVersion: "5.8",
                ProjectType: projectType,
                LastModified: DateTimeOffset.UnixEpoch,
                LastLaunched: null,
                IsFavorite: false,
                ProjectState: ProjectState.Available,
                EngineState: EngineResolutionState.Resolved);
            return new TemporarySolutionProject(rootPath, project);
        }

        public string CreateSolution(string fileName, string contents)
        {
            var solutionPath = Path.Combine(Project.ProjectDirectory, fileName);
            File.WriteAllText(solutionPath, contents);
            return Path.GetFullPath(solutionPath);
        }

        public string CreateNestedSolution(string directoryName, string fileName)
        {
            var directory = Path.Combine(Project.ProjectDirectory, directoryName);
            Directory.CreateDirectory(directory);
            var solutionPath = Path.Combine(directory, fileName);
            File.WriteAllText(solutionPath, "nested solution");
            return solutionPath;
        }

        public void Dispose()
        {
            Directory.Delete(
                Path.GetFullPath(Path.Combine(RootPath, "..")),
                recursive: true);
        }
    }
}
