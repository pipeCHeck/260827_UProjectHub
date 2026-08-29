using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.Windows.Launching;

[TestClass]
public sealed class UnrealProjectFilesGeneratorTests
{
    [TestMethod]
    public void PrepareUsesResolvedEngineUbtWithSeparatedProjectArguments()
    {
        using var fixture = GenerationFixture.Create(installedBuild: true);
        var generator = new UnrealProjectFilesGenerator(
            new FakeExternalProcessRunner(),
            new VisualStudioSolutionLocator());

        var preparation = generator.Prepare(fixture.Project, fixture.Engine);

        Assert.IsTrue(preparation.CanGenerate);
        Assert.IsNull(preparation.UnavailableReason);
        Assert.IsNotNull(preparation.Request);
        Assert.AreEqual(fixture.UbtPath, preparation.Request.Process.FileName);
        Assert.AreEqual(
            fixture.Engine.RootPath,
            preparation.Request.Process.WorkingDirectory);
        CollectionAssert.AreEqual(
            new[]
            {
                "-ProjectFiles",
                $"-Project={fixture.Project.ProjectFilePath.Value}",
                "-Game",
                "-Progress",
                "-Rocket",
            },
            preparation.Request.Process.ArgumentList.ToArray());
        Assert.IsFalse(preparation.Request.Process.ArgumentList.Any(argument =>
            argument.StartsWith(
                "-ProjectFileFormat=",
                StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(string.Equals(
            Path.Combine(fixture.Project.ProjectDirectory, "Game.sln"),
            preparation.Request.ExpectedSolutionPath,
            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [DataRow(EngineSource.SourceBuild)]
    [DataRow(EngineSource.Manual)]
    public void PrepareOmitsRocketForNonLauncherEngineEvenWithInstalledBuildMarker(
        EngineSource engineSource)
    {
        using var fixture = GenerationFixture.Create(
            installedBuild: true,
            engineSource: engineSource);
        var generator = new UnrealProjectFilesGenerator(
            new FakeExternalProcessRunner(),
            new VisualStudioSolutionLocator());

        var preparation = generator.Prepare(fixture.Project, fixture.Engine);

        Assert.IsTrue(preparation.CanGenerate);
        Assert.AreEqual(fixture.UbtPath, preparation.Request!.Process.FileName);
        Assert.AreEqual(
            fixture.Engine.RootPath,
            preparation.Request.Process.WorkingDirectory);
        CollectionAssert.AreEqual(
            new[]
            {
                "-ProjectFiles",
                $"-Project={fixture.Project.ProjectFilePath.Value}",
                "-Game",
                "-Progress",
            },
            preparation.Request.Process.ArgumentList.ToArray());
    }

    [TestMethod]
    public void PrepareOmitsRocketForLauncherEngineWithoutInstalledBuildMarker()
    {
        using var fixture = GenerationFixture.Create(installedBuild: false);
        var generator = new UnrealProjectFilesGenerator(
            new FakeExternalProcessRunner(),
            new VisualStudioSolutionLocator());

        var preparation = generator.Prepare(fixture.Project, fixture.Engine);

        Assert.IsTrue(preparation.CanGenerate);
        CollectionAssert.DoesNotContain(
            preparation.Request!.Process.ArgumentList.ToArray(),
            "-Rocket");
    }

    [TestMethod]
    public void PrepareKeepsSpecialProjectPathInOneSeparatedArgument()
    {
        using var fixture = GenerationFixture.Create(
            installedBuild: true,
            projectDirectoryName: "Project & 특수 (Phase 2)",
            projectName: "Game #1");
        var generator = new UnrealProjectFilesGenerator(
            new FakeExternalProcessRunner(),
            new VisualStudioSolutionLocator());

        var preparation = generator.Prepare(fixture.Project, fixture.Engine);

        Assert.IsTrue(preparation.CanGenerate);
        Assert.AreEqual(
            $"-Project={fixture.Project.ProjectFilePath.Value}",
            preparation.Request!.Process.ArgumentList[1]);
        Assert.HasCount(5, preparation.Request.Process.ArgumentList);
    }

    [TestMethod]
    public void PrepareIsUnavailableWhenResolvedEngineHasNoRunnableUbt()
    {
        using var fixture = GenerationFixture.Create(installedBuild: true);
        File.Delete(fixture.UbtPath);
        var generator = new UnrealProjectFilesGenerator(
            new FakeExternalProcessRunner(),
            new VisualStudioSolutionLocator());

        var preparation = generator.Prepare(fixture.Project, fixture.Engine);

        Assert.IsFalse(preparation.CanGenerate);
        Assert.IsNull(preparation.Request);
        Assert.Contains(
            "UnrealBuildTool",
            preparation.UnavailableReason!,
            StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task GenerateSuccessRelocatesNewSolutionImmediately()
    {
        using var fixture = GenerationFixture.Create(installedBuild: true);
        var runner = new FakeExternalProcessRunner((_, _) =>
        {
            File.WriteAllText(
                Path.Combine(fixture.Project.ProjectDirectory, "Game.sln"),
                "Microsoft Visual Studio Solution File");
            return Task.FromResult(SuccessfulProcessResult());
        });
        var generator = new UnrealProjectFilesGenerator(
            runner,
            new VisualStudioSolutionLocator());
        var request = generator.Prepare(fixture.Project, fixture.Engine).Request!;

        var result = await generator.GenerateAsync(request);

        Assert.AreEqual(ProjectFileGenerationStatus.Succeeded, result.Status);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            VisualStudioSolutionState.Available,
            result.SolutionSelection!.State);
        Assert.IsTrue(string.Equals(
            request.ExpectedSolutionPath,
            result.SolutionSelection.SolutionPath,
            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task GenerateZeroExitKeepsMissingSolutionResultDistinct()
    {
        using var fixture = GenerationFixture.Create(installedBuild: true);
        var generator = new UnrealProjectFilesGenerator(
            new FakeExternalProcessRunner(
                (_, _) => Task.FromResult(SuccessfulProcessResult())),
            new VisualStudioSolutionLocator());
        var request = generator.Prepare(fixture.Project, fixture.Engine).Request!;

        var result = await generator.GenerateAsync(request);

        Assert.AreEqual(ProjectFileGenerationStatus.Succeeded, result.Status);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            VisualStudioSolutionState.Missing,
            result.SolutionSelection!.State);
    }

    [TestMethod]
    [DataRow(ExternalProcessStatus.NonZeroExit, ProjectFileGenerationStatus.NonZeroExit)]
    [DataRow(ExternalProcessStatus.FailedToStart, ProjectFileGenerationStatus.FailedToStart)]
    [DataRow(ExternalProcessStatus.Cancelled, ProjectFileGenerationStatus.Cancelled)]
    public async Task GeneratePreservesExternalProcessFailureState(
        ExternalProcessStatus processStatus,
        ProjectFileGenerationStatus expectedStatus)
    {
        using var fixture = GenerationFixture.Create(installedBuild: true);
        var processResult = new ExternalProcessResult(
            processStatus,
            ExitCode: processStatus == ExternalProcessStatus.NonZeroExit ? 7 : null,
            StandardOutputTail: "stdout tail",
            StandardErrorTail: "stderr tail",
            ErrorMessage: "generation failed");
        var runner = new FakeExternalProcessRunner(
            (_, _) => Task.FromResult(processResult));
        var generator = new UnrealProjectFilesGenerator(
            runner,
            new VisualStudioSolutionLocator());
        var request = generator.Prepare(fixture.Project, fixture.Engine).Request!;

        var result = await generator.GenerateAsync(request);

        Assert.AreEqual(expectedStatus, result.Status);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(processResult.ExitCode, result.ExitCode);
        Assert.AreEqual("stdout tail", result.StandardOutputTail);
        Assert.AreEqual("stderr tail", result.StandardErrorTail);
        Assert.AreEqual("generation failed", result.ErrorMessage);
        Assert.IsNull(result.SolutionSelection);
    }

    [TestMethod]
    public async Task GenerateFailureDoesNotRewriteExistingProjectOrSolution()
    {
        using var fixture = GenerationFixture.Create(installedBuild: true);
        const string descriptor = "{ \"EngineAssociation\": \"5.8\" }";
        const string solution = "existing solution";
        File.WriteAllText(fixture.Project.ProjectFilePath.Value, descriptor);
        File.WriteAllText(
            Path.Combine(fixture.Project.ProjectDirectory, "Game.sln"),
            solution);
        var processResult = new ExternalProcessResult(
            ExternalProcessStatus.NonZeroExit,
            ExitCode: 6,
            StandardOutputTail: string.Empty,
            StandardErrorTail: "failed",
            ErrorMessage: "The process exited with code 6.");
        var generator = new UnrealProjectFilesGenerator(
            new FakeExternalProcessRunner(
                (_, _) => Task.FromResult(processResult)),
            new VisualStudioSolutionLocator());
        var request = generator.Prepare(fixture.Project, fixture.Engine).Request!;

        var result = await generator.GenerateAsync(request);

        Assert.AreEqual(ProjectFileGenerationStatus.NonZeroExit, result.Status);
        Assert.AreEqual(
            descriptor,
            File.ReadAllText(fixture.Project.ProjectFilePath.Value));
        Assert.AreEqual(
            solution,
            File.ReadAllText(Path.Combine(
                fixture.Project.ProjectDirectory,
                "Game.sln")));
    }

    [TestMethod]
    public async Task GenerateRejectsConcurrentRunForSameProject()
    {
        using var fixture = GenerationFixture.Create(installedBuild: true);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ExternalProcessResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeExternalProcessRunner((_, _) =>
        {
            started.TrySetResult();
            return release.Task;
        });
        var generator = new UnrealProjectFilesGenerator(
            runner,
            new VisualStudioSolutionLocator());
        var request = generator.Prepare(fixture.Project, fixture.Engine).Request!;

        var firstRun = generator.GenerateAsync(request);
        await started.Task;
        var duplicate = await generator.GenerateAsync(request);
        release.SetResult(SuccessfulProcessResult());
        _ = await firstRun;

        Assert.AreEqual(
            ProjectFileGenerationStatus.AlreadyRunning,
            duplicate.Status);
        Assert.AreEqual(1, runner.RunCount);
    }

    [TestMethod]
    public async Task GenerateAllowsConcurrentRunsForDifferentProjects()
    {
        using var firstFixture = GenerationFixture.Create(installedBuild: true);
        using var secondFixture = GenerationFixture.Create(installedBuild: true);
        var release = new TaskCompletionSource<ExternalProcessResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeExternalProcessRunner((_, _) => release.Task);
        var generator = new UnrealProjectFilesGenerator(
            runner,
            new VisualStudioSolutionLocator());
        var firstRequest = generator.Prepare(
            firstFixture.Project,
            firstFixture.Engine).Request!;
        var secondRequest = generator.Prepare(
            secondFixture.Project,
            secondFixture.Engine).Request!;

        var firstRun = generator.GenerateAsync(firstRequest);
        var secondRun = generator.GenerateAsync(secondRequest);

        Assert.AreEqual(2, runner.RunCount);
        release.SetResult(SuccessfulProcessResult());
        _ = await Task.WhenAll(firstRun, secondRun);
    }

    private static ExternalProcessResult SuccessfulProcessResult() =>
        new(
            ExternalProcessStatus.Succeeded,
            ExitCode: 0,
            StandardOutputTail: "generated",
            StandardErrorTail: string.Empty,
            ErrorMessage: null);

    private sealed class FakeExternalProcessRunner : IExternalProcessRunner
    {
        private readonly Func<
            ExternalProcessRequest,
            CancellationToken,
            Task<ExternalProcessResult>> _run;

        public FakeExternalProcessRunner(
            Func<
                ExternalProcessRequest,
                CancellationToken,
                Task<ExternalProcessResult>>? run = null)
        {
            _run = run ?? ((_, _) => throw new InvalidOperationException(
                "Process execution was not expected."));
        }

        public int RunCount { get; private set; }

        public Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            return _run(request, cancellationToken);
        }
    }

    private sealed class GenerationFixture : IDisposable
    {
        private GenerationFixture(
            string rootPath,
            UnrealProject project,
            InstalledEngine engine,
            string ubtPath)
        {
            RootPath = rootPath;
            Project = project;
            Engine = engine;
            UbtPath = ubtPath;
        }

        public string RootPath { get; }

        public UnrealProject Project { get; }

        public InstalledEngine Engine { get; }

        public string UbtPath { get; }

        public static GenerationFixture Create(
            bool installedBuild,
            EngineSource engineSource = EngineSource.Launcher,
            string projectDirectoryName = "Project",
            string projectName = "Game")
        {
            var testRoot = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                nameof(UnrealProjectFilesGeneratorTests),
                Guid.NewGuid().ToString("N"));
            var engineRoot = Path.Combine(testRoot, "EngineRoot");
            var ubtPath = Path.Combine(
                engineRoot,
                "Engine",
                "Binaries",
                "DotNET",
                "UnrealBuildTool",
                "UnrealBuildTool.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(ubtPath)!);
            File.WriteAllText(ubtPath, string.Empty);
            if (installedBuild)
            {
                var markerPath = Path.Combine(
                    engineRoot,
                    "Engine",
                    "Build",
                    "InstalledBuild.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
                File.WriteAllText(markerPath, "Installed");
            }

            var projectDirectory = Path.Combine(testRoot, projectDirectoryName);
            Directory.CreateDirectory(projectDirectory);
            var projectPath = Path.Combine(
                projectDirectory,
                $"{projectName}.uproject");
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
            var engine = new InstalledEngine(
                "Unreal Engine 5.8",
                "5.8",
                "5.8",
                engineRoot,
                Path.Combine(engineRoot, "Engine", "Binaries", "Win64", "UnrealEditor.exe"),
                engineSource,
                IsUsable: true);
            return new GenerationFixture(testRoot, project, engine, ubtPath);
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
