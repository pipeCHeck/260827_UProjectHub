using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.Windows.Launching;

[TestClass]
public sealed class ExternalProcessRunnerTests
{
    [TestMethod]
    public async Task RunStreamsOutputBeforeProcessExits()
    {
        var runner = new ExternalProcessRunner();
        var firstOutput = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new InlineProgress<ExternalProcessOutput>(output =>
        {
            if (output.Text.Contains("early-output", StringComparison.Ordinal))
            {
                firstOutput.TrySetResult();
            }
        });
        var request = new ExternalProcessRequest(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            [
                "/d",
                "/s",
                "/c",
                "echo early-output & ping 127.0.0.1 -n 3 >nul & echo late-output",
            ]);

        var run = runner.RunAsync(request, CancellationToken.None, progress);
        await firstOutput.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsFalse(run.IsCompleted);
        var result = await run;
        Assert.Contains("late-output", result.StandardOutputTail);
    }

    [TestMethod]
    public async Task RunStreamsStandardOutputAndStandardError()
    {
        var runner = new ExternalProcessRunner();
        var streamed = new List<ExternalProcessOutput>();
        var progress = new InlineProgress<ExternalProcessOutput>(output =>
        {
            lock (streamed)
            {
                streamed.Add(output);
            }
        });
        var request = new ExternalProcessRequest(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            [
                "/d",
                "/s",
                "/c",
                "echo standard-output & echo standard-error 1>&2",
            ]);

        var result = await runner.RunAsync(
            request,
            CancellationToken.None,
            progress);

        Assert.AreEqual(ExternalProcessStatus.Succeeded, result.Status);
        Assert.IsTrue(streamed.Any(output =>
            output.Stream == ExternalProcessOutputStream.StandardOutput
            && output.Text.Contains("standard-output", StringComparison.Ordinal)));
        Assert.IsTrue(streamed.Any(output =>
            output.Stream == ExternalProcessOutputStream.StandardError
            && output.Text.Contains("standard-error", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task RunCapturesOutputAndNonZeroExitCode()
    {
        var runner = new ExternalProcessRunner();
        var request = new ExternalProcessRequest(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            [
                "/d",
                "/s",
                "/c",
                "echo standard-output & echo standard-error 1>&2 & exit /b 7",
            ]);

        var result = await runner.RunAsync(request);

        Assert.AreEqual(ExternalProcessStatus.NonZeroExit, result.Status);
        Assert.AreEqual(7, result.ExitCode);
        Assert.Contains("standard-output", result.StandardOutputTail);
        Assert.Contains("standard-error", result.StandardErrorTail);
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task RunRetainsOnlyConfiguredOutputTail()
    {
        const int outputLimit = 64;
        var runner = new ExternalProcessRunner(outputLimit);
        var request = new ExternalProcessRequest(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            [
                "/d",
                "/s",
                "/c",
                "for /L %i in (1,1,100) do @echo 0123456789",
            ]);

        var result = await runner.RunAsync(request);

        Assert.AreEqual(ExternalProcessStatus.Succeeded, result.Status);
        Assert.IsLessThanOrEqualTo(
            outputLimit,
            result.StandardOutputTail.Length);
        Assert.EndsWith("0123456789", result.StandardOutputTail.TrimEnd());
    }

    [TestMethod]
    public async Task RunRetainsOnlyConfiguredErrorTail()
    {
        const int outputLimit = 64;
        var runner = new ExternalProcessRunner(outputLimit);
        var request = new ExternalProcessRequest(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            [
                "/d",
                "/s",
                "/c",
                "for /L %i in (1,1,100) do @echo 9876543210 1>&2",
            ]);

        var result = await runner.RunAsync(request);

        Assert.AreEqual(ExternalProcessStatus.Succeeded, result.Status);
        Assert.IsLessThanOrEqualTo(
            outputLimit,
            result.StandardErrorTail.Length);
        Assert.EndsWith("9876543210", result.StandardErrorTail.TrimEnd());
    }

    [TestMethod]
    public async Task RunManyStreamedChunksKeepsFinalOutputBounded()
    {
        const int outputLimit = 256;
        var runner = new ExternalProcessRunner(outputLimit);
        var streamedCharacterCount = 0L;
        var progress = new InlineProgress<ExternalProcessOutput>(output =>
            Interlocked.Add(ref streamedCharacterCount, output.Text.Length));
        var request = new ExternalProcessRequest(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            [
                "/d",
                "/s",
                "/c",
                "for /L %i in (1,1,20000) do @echo 0123456789ABCDEF",
            ]);

        var result = await runner.RunAsync(
            request,
            CancellationToken.None,
            progress);

        Assert.AreEqual(ExternalProcessStatus.Succeeded, result.Status);
        Assert.IsGreaterThan(outputLimit, streamedCharacterCount);
        Assert.IsLessThanOrEqualTo(outputLimit, result.StandardOutputTail.Length);
    }

    [TestMethod]
    public async Task RunReturnsFailedToStartInsteadOfThrowing()
    {
        var runner = new ExternalProcessRunner();
        var request = new ExternalProcessRequest(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"));

        var result = await runner.RunAsync(request);

        Assert.AreEqual(ExternalProcessStatus.FailedToStart, result.Status);
        Assert.IsNull(result.ExitCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [TestMethod]
    public async Task RunCancelsAndTerminatesLongRunningProcess()
    {
        var runner = new ExternalProcessRunner();
        var request = new ExternalProcessRequest(
            Path.Combine(Environment.SystemDirectory, "PING.EXE"),
            ["127.0.0.1", "-n", "30", "-w", "1000"]);
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(100));

        var result = await runner.RunAsync(request, cancellation.Token);

        Assert.AreEqual(ExternalProcessStatus.Cancelled, result.Status);
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task RunCancellationRemainsResponsiveDuringHeavyStreaming()
    {
        var runner = new ExternalProcessRunner();
        var firstOutput = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new InlineProgress<ExternalProcessOutput>(_ =>
            firstOutput.TrySetResult());
        var request = new ExternalProcessRequest(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            [
                "/d",
                "/s",
                "/c",
                "for /L %i in (1,1,1000000) do @echo high-volume-output-%i",
            ]);
        using var cancellation = new CancellationTokenSource();

        var run = runner.RunAsync(request, cancellation.Token, progress);
        await firstOutput.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(8));

        Assert.AreEqual(ExternalProcessStatus.Cancelled, result.Status);
    }

    [TestMethod]
    public async Task CancellationCleanupWaitIsBoundedWhenCleanupDoesNotFinish()
    {
        var neverCompletes = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var completed = await ExternalProcessRunner
            .WaitForCancellationCleanupAsync(
                neverCompletes.Task,
                TimeSpan.FromMilliseconds(50))
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsFalse(completed);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
