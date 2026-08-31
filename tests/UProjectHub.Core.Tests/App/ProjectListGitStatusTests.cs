using System.ComponentModel;
using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Windows.SourceControl;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ProjectListGitStatusTests
{
    [TestMethod]
    public async Task SnapshotShowsRowsBeforeGitCompletesThenUpdatesOnlyMatchingRowAsync()
    {
        var git = new DeferredGitStatusService();
        await using var store = new ProjectGitStatusStore(
            git,
            new ImmediateDispatcher());
        var list = new ProjectListViewModel(gitStatuses: store);
        var first = CreateProject("First", 1);
        var second = CreateProject("Second", 2);

        var catalog = new ProjectCatalog();
        catalog.Upsert(first);
        catalog.Upsert(second);
        list.SetSnapshot(catalog.GetSnapshot());

        Assert.HasCount(2, list.Rows);
        Assert.IsNull(list.Rows[0].GitStatus);
        Assert.AreEqual(string.Empty, list.Rows[0].GitStatusDisplay);
        await git.WaitForCallsAsync(2);
        var changed = WaitForPropertyAsync(
            list.Rows.Single(row => row.Name == "Second"),
            nameof(ProjectRowViewModel.GitStatus));

        git.Complete(second.ProjectDirectory, GitProjectState.Changed);
        await changed.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsNull(list.Rows.Single(row => row.Name == "First").GitStatus);
        var secondRow = list.Rows.Single(row => row.Name == "Second");
        Assert.AreEqual(GitProjectState.Changed, secondRow.GitState);
        Assert.AreEqual("Changed", secondRow.GitStatusDisplay);
        Assert.IsTrue(secondRow.IsGitChanged);
    }

    [TestMethod]
    public async Task MissingSnapshotDoesNotRetainPreviouslyDisplayedGitStatusAsync()
    {
        var git = new DeferredGitStatusService();
        await using var store = new ProjectGitStatusStore(
            git,
            new ImmediateDispatcher(),
            maxConcurrency: 1);
        var list = new ProjectListViewModel(gitStatuses: store);
        var project = CreateProject("Game", 1);
        var catalog = new ProjectCatalog();
        catalog.Upsert(project);
        list.SetSnapshot(catalog.GetSnapshot());
        await git.WaitForCallsAsync(1);
        var statusChanged = WaitForPropertyAsync(
            list.Rows.Single(),
            nameof(ProjectRowViewModel.GitStatus));
        git.Complete(project.ProjectDirectory, GitProjectState.Clean);
        await statusChanged.WaitAsync(TimeSpan.FromSeconds(1));

        catalog.Upsert(project with { ProjectState = ProjectState.Missing });
        list.SetSnapshot(catalog.GetSnapshot());

        Assert.IsNull(list.Rows.Single().GitStatus);
        Assert.AreEqual(string.Empty, list.Rows.Single().GitStatusDisplay);
    }

    [TestMethod]
    [DataRow(GitProjectState.NotRepository, "Not Repository", false)]
    [DataRow(GitProjectState.Clean, "Clean", false)]
    [DataRow(GitProjectState.Changed, "Changed", true)]
    [DataRow(GitProjectState.Failed, "Failed", false)]
    [DataRow(GitProjectState.GitUnavailable, "Git Unavailable", false)]
    public void RowUsesAllRequiredGitStates(
        GitProjectState state,
        string expectedDisplay,
        bool expectedChanged)
    {
        var row = new ProjectRowViewModel(CreateProject("Game", 1));

        row.UpdateGitStatus(new GitProjectStatus(state));

        Assert.AreEqual(state, row.GitState);
        Assert.AreEqual(expectedDisplay, row.GitStatusDisplay);
        Assert.AreEqual(expectedChanged, row.IsGitChanged);
    }

    private static Task WaitForPropertyAsync(
        INotifyPropertyChanged source,
        string propertyName)
    {
        var changed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.PropertyChanged += Handler;
        return changed.Task;

        void Handler(object? sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName == propertyName)
            {
                source.PropertyChanged -= Handler;
                changed.TrySetResult();
            }
        }
    }

    private static UnrealProject CreateProject(string name, int number) => new(
        name,
        new ProjectPath($@"D:\Projects\{name}\{name}.uproject"),
        "5.8",
        "5.8.1",
        ProjectType.Cpp,
        DateTimeOffset.UnixEpoch.AddDays(number),
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

    private sealed class DeferredGitStatusService : IGitStatusService
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, TaskCompletionSource<GitProjectStatus>>
            _calls = new(StringComparer.OrdinalIgnoreCase);

        public Task<GitProjectStatus> GetStatusAsync(
            string projectDirectory,
            bool includeRemotes = false,
            CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<GitProjectStatus> completion;
            lock (_gate)
            {
                completion = new TaskCompletionSource<GitProjectStatus>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _calls[projectDirectory] = completion;
            }

            return completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete(string projectDirectory, GitProjectState state)
        {
            lock (_gate)
            {
                _calls[projectDirectory].TrySetResult(new GitProjectStatus(state));
            }
        }

        public async Task WaitForCallsAsync(int count)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                lock (_gate)
                {
                    if (_calls.Count >= count)
                    {
                        return;
                    }
                }

                await Task.Delay(10);
            }

            throw new TimeoutException("Git calls did not start.");
        }
    }
}
