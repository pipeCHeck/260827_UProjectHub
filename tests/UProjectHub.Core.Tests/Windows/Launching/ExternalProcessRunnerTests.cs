using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.Windows.Launching;

[TestClass]
public sealed class ExternalProcessRunnerTests
{
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
}
