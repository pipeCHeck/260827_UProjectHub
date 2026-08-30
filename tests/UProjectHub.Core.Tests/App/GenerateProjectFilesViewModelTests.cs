using UProjectHub.App.ViewModels;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class GenerateProjectFilesViewModelTests
{
    [TestMethod]
    public async Task StreamingOutputAppearsBeforeGenerationCompletesAsync()
    {
        var outputReported = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ProjectFileGenerationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = CreateFixture(async (progress, _) =>
        {
            progress!.Report(new ExternalProcessOutput(
                ExternalProcessOutputStream.StandardOutput,
                "discovering modules\n"));
            outputReported.SetResult();
            return await release.Task;
        });

        var generation = fixture.ViewModel.GenerateCommand.ExecuteAsync();
        await outputReported.Task;
        await WaitUntilAsync(() => fixture.ViewModel.OutputDetails.Contains(
            "discovering modules",
            StringComparison.Ordinal));

        Assert.IsFalse(generation.IsCompleted);
        Assert.IsTrue(fixture.ViewModel.IsRunning);

        release.SetResult(SuccessResult());
        await generation;
    }

    [TestMethod]
    public async Task StreamingPreservesReportedOrderAndStreamIdentityAsync()
    {
        var reported = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ProjectFileGenerationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = CreateFixture(async (progress, _) =>
        {
            progress!.Report(new ExternalProcessOutput(
                ExternalProcessOutputStream.StandardOutput,
                "first\n"));
            progress.Report(new ExternalProcessOutput(
                ExternalProcessOutputStream.StandardError,
                "second\n"));
            progress.Report(new ExternalProcessOutput(
                ExternalProcessOutputStream.StandardOutput,
                "third\n"));
            reported.SetResult();
            return await release.Task;
        });

        var generation = fixture.ViewModel.GenerateCommand.ExecuteAsync();
        await reported.Task;
        await WaitUntilAsync(() => fixture.ViewModel.OutputDetails.Contains(
            "third",
            StringComparison.Ordinal));

        var first = fixture.ViewModel.OutputDetails.IndexOf(
            "[stdout] first",
            StringComparison.Ordinal);
        var second = fixture.ViewModel.OutputDetails.IndexOf(
            "[stderr] second",
            StringComparison.Ordinal);
        var third = fixture.ViewModel.OutputDetails.IndexOf(
            "[stdout] third",
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, first);
        Assert.IsGreaterThan(first, second);
        Assert.IsGreaterThan(second, third);

        release.SetResult(SuccessResult());
        await generation;
    }

    [TestMethod]
    public async Task HighVolumeStreamingUsesBoundedBufferAndBatchedUpdatesAsync()
    {
        var reported = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ProjectFileGenerationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = CreateFixture(async (progress, _) =>
        {
            for (var index = 0; index < 20_000; index++)
            {
                progress!.Report(new ExternalProcessOutput(
                    ExternalProcessOutputStream.StandardOutput,
                    $"line-{index:D5}-0123456789ABCDEF\n"));
            }

            reported.SetResult();
            return await release.Task;
        });
        var outputUpdates = 0;
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(
                GenerateProjectFilesViewModel.OutputDetails))
            {
                Interlocked.Increment(ref outputUpdates);
            }
        };

        var generation = fixture.ViewModel.GenerateCommand.ExecuteAsync();
        await reported.Task;
        await WaitUntilAsync(() => fixture.ViewModel.HasOutputDetails);
        await Task.Delay(250);

        Assert.IsLessThanOrEqualTo(40 * 1024, fixture.ViewModel.OutputDetails.Length);
        Assert.IsLessThanOrEqualTo(4, outputUpdates);

        release.SetResult(SuccessResult());
        await generation;
    }

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

    [TestMethod]
    public async Task CancelThenRetryIgnoresLateOutputFromPreviousRunAsync()
    {
        IProgress<ExternalProcessOutput>? firstProgress = null;
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource<ProjectFileGenerationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempt = 0;
        var fixture = CreateFixture(async (progress, cancellationToken) =>
        {
            attempt++;
            if (attempt == 1)
            {
                firstProgress = progress;
                progress!.Report(new ExternalProcessOutput(
                    ExternalProcessOutputStream.StandardOutput,
                    "old-live\n"));
                firstStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                }

                return CancelledResult("old-final");
            }

            progress!.Report(new ExternalProcessOutput(
                ExternalProcessOutputStream.StandardOutput,
                "new-live\n"));
            secondStarted.SetResult();
            return await releaseSecond.Task;
        });

        var firstRun = fixture.ViewModel.GenerateCommand.ExecuteAsync();
        await firstStarted.Task;
        fixture.ViewModel.CancelCommand.Execute(null);
        await firstRun;

        var secondRun = fixture.ViewModel.GenerateCommand.ExecuteAsync();
        await secondStarted.Task;
        firstProgress!.Report(new ExternalProcessOutput(
            ExternalProcessOutputStream.StandardError,
            "old-late\n"));
        await WaitUntilAsync(() => fixture.ViewModel.OutputDetails.Contains(
            "new-live",
            StringComparison.Ordinal));

        Assert.DoesNotContain("old-late", fixture.ViewModel.OutputDetails);

        releaseSecond.SetResult(SuccessResult());
        await secondRun;
    }

    [TestMethod]
    public async Task DisposeIgnoresLateStreamingOutputAsync()
    {
        IProgress<ExternalProcessOutput>? capturedProgress = null;
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = CreateFixture(async (progress, cancellationToken) =>
        {
            capturedProgress = progress;
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }

            return CancelledResult("cancelled-final");
        });

        var generation = fixture.ViewModel.GenerateCommand.ExecuteAsync();
        await started.Task;
        fixture.ViewModel.Dispose();
        await generation;
        var outputAfterDispose = fixture.ViewModel.OutputDetails;

        capturedProgress!.Report(new ExternalProcessOutput(
            ExternalProcessOutputStream.StandardOutput,
            "late-after-close\n"));
        await Task.Delay(200);

        Assert.AreEqual(outputAfterDispose, fixture.ViewModel.OutputDetails);
    }

    private static Fixture CreateFixture(
        ProjectFileGenerationResult? result = null) =>
        CreateFixture((_, _) => Task.FromResult(result ?? SuccessResult()));

    private static Fixture CreateFixture(
        Func<CancellationToken, Task<ProjectFileGenerationResult>> generate)
        => CreateFixture((_, cancellationToken) => generate(cancellationToken));

    private static Fixture CreateFixture(
        Func<
            IProgress<ExternalProcessOutput>?,
            CancellationToken,
            Task<ProjectFileGenerationResult>> generate)
    {
        var generateCount = 0;
        var refreshCount = 0;
        var request = CreateRequest();
        var viewModel = new GenerateProjectFilesViewModel(
            request,
            async (progress, cancellationToken) =>
            {
                generateCount++;
                return await generate(progress, cancellationToken);
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

    private static ProjectFileGenerationResult CancelledResult(string output) =>
        new(
            ProjectFileGenerationStatus.Cancelled,
            ExitCode: null,
            StandardOutputTail: output,
            StandardErrorTail: string.Empty,
            ErrorMessage: "The operation was cancelled.",
            SolutionSelection: null);

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!predicate())
        {
            if (DateTime.UtcNow >= timeout)
            {
                Assert.Fail("The expected asynchronous state was not observed.");
            }

            await Task.Delay(20);
        }
    }

    private sealed record Fixture(
        GenerateProjectFilesViewModel ViewModel,
        Func<int> GetGenerateCount,
        Func<int> GetRefreshCount)
    {
        public int GenerateCount => GetGenerateCount();

        public int RefreshCount => GetRefreshCount();
    }
}
