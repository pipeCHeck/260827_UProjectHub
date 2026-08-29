using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Tests.Time;
using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.Windows.Launching;

[TestClass]
public sealed class UnrealEditorLauncherTests
{
    [TestMethod]
    public void ResolvedEngineLaunchesExplicitEditorWithOneProjectArgument()
    {
        using var fixture = TemporaryLaunchTree.Create();
        var editorPath = fixture.CreateEditor("UE 5.8 (Custom)");
        var projectPath = fixture.CreateProject("Game Academy", "My Game");
        var project = CreateProject(projectPath);
        var resolution = EngineResolver.Resolve(
            "5.8",
            [CreateEngine(editorPath, isUsable: true)]);
        var process = new FakeProcessLauncher(LaunchResult.Succeeded());
        var launchTime = new DateTimeOffset(
            2026,
            8,
            27,
            11,
            45,
            0,
            TimeSpan.Zero);
        var clock = new FakeClock(launchTime);
        IUnrealEditorLauncher launcher = new UnrealEditorLauncher(process, clock);

        var result = launcher.Launch(project, resolution);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(launchTime, result.LaunchedAtUtc);
        Assert.HasCount(1, process.Requests);
        var request = process.Requests[0];
        Assert.AreEqual(editorPath, request.FileName);
        Assert.HasCount(1, request.ArgumentList);
        Assert.AreEqual(projectPath, request.ArgumentList[0]);
        Assert.DoesNotStartWith("\"", request.ArgumentList[0]);
        Assert.DoesNotEndWith("\"", request.ArgumentList[0]);
        Assert.IsFalse(request.UseShellExecute);
    }

    [TestMethod]
    public void ProcessFailureDoesNotRecordSuccessfulLaunchTimestamp()
    {
        using var fixture = TemporaryLaunchTree.Create();
        var editorPath = fixture.CreateEditor("FailedStart");
        var project = CreateProject(fixture.CreateProject("Project", "Project"));
        var resolution = EngineResolver.Resolve(
            "5.8",
            [CreateEngine(editorPath, isUsable: true)]);
        var process = new FakeProcessLauncher(
            LaunchResult.Failed("Process start failed."));
        var clock = new FakeClock(
            new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
        var launcher = new UnrealEditorLauncher(process, clock);

        var result = launcher.Launch(project, resolution);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.LaunchedAtUtc);
        Assert.AreEqual("Process start failed.", result.ErrorMessage);
        Assert.HasCount(1, process.Requests);
    }

    [TestMethod]
    public void MissingAmbiguousAndUnknownResolutionDoNotStartProcess()
    {
        using var fixture = TemporaryLaunchTree.Create();
        var editorPath = fixture.CreateEditor("ResolutionStates");
        var project = CreateProject(fixture.CreateProject("Project", "Project"));
        var engine = CreateEngine(editorPath, isUsable: true);
        EngineResolution[] resolutions =
        [
            EngineResolver.Resolve("5.8", []),
            EngineResolver.Resolve("5.8", [engine, engine with
            {
                RootPath = fixture.CreateDirectory("SecondEngine"),
                EditorPath = fixture.CreateEditor("SecondEngine"),
            }]),
            EngineResolver.Resolve(null, [engine]),
        ];

        foreach (var resolution in resolutions)
        {
            var process = new FakeProcessLauncher(LaunchResult.Succeeded());
            var launcher = new UnrealEditorLauncher(
                process,
                new FakeClock(DateTimeOffset.UnixEpoch));

            var result = launcher.Launch(project, resolution);

            Assert.IsFalse(result.IsSuccess);
            Assert.HasCount(0, process.Requests);
        }
    }

    [TestMethod]
    public void MissingEditorAfterResolutionDoesNotStartProcess()
    {
        using var fixture = TemporaryLaunchTree.Create();
        var editorPath = fixture.CreateEditor("RemovedEditor");
        var resolution = EngineResolver.Resolve(
            "5.8",
            [CreateEngine(editorPath, isUsable: true)]);
        File.Delete(editorPath);
        var process = new FakeProcessLauncher(LaunchResult.Succeeded());
        var launcher = new UnrealEditorLauncher(
            process,
            new FakeClock(DateTimeOffset.UnixEpoch));
        var project = CreateProject(fixture.CreateProject("Project", "Project"));

        var result = launcher.Launch(project, resolution);

        Assert.IsFalse(result.IsSuccess);
        Assert.HasCount(0, process.Requests);
    }

    [TestMethod]
    public void UnusableEngineCannotProduceALaunchRequest()
    {
        using var fixture = TemporaryLaunchTree.Create();
        var editorPath = fixture.CreateEditor("UnusableEditor");
        var resolution = EngineResolver.Resolve(
            "5.8",
            [CreateEngine(editorPath, isUsable: false)]);
        var process = new FakeProcessLauncher(LaunchResult.Succeeded());
        var launcher = new UnrealEditorLauncher(
            process,
            new FakeClock(DateTimeOffset.UnixEpoch));
        var project = CreateProject(fixture.CreateProject("Project", "Project"));

        var result = launcher.Launch(project, resolution);

        Assert.AreEqual(EngineResolutionState.Missing, resolution.State);
        Assert.IsFalse(result.IsSuccess);
        Assert.HasCount(0, process.Requests);
    }

    [TestMethod]
    public void NewProjectLaunchesSelectedEditorWithoutAProjectArgument()
    {
        using var fixture = TemporaryLaunchTree.Create();
        var editorPath = fixture.CreateEditor("UE 5.10");
        var process = new FakeProcessLauncher(LaunchResult.Succeeded());
        IUnrealEditorLauncher launcher = new UnrealEditorLauncher(
            process,
            new FakeClock(DateTimeOffset.UnixEpoch));

        var result = launcher.LaunchNewProject(
            CreateEngine(editorPath, isUsable: true));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.LaunchedAtUtc);
        Assert.HasCount(1, process.Requests);
        Assert.AreEqual(editorPath, process.Requests[0].FileName);
        Assert.IsEmpty(process.Requests[0].ArgumentList);
        Assert.IsFalse(process.Requests[0].UseShellExecute);
    }

    [TestMethod]
    public void NewProjectRejectsAnUnusableOrMissingEditor()
    {
        using var fixture = TemporaryLaunchTree.Create();
        var editorPath = fixture.CreateEditor("UE 5.10");
        var process = new FakeProcessLauncher(LaunchResult.Succeeded());
        IUnrealEditorLauncher launcher = new UnrealEditorLauncher(
            process,
            new FakeClock(DateTimeOffset.UnixEpoch));

        var unusable = launcher.LaunchNewProject(
            CreateEngine(editorPath, isUsable: false));
        File.Delete(editorPath);
        var missing = launcher.LaunchNewProject(
            CreateEngine(editorPath, isUsable: true));

        Assert.IsFalse(unusable.IsSuccess);
        Assert.IsFalse(missing.IsSuccess);
        Assert.HasCount(0, process.Requests);
    }

    private static UnrealProject CreateProject(string projectPath) =>
        new(
            Name: Path.GetFileNameWithoutExtension(projectPath),
            ProjectFilePath: new ProjectPath(projectPath),
            EngineAssociation: "5.8",
            EngineDisplayVersion: "5.8",
            ProjectType: ProjectType.Cpp,
            LastModified: DateTimeOffset.UnixEpoch,
            LastLaunched: null,
            IsFavorite: false,
            ProjectState: ProjectState.Available,
            EngineState: EngineResolutionState.Resolved);

    private static InstalledEngine CreateEngine(
        string editorPath,
        bool isUsable) =>
        new(
            DisplayName: "Unreal Engine 5.8",
            Association: "5.8",
            DisplayVersion: "5.8",
            RootPath: Path.GetFullPath(Path.Combine(editorPath, "..", "..", "..", "..")),
            EditorPath: editorPath,
            Source: EngineSource.Launcher,
            IsUsable: isUsable);

    private sealed class TemporaryLaunchTree : IDisposable
    {
        private TemporaryLaunchTree(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TemporaryLaunchTree Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                "UnrealEditorLauncher",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TemporaryLaunchTree(root);
        }

        public string CreateDirectory(string name)
        {
            var path = Path.GetFullPath(Path.Combine(RootPath, name));
            Directory.CreateDirectory(path);
            return path;
        }

        public string CreateEditor(string engineName)
        {
            var editorPath = Path.Combine(
                RootPath,
                engineName,
                "Engine",
                "Binaries",
                "Win64",
                "UnrealEditor.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(editorPath)!);
            File.WriteAllText(editorPath, string.Empty);
            return Path.GetFullPath(editorPath);
        }

        public string CreateProject(string directoryName, string projectName)
        {
            var projectPath = Path.Combine(
                RootPath,
                directoryName,
                $"{projectName}.uproject");
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(projectPath, "{}");
            return Path.GetFullPath(projectPath);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
