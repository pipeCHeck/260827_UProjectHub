using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.Windows.Launching;

[TestClass]
public sealed class ExplorerLauncherTests
{
    [TestMethod]
    public void OpenProjectFolderUsesExplorerWithOneUnquotedPathArgument()
    {
        using var fixture = TemporaryProject.Create("Game Academy", "My Game");
        var process = new FakeProcessLauncher(LaunchResult.Succeeded());
        IExplorerLauncher launcher = new ExplorerLauncher(process);

        var result = launcher.OpenProjectFolder(fixture.Project);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, process.Requests);
        var request = process.Requests[0];
        Assert.AreEqual("explorer.exe", request.FileName);
        CollectionAssert.AreEqual(
            new[] { fixture.Project.ProjectDirectory },
            request.ArgumentList.ToArray());
        Assert.DoesNotStartWith("\"", request.ArgumentList[0]);
        Assert.DoesNotEndWith("\"", request.ArgumentList[0]);
    }

    [TestMethod]
    public void RevealProjectUsesSelectOptionAndExactProjectPathArguments()
    {
        using var fixture = TemporaryProject.Create("Game Academy", "My Game");
        var process = new FakeProcessLauncher(LaunchResult.Succeeded());
        var launcher = new ExplorerLauncher(process);

        var result = launcher.RevealProjectFile(fixture.Project);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, process.Requests);
        var request = process.Requests[0];
        Assert.AreEqual("explorer.exe", request.FileName);
        CollectionAssert.AreEqual(
            new[] { "/select,", fixture.Project.ProjectFilePath.Value },
            request.ArgumentList.ToArray());
        Assert.AreNotEqual(fixture.Project.ProjectFilePath.Value, request.FileName);
        Assert.DoesNotStartWith("\"", request.ArgumentList[1]);
        Assert.DoesNotEndWith("\"", request.ArgumentList[1]);
    }

    [TestMethod]
    public void MissingFolderOrProjectFileDoesNotStartExplorer()
    {
        using var fixture = TemporaryProject.Create("Missing", "Missing");
        File.Delete(fixture.Project.ProjectFilePath.Value);
        var revealProcess = new FakeProcessLauncher(LaunchResult.Succeeded());
        var revealLauncher = new ExplorerLauncher(revealProcess);

        var revealResult = revealLauncher.RevealProjectFile(fixture.Project);

        Assert.IsFalse(revealResult.IsSuccess);
        Assert.HasCount(0, revealProcess.Requests);

        Directory.Delete(fixture.Project.ProjectDirectory, recursive: true);
        var folderProcess = new FakeProcessLauncher(LaunchResult.Succeeded());
        var folderLauncher = new ExplorerLauncher(folderProcess);

        var folderResult = folderLauncher.OpenProjectFolder(fixture.Project);

        Assert.IsFalse(folderResult.IsSuccess);
        Assert.HasCount(0, folderProcess.Requests);
    }

    [TestMethod]
    public void ProcessFailureIsReturnedByExplorerLauncher()
    {
        using var fixture = TemporaryProject.Create("Project", "Project");
        var process = new FakeProcessLauncher(
            LaunchResult.Failed("Explorer failed."));
        var launcher = new ExplorerLauncher(process);

        var result = launcher.OpenProjectFolder(fixture.Project);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("Explorer failed.", result.ErrorMessage);
        Assert.HasCount(1, process.Requests);
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

        public static TemporaryProject Create(
            string directoryName,
            string projectName)
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                "ExplorerLauncher",
                Guid.NewGuid().ToString("N"));
            var projectPath = Path.Combine(
                rootPath,
                directoryName,
                $"{projectName}.uproject");
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(projectPath, "{}");
            var project = new UnrealProject(
                Name: projectName,
                ProjectFilePath: new ProjectPath(projectPath),
                EngineAssociation: "5.8",
                EngineDisplayVersion: "5.8",
                ProjectType: ProjectType.Cpp,
                LastModified: DateTimeOffset.UnixEpoch,
                LastLaunched: null,
                IsFavorite: false,
                ProjectState: ProjectState.Available,
                EngineState: EngineResolutionState.Resolved);
            return new TemporaryProject(rootPath, project);
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
