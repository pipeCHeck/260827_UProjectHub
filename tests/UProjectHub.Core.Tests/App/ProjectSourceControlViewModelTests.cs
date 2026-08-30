using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Windows.Launching;
using UProjectHub.Windows.SourceControl;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ProjectSourceControlViewModelTests
{
    [TestMethod]
    public async Task ActivateImmediatelyRefreshesStatusAndRemotesAsync()
    {
        var project = CreateProject();
        var git = new ImmediateGitStatusService(new GitProjectStatus(
            GitProjectState.Changed,
            @"D:\Repository",
            [new GitRemote(
                "origin",
                "https://example.com/team/game.git",
                "https://example.com/team/game.git")]));
        await using var store = new ProjectGitStatusStore(
            git,
            new ImmediateDispatcher());
        _ = store.UpdateCatalog([project]);
        using var viewModel = new ProjectSourceControlViewModel(
            project,
            store,
            new FakeWebUrlLauncher());

        await viewModel.ActivateAsync();

        Assert.IsTrue(git.IncludedRemotes);
        Assert.AreEqual(GitProjectState.Changed, viewModel.State);
        Assert.AreEqual("Changed", viewModel.StateDisplay);
        Assert.AreEqual(@"D:\Repository", viewModel.RepositoryRoot);
        Assert.HasCount(1, viewModel.Remotes);
    }

    [TestMethod]
    public async Task DisposeCancelsRefreshAndIgnoresLateResultAsync()
    {
        var project = CreateProject();
        var git = new BlockingGitStatusService();
        await using var store = new ProjectGitStatusStore(
            git,
            new ImmediateDispatcher());
        _ = store.UpdateCatalog([project]);
        await git.WaitForStartedAsync();
        var viewModel = new ProjectSourceControlViewModel(
            project,
            store,
            new FakeWebUrlLauncher());

        var refresh = viewModel.ActivateAsync();
        await git.WaitForCallCountAsync(2);
        viewModel.Dispose();
        git.CompleteAll(new GitProjectStatus(GitProjectState.Clean));
        await refresh.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsNull(viewModel.Status);
    }

    [TestMethod]
    public async Task RemoteOpenCommandUsesOnlyPrevalidatedWebUrlAsync()
    {
        var project = CreateProject();
        var git = new ImmediateGitStatusService(new GitProjectStatus(
            GitProjectState.Clean,
            @"D:\Repository",
            [
                new GitRemote(
                    "origin",
                    "https://example.com/team/game.git",
                    "https://example.com/team/game.git"),
                new GitRemote(
                    "ssh",
                    "git@example.com:team/game.git",
                    null),
            ]));
        await using var store = new ProjectGitStatusStore(
            git,
            new ImmediateDispatcher());
        _ = store.UpdateCatalog([project]);
        var launcher = new FakeWebUrlLauncher();
        using var viewModel = new ProjectSourceControlViewModel(
            project,
            store,
            launcher);
        await viewModel.ActivateAsync();

        Assert.IsTrue(viewModel.Remotes[0].OpenCommand.CanExecute(null));
        Assert.IsFalse(viewModel.Remotes[1].OpenCommand.CanExecute(null));
        viewModel.Remotes[0].OpenCommand.Execute(null);
        viewModel.Remotes[1].OpenCommand.Execute(null);

        CollectionAssert.AreEqual(
            new[] { "https://example.com/team/game.git" },
            launcher.Urls.ToArray());
    }

    private static UnrealProject CreateProject() => new(
        "Game",
        new ProjectPath(@"D:\Projects\Game\Game.uproject"),
        "5.8",
        "5.8.1",
        ProjectType.Cpp,
        DateTimeOffset.UnixEpoch,
        LastLaunched: null,
        IsFavorite: false,
        ProjectState.Available,
        EngineResolutionState.Resolved);

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateGitStatusService(GitProjectStatus result)
        : IGitStatusService
    {
        private int _includedRemotes;

        public bool IncludedRemotes => Volatile.Read(ref _includedRemotes) != 0;

        public Task<GitProjectStatus> GetStatusAsync(
            string projectDirectory,
            bool includeRemotes = false,
            CancellationToken cancellationToken = default)
        {
            if (includeRemotes)
            {
                Interlocked.Exchange(ref _includedRemotes, 1);
            }

            return Task.FromResult(result);
        }
    }

    private sealed class BlockingGitStatusService : IGitStatusService
    {
        private readonly object _gate = new();
        private readonly List<TaskCompletionSource<GitProjectStatus>> _calls = [];

        public Task<GitProjectStatus> GetStatusAsync(
            string projectDirectory,
            bool includeRemotes = false,
            CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<GitProjectStatus> call;
            lock (_gate)
            {
                call = new TaskCompletionSource<GitProjectStatus>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _calls.Add(call);
            }

            return call.Task.WaitAsync(cancellationToken);
        }

        public Task WaitForStartedAsync() => WaitForCallCountAsync(1);

        public async Task WaitForCallCountAsync(int expected)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                lock (_gate)
                {
                    if (_calls.Count >= expected)
                    {
                        return;
                    }
                }

                await Task.Delay(10);
            }

            throw new TimeoutException("Git query did not start.");
        }

        public void CompleteAll(GitProjectStatus result)
        {
            lock (_gate)
            {
                foreach (var call in _calls)
                {
                    call.TrySetResult(result);
                }
            }
        }
    }

    private sealed class FakeWebUrlLauncher : IWebUrlLauncher
    {
        public List<string> Urls { get; } = [];

        public LaunchResult Open(string url)
        {
            Urls.Add(url);
            return LaunchResult.Succeeded();
        }
    }
}
