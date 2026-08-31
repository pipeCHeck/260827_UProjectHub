using UProjectHub.Windows.Launching;
using UProjectHub.Windows.SourceControl;

namespace UProjectHub.Core.Tests.Windows.SourceControl;

[TestClass]
public sealed class GitStatusServiceTests
{
    [TestMethod]
    public async Task CleanRepositoryUsesProjectDirectoryAndReportsRootAsync()
    {
        var runner = new QueueProcessRunner(
            Succeeded("git version 2.50.0\n"),
            Succeeded("D:/Work/Repository\n"),
            Succeeded(string.Empty));
        var service = new GitStatusService(runner);

        var result = await service.GetStatusAsync(
            @"D:\Work\Repository\Samples\Game");

        Assert.AreEqual(GitProjectState.Clean, result.State);
        Assert.AreEqual("D:/Work/Repository", result.RepositoryRoot);
        CollectionAssert.AreEqual(
            new[]
            {
                "--no-optional-locks", "-C",
                @"D:\Work\Repository\Samples\Game",
                "rev-parse", "--show-toplevel",
            },
            runner.Requests[1].ArgumentList.ToArray());
    }

    [TestMethod]
    [DataRow(" M Source/Game.cpp\0", DisplayName = "unstaged")]
    [DataRow("M  Source/Game.cpp\0", DisplayName = "staged")]
    [DataRow("?? Notes.txt\0", DisplayName = "untracked")]
    public async Task AnyPorcelainEntryReportsChangedAsync(string output)
    {
        var runner = new QueueProcessRunner(
            Succeeded("git version 2.50.0\n"),
            Succeeded("D:/Work/Game\n"),
            Succeeded(output));
        var service = new GitStatusService(runner);

        var result = await service.GetStatusAsync(@"D:\Work\Game");

        Assert.AreEqual(GitProjectState.Changed, result.State);
    }

    [TestMethod]
    public async Task GitUnavailableIsProbedOnlyOnceAcrossProjectsAsync()
    {
        var runner = new QueueProcessRunner(FailedToStart("git was not found"));
        var service = new GitStatusService(runner);

        var first = await service.GetStatusAsync(@"D:\Work\One");
        var second = await service.GetStatusAsync(@"D:\Work\Two");

        Assert.AreEqual(GitProjectState.GitUnavailable, first.State);
        Assert.AreEqual(GitProjectState.GitUnavailable, second.State);
        Assert.HasCount(1, runner.Requests);
    }

    [TestMethod]
    public async Task LaterFailedToStartMarksGitUnavailableForRemainingProjectsAsync()
    {
        var runner = new QueueProcessRunner(
            Succeeded("git version 2.50.0\n"),
            FailedToStart("git disappeared"));
        var service = new GitStatusService(runner);

        var first = await service.GetStatusAsync(@"D:\Work\One");
        var second = await service.GetStatusAsync(@"D:\Work\Two");

        Assert.AreEqual(GitProjectState.GitUnavailable, first.State);
        Assert.AreEqual(GitProjectState.GitUnavailable, second.State);
        Assert.HasCount(2, runner.Requests);
    }

    [TestMethod]
    public async Task NonRepositoryIsLocaleIndependentAndDistinctFromRepositoryFailureAsync()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"UProjectHub-GitStatus-{Guid.NewGuid():N}");
        var plainProject = Path.Combine(temporaryRoot, "PlainProject");
        var brokenRepository = Path.Combine(temporaryRoot, "BrokenRepository");
        Directory.CreateDirectory(plainProject);
        Directory.CreateDirectory(Path.Combine(brokenRepository, ".git"));
        var notRepositoryRunner = new QueueProcessRunner(
            Succeeded("git version 2.50.0\n"),
            Failed(128, "fatal: kein Git-Repository"));
        var failedRunner = new QueueProcessRunner(
            Succeeded("git version 2.50.0\n"),
            Failed(128, "fatal: unsafe repository ownership"));

        try
        {
            var notRepository = await new GitStatusService(notRepositoryRunner)
                .GetStatusAsync(plainProject);
            var failed = await new GitStatusService(failedRunner)
                .GetStatusAsync(brokenRepository);

            Assert.AreEqual(GitProjectState.NotRepository, notRepository.State);
            Assert.AreEqual(GitProjectState.Failed, failed.State);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task DetailsIncludeDistinctRemotesAndOnlySafeWebUrlsAsync()
    {
        var runner = new QueueProcessRunner(
            Succeeded("git version 2.50.0\n"),
            Succeeded("D:/Work/Game\n"),
            Succeeded(string.Empty),
            Succeeded(
                "origin\thttps://example.com/team/game.git (fetch)\n"
                + "origin\thttps://example.com/team/game.git (push)\n"
                + "mirror\tssh://git@example.net/team/game.git (fetch)\n"));
        var service = new GitStatusService(runner);

        var result = await service.GetStatusAsync(
            @"D:\Work\Game",
            includeRemotes: true);

        Assert.HasCount(2, result.Remotes);
        Assert.AreEqual("origin", result.Remotes[0].Name);
        Assert.AreEqual(
            "https://example.com/team/game.git",
            result.Remotes[0].WebUrl);
        Assert.IsNull(result.Remotes[1].WebUrl);
    }

    [TestMethod]
    public async Task CredentialedHttpRemoteIsRedactedBeforeLeavingServiceAsync()
    {
        var runner = new QueueProcessRunner(
            Succeeded("git version 2.50.0\n"),
            Succeeded("D:/Work/Game\n"),
            Succeeded(string.Empty),
            Succeeded(
                "origin\thttps://build-user:secret-token@example.com/team/game.git (fetch)\n"));
        var service = new GitStatusService(runner);

        var result = await service.GetStatusAsync(
            @"D:\Work\Game",
            includeRemotes: true);

        Assert.HasCount(1, result.Remotes);
        var remote = result.Remotes[0];
        Assert.AreEqual(
            "https://example.com/team/game.git",
            remote.Url);
        Assert.AreEqual(remote.Url, remote.WebUrl);
        Assert.IsFalse(remote.Url.Contains("build-user", StringComparison.Ordinal));
        Assert.IsFalse(remote.Url.Contains("secret-token", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CredentialedAbsoluteNonWebRemoteIsRedactedBeforeLeavingServiceAsync()
    {
        var runner = new QueueProcessRunner(
            Succeeded("git version 2.50.0\n"),
            Succeeded("D:/Work/Game\n"),
            Succeeded(string.Empty),
            Succeeded(
                "origin\tssh://build-user:secret-token@example.com/team/game.git (fetch)\n"));
        var service = new GitStatusService(runner);

        var result = await service.GetStatusAsync(
            @"D:\Work\Game",
            includeRemotes: true);

        Assert.HasCount(1, result.Remotes);
        var remote = result.Remotes[0];
        Assert.AreEqual(
            "ssh://example.com/team/game.git",
            remote.Url);
        Assert.IsNull(remote.WebUrl);
        Assert.IsFalse(remote.Url.Contains("build-user", StringComparison.Ordinal));
        Assert.IsFalse(remote.Url.Contains("secret-token", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CallerCancellationCancelsGitQueryAsync()
    {
        var runner = new BlockingProcessRunner();
        var service = new GitStatusService(runner);
        using var cancellation = new CancellationTokenSource();

        var query = service.GetStatusAsync(
            @"D:\Work\Game",
            cancellationToken: cancellation.Token);
        await runner.Started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => query);
    }

    [TestMethod]
    public async Task CommandTimeoutReturnsFailedWithoutBlockingIndefinitelyAsync()
    {
        var runner = new ProbeThenTimeoutRunner();
        var service = new GitStatusService(
            runner,
            commandTimeout: TimeSpan.FromMilliseconds(20));

        var result = await service.GetStatusAsync(@"D:\Work\Game")
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(GitProjectState.Failed, result.State);
        StringAssert.Contains(result.ErrorMessage, "timed out");
    }

    private static ExternalProcessResult Succeeded(string output) => new(
        ExternalProcessStatus.Succeeded,
        0,
        output,
        string.Empty,
        null);

    private static ExternalProcessResult Failed(int exitCode, string error) => new(
        ExternalProcessStatus.NonZeroExit,
        exitCode,
        string.Empty,
        error,
        $"The process exited with code {exitCode}.");

    private static ExternalProcessResult FailedToStart(string error) => new(
        ExternalProcessStatus.FailedToStart,
        null,
        string.Empty,
        string.Empty,
        error);

    private sealed class QueueProcessRunner(params ExternalProcessResult[] results)
        : IExternalProcessRunner
    {
        private readonly Queue<ExternalProcessResult> _results = new(results);

        public List<ExternalProcessRequest> Requests { get; } = [];

        public Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class BlockingProcessRunner : IExternalProcessRunner
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The canceled delay returned.");
        }
    }

    private sealed class ProbeThenTimeoutRunner : IExternalProcessRunner
    {
        private int _callCount;

        public async Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                return Succeeded("git version 2.50.0\n");
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return new ExternalProcessResult(
                    ExternalProcessStatus.Cancelled,
                    null,
                    string.Empty,
                    string.Empty,
                    "The operation was cancelled.");
            }

            throw new InvalidOperationException("The timeout did not cancel Git.");
        }
    }
}
