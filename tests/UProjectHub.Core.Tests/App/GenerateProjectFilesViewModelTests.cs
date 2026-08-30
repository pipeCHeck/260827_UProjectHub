using UProjectHub.App.ViewModels;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class GenerateProjectFilesViewModelTests
{
    [TestMethod]
    public void ConfirmationShowsExactTargetsAndDoesNotStartAutomatically()
    {
        var fixture = CreateFixture();

        Assert.AreEqual("Game", fixture.ViewModel.ProjectName);
        Assert.AreEqual(@"D:\Projects\Game\Game.uproject", fixture.ViewModel.ProjectPath);
        Assert.AreEqual("Unreal Engine 5.8", fixture.ViewModel.EngineDisplayName);
        Assert.AreEqual(@"C:\UE\5.8", fixture.ViewModel.EngineRoot);
        Assert.AreEqual(@"D:\Projects\Game\Game.sln", fixture.ViewModel.ExpectedSolutionPath);
        Assert.IsFalse(fixture.ViewModel.IsRunning);
        Assert.IsFalse(fixture.ViewModel.IsCompleted);
        Assert.AreEqual(0, fixture.GenerateCount);
        Assert.IsTrue(fixture.ViewModel.GenerateCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task ExplicitGenerateReportsSuccessAndRefreshesSolutionStateAsync()
    {
        var fixture = CreateFixture(result: SuccessResult());

        await fixture.ViewModel.GenerateCommand.ExecuteAsync();

        Assert.AreEqual(1, fixture.GenerateCount);
        Assert.IsTrue(fixture.ViewModel.IsCompleted);
        Assert.IsFalse(fixture.ViewModel.IsRunning);
        Assert.IsTrue(fixture.ViewModel.WasSuccessful);
        Assert.AreEqual(1, fixture.RefreshCount);
        StringAssert.Contains(fixture.ViewModel.StatusText, "generated");
        StringAssert.Contains(fixture.ViewModel.OutputDetails, "Generating code");
        Assert.IsFalse(fixture.ViewModel.GenerateCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task FailureKeepsBoundedDiagnosticDetailsWithoutRefreshingSolutionAsync()
    {
        var fixture = CreateFixture(result: new ProjectFileGenerationResult(
            ProjectFileGenerationStatus.NonZeroExit,
            ExitCode: 6,
            StandardOutputTail: "output tail",
            StandardErrorTail: "error tail",
            ErrorMessage: "The process exited with code 6.",
            SolutionSelection: null));

        await fixture.ViewModel.GenerateCommand.ExecuteAsync();

        Assert.IsFalse(fixture.ViewModel.WasSuccessful);
        Assert.AreEqual(0, fixture.RefreshCount);
        StringAssert.Contains(fixture.ViewModel.StatusText, "failed");
        StringAssert.Contains(fixture.ViewModel.OutputDetails, "code 6");
        StringAssert.Contains(fixture.ViewModel.OutputDetails, "error tail");
    }

    [TestMethod]
    public async Task CancelRequestsCancellationAndReportsCancelledAsync()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var fixture = CreateFixture(async cancellationToken =>
        {
            attempts++;
            if (attempts > 1)
            {
                return SuccessResult();
            }

            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }

            return new ProjectFileGenerationResult(
                ProjectFileGenerationStatus.Cancelled,
                ExitCode: null,
                StandardOutputTail: string.Empty,
                StandardErrorTail: string.Empty,
                ErrorMessage: "The operation was cancelled.",
                SolutionSelection: null);
        });

        var generation = fixture.ViewModel.GenerateCommand.ExecuteAsync();
        await started.Task;
        Assert.IsTrue(fixture.ViewModel.IsRunning);
        Assert.IsTrue(fixture.ViewModel.CancelCommand.CanExecute(null));

        fixture.ViewModel.CancelCommand.Execute(null);
        await generation;

        Assert.IsTrue(fixture.ViewModel.IsCompleted);
        Assert.IsFalse(fixture.ViewModel.WasSuccessful);
        StringAssert.Contains(fixture.ViewModel.StatusText, "canceled");
        Assert.IsTrue(fixture.ViewModel.GenerateCommand.CanExecute(null));

        await fixture.ViewModel.GenerateCommand.ExecuteAsync();

        Assert.AreEqual(2, fixture.GenerateCount);
        Assert.IsTrue(fixture.ViewModel.WasSuccessful);
        Assert.AreEqual(1, fixture.RefreshCount);
    }

    private static Fixture CreateFixture(
        ProjectFileGenerationResult? result = null) =>
        CreateFixture(_ => Task.FromResult(result ?? SuccessResult()));

    private static Fixture CreateFixture(
        Func<CancellationToken, Task<ProjectFileGenerationResult>> generate)
    {
        var generateCount = 0;
        var refreshCount = 0;
        var request = CreateRequest();
        var viewModel = new GenerateProjectFilesViewModel(
            request,
            async cancellationToken =>
            {
                generateCount++;
                return await generate(cancellationToken);
            },
            () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            });
        return new Fixture(
            viewModel,
            () => generateCount,
            () => refreshCount);
    }

    private static ProjectFileGenerationRequest CreateRequest()
    {
        var project = new UnrealProject(
            "Game",
            new ProjectPath(@"D:\Projects\Game\Game.uproject"),
            "5.8",
            "5.8.1",
            ProjectType.Cpp,
            DateTimeOffset.UtcNow,
            LastLaunched: null,
            IsFavorite: false,
            ProjectState.Available,
            EngineResolutionState.Resolved);
        var engine = new InstalledEngine(
            "Unreal Engine 5.8",
            "5.8",
            "5.8.1",
            @"C:\UE\5.8",
            @"C:\UE\5.8\Engine\Binaries\Win64\UnrealEditor.exe",
            EngineSource.Launcher,
            IsUsable: true);
        return new ProjectFileGenerationRequest(
            project,
            engine,
            new ExternalProcessRequest(
                @"C:\UE\5.8\Engine\Binaries\DotNET\UnrealBuildTool\UnrealBuildTool.exe",
                ["-ProjectFiles"]),
            @"D:\Projects\Game\Game.sln");
    }

    private static ProjectFileGenerationResult SuccessResult() =>
        new(
            ProjectFileGenerationStatus.Succeeded,
            ExitCode: 0,
            StandardOutputTail: "Generating code",
            StandardErrorTail: string.Empty,
            ErrorMessage: null,
            VisualStudioSolutionSelection.Available(
                @"D:\Projects\Game\Game.sln",
                [@"D:\Projects\Game\Game.sln"]));

    private sealed record Fixture(
        GenerateProjectFilesViewModel ViewModel,
        Func<int> GetGenerateCount,
        Func<int> GetRefreshCount)
    {
        public int GenerateCount => GetGenerateCount();

        public int RefreshCount => GetRefreshCount();
    }
}
