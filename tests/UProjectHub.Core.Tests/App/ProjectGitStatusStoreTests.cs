using UProjectHub.App.Services;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Windows.SourceControl;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ProjectGitStatusStoreTests
{
    [TestMethod]
    public async Task CatalogQueuesBackgroundWorkWithTwoWayConcurrencyAndProgressiveResultsAsync()
    {
        var service = new ControlledGitStatusService();
        await using var store = new ProjectGitStatusStore(
            service,
            new ImmediateDispatcher(),
            maxConcurrency: 2);
        var projects = Enumerable.Range(1, 4).Select(CreateProject).ToArray();
        var changed = new TaskCompletionSource<ProjectGitStatusChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        store.StatusChanged += (_, eventArgs) => changed.TrySetResult(eventArgs);

        _ = store.UpdateCatalog(projects);
        await service.WaitForStartedCountAsync(2);

        Assert.AreEqual(2, service.MaxConcurrency);
        Assert.IsTrue(projects.All(project => store.TryGet(project) is null));

        service.Complete(projects[0].ProjectDirectory, GitProjectState.Clean);
        var firstChange = await changed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(projects[0].ProjectFilePath, firstChange.ProjectPath);
        Assert.AreEqual(GitProjectState.Clean, store.TryGet(projects[0])!.State);
        await service.WaitForStartedCountAsync(3);
        Assert.AreEqual(2, service.MaxConcurrency);
    }

    [TestMethod]
    public async Task ExplicitRefreshRunsBeforeQueuedBackgroundProjectsAsync()
    {
        var service = new ControlledGitStatusService();
        await using var store = new ProjectGitStatusStore(
            service,
            new ImmediateDispatcher(),
            maxConcurrency: 2);
        var projects = Enumerable.Range(1, 4).Select(CreateProject).ToArray();
        _ = store.UpdateCatalog(projects);
        await service.WaitForStartedCountAsync(2);

        var explicitRefresh = store.RefreshAsync(
            projects[3],
            includeRemotes: true);
        service.Complete(projects[0].ProjectDirectory, GitProjectState.Clean);
        await service.WaitForStartedCountAsync(3);

        Assert.AreEqual(
            projects[3].ProjectDirectory,
            service.StartedCalls[2].ProjectDirectory);
        Assert.IsTrue(service.StartedCalls[2].IncludeRemotes);

        service.Complete(projects[3].ProjectDirectory, GitProjectState.Changed);
        var result = await explicitRefresh.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(GitProjectState.Changed, result!.State);
    }

    [TestMethod]
    public async Task OlderBackgroundResultCannotOverwriteNewerExplicitRefreshAsync()
    {
        var service = new ControlledGitStatusService();
        await using var store = new ProjectGitStatusStore(
            service,
            new ImmediateDispatcher(),
            maxConcurrency: 2);
        var project = CreateProject(1);
        var backgroundRefresh = store.UpdateCatalog([project]);
        await service.WaitForStartedCountAsync(1);

        var explicitRefresh = store.RefreshAsync(project, includeRemotes: true);
        await service.WaitForStartedCountAsync(2);
        service.CompleteCall(1, GitProjectState.Changed);
        await explicitRefresh.WaitAsync(TimeSpan.FromSeconds(1));
        service.CompleteCall(0, GitProjectState.Clean);
        await backgroundRefresh.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(GitProjectState.Changed, store.TryGet(project)!.State);
    }

    [TestMethod]
    public async Task CancelledExplicitRefreshDoesNotDiscardInitialBackgroundStatusAsync()
    {
        var service = new ControlledGitStatusService();
        await using var store = new ProjectGitStatusStore(
            service,
            new ImmediateDispatcher(),
            maxConcurrency: 2);
        var project = CreateProject(1);
        var initialBackground = store.UpdateCatalog([project]);
        await service.WaitForStartedCountAsync(1);
        using var cancellation = new CancellationTokenSource();

        var explicitRefresh = store.RefreshAsync(
            project,
            includeRemotes: true,
            cancellation.Token);
        await service.WaitForStartedCountAsync(2);
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => explicitRefresh);
        await service.WaitForStartedCountAsync(3);
        service.CompleteCall(0, GitProjectState.Clean);
        service.CompleteCall(2, GitProjectState.Changed);
        await initialBackground.WaitAsync(TimeSpan.FromSeconds(1));
        await service.WaitForCompletedCountAsync(3);

        Assert.AreEqual(
            GitProjectState.Changed,
            store.TryGet(project)!.State);
        Assert.IsFalse(service.StartedCalls[2].IncludeRemotes);
    }

    [TestMethod]
    public async Task RemovedProjectDoesNotReturnWhenLateQueryCompletesAsync()
    {
        var service = new ControlledGitStatusService();
        await using var store = new ProjectGitStatusStore(
            service,
            new ImmediateDispatcher(),
            maxConcurrency: 2);
        var project = CreateProject(1);
        var backgroundRefresh = store.UpdateCatalog([project]);
        await service.WaitForStartedCountAsync(1);

        _ = store.UpdateCatalog([]);
        service.Complete(project.ProjectDirectory, GitProjectState.Changed);
        await backgroundRefresh.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsNull(store.TryGet(project));
    }

    [TestMethod]
    public async Task UnavailableProjectClearsStatusAndQueuesFreshQueryWhenAvailableAgainAsync()
    {
        var service = new ControlledGitStatusService();
        await using var store = new ProjectGitStatusStore(
            service,
            new ImmediateDispatcher(),
            maxConcurrency: 1);
        var project = CreateProject(1);
        var initial = store.UpdateCatalog([project]);
        await service.WaitForStartedCountAsync(1);
        service.CompleteCall(0, GitProjectState.Clean);
        await initial.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(GitProjectState.Clean, store.TryGet(project)!.State);

        var missing = project with { ProjectState = ProjectState.Missing };
        await store.UpdateCatalog([missing]);

        Assert.IsNull(store.TryGet(missing));

        var availableAgain = store.UpdateCatalog([project]);
        await service.WaitForStartedCountAsync(2);
        service.CompleteCall(1, GitProjectState.Changed);
        await availableAgain.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(GitProjectState.Changed, store.TryGet(project)!.State);
    }

    [TestMethod]
    public async Task RevalidateCatalogRefreshesExistingStatusOnceInBackgroundAsync()
    {
        var service = new ControlledGitStatusService();
        await using var store = new ProjectGitStatusStore(
            service,
            new ImmediateDispatcher(),
            maxConcurrency: 2);
        var project = CreateProject(1);
        var initial = store.UpdateCatalog([project]);
        await service.WaitForStartedCountAsync(1);
        service.CompleteCall(0, GitProjectState.Clean);
        await initial.WaitAsync(TimeSpan.FromSeconds(1));

        var revalidation = store.RevalidateCatalog([project]);
        await service.WaitForStartedCountAsync(2);

        Assert.AreEqual(GitProjectState.Clean, store.TryGet(project)!.State);
        service.CompleteCall(1, GitProjectState.Changed);
        await revalidation.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(GitProjectState.Changed, store.TryGet(project)!.State);
        Assert.HasCount(2, service.StartedCalls);
    }

    [TestMethod]
    public async Task CatalogRevalidationCannotSupersedePendingExplicitRefreshAsync()
    {
        var service = new ControlledGitStatusService();
        await using var store = new ProjectGitStatusStore(
            service,
            new ImmediateDispatcher(),
            maxConcurrency: 2);
        var project = CreateProject(1);
        var initial = store.UpdateCatalog([project]);
        await service.WaitForStartedCountAsync(1);
        service.CompleteCall(0, GitProjectState.Clean);
        await initial.WaitAsync(TimeSpan.FromSeconds(1));

        var explicitRefresh = store.RefreshAsync(
            project,
            includeRemotes: true);
        await service.WaitForStartedCountAsync(2);

        var revalidation = store.RevalidateCatalog([project]);
        service.CompleteCall(1, GitProjectState.Changed);

        var explicitResult = await explicitRefresh.WaitAsync(
            TimeSpan.FromSeconds(1));
        await revalidation.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(GitProjectState.Changed, explicitResult!.State);
        Assert.AreEqual(GitProjectState.Changed, store.TryGet(project)!.State);
        Assert.HasCount(2, service.StartedCalls);
        Assert.IsTrue(service.StartedCalls[1].IncludeRemotes);
    }

    [TestMethod]
    public async Task CancelledExplicitRefreshCannotConsumeSharedCatalogRevalidationAsync()
    {
        var service = new ControlledGitStatusService();
        await using var store = new ProjectGitStatusStore(
            service,
            new ImmediateDispatcher(),
            maxConcurrency: 2);
        var project = CreateProject(1);
        var initial = store.UpdateCatalog([project]);
        await service.WaitForStartedCountAsync(1);
        service.CompleteCall(0, GitProjectState.Clean);
        await initial.WaitAsync(TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();

        var explicitRefresh = store.RefreshAsync(
            project,
            includeRemotes: true,
            cancellation.Token);
        await service.WaitForStartedCountAsync(2);
        var revalidation = store.RevalidateCatalog([project]);

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => explicitRefresh);
        await service.WaitForStartedCountAsync(3);
        Assert.IsFalse(service.StartedCalls[2].IncludeRemotes);

        service.CompleteCall(2, GitProjectState.Changed);
        await revalidation.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(GitProjectState.Changed, store.TryGet(project)!.State);
        Assert.HasCount(3, service.StartedCalls);
    }

    [TestMethod]
    public async Task ExplicitCancellationCannotConsumeAnAlreadyQueuedCatalogRevalidationAsync()
    {
        var service = new ControlledGitStatusService();
        await using var store = new ProjectGitStatusStore(
            service,
            new ImmediateDispatcher(),
            maxConcurrency: 1);
        var project = CreateProject(1);
        var blocker = CreateProject(2);
        var initial = store.UpdateCatalog([project]);
        await service.WaitForStartedCountAsync(1);
        service.CompleteCall(0, GitProjectState.Clean);
        await initial.WaitAsync(TimeSpan.FromSeconds(1));

        var blockingRefresh = store.UpdateCatalog([project, blocker]);
        await service.WaitForStartedCountAsync(2);
        var revalidation = store.RevalidateCatalog([project]);
        using var cancellation = new CancellationTokenSource();
        var explicitRefresh = store.RefreshAsync(
            project,
            includeRemotes: true,
            cancellation.Token);

        service.CompleteCall(1, GitProjectState.Clean);
        await blockingRefresh.WaitAsync(TimeSpan.FromSeconds(1));
        await service.WaitForStartedCountAsync(3);
        Assert.IsTrue(service.StartedCalls[2].IncludeRemotes);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => explicitRefresh);

        var fallbackStarted = service.WaitForStartedCountAsync(4);
        var firstCompletion = await Task.WhenAny(revalidation, fallbackStarted)
            .WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreSame(
            fallbackStarted,
            firstCompletion,
            "The queued F5 revalidation completed without a fresh background query.");

        service.CompleteCall(3, GitProjectState.Changed);
        await revalidation.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(GitProjectState.Changed, store.TryGet(project)!.State);
        Assert.IsFalse(service.StartedCalls[3].IncludeRemotes);
    }

    private static UnrealProject CreateProject(int number) => new(
        $"Game{number}",
        new ProjectPath($@"D:\Projects\Game{number}\Game{number}.uproject"),
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

    private sealed class ControlledGitStatusService : IGitStatusService
    {
        private readonly object _gate = new();
        private readonly List<Call> _calls = [];
        private int _active;
        private int _completed;

        public IReadOnlyList<Call> StartedCalls
        {
            get
            {
                lock (_gate)
                {
                    return _calls.ToArray();
                }
            }
        }

        public int MaxConcurrency { get; private set; }

        public async Task<GitProjectStatus> GetStatusAsync(
            string projectDirectory,
            bool includeRemotes = false,
            CancellationToken cancellationToken = default)
        {
            var call = new Call(projectDirectory, includeRemotes);
            lock (_gate)
            {
                _calls.Add(call);
                _active++;
                MaxConcurrency = Math.Max(MaxConcurrency, _active);
                Monitor.PulseAll(_gate);
            }

            try
            {
                return await call.Completion.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                lock (_gate)
                {
                    _active--;
                    _completed++;
                    Monitor.PulseAll(_gate);
                }
            }
        }

        public void Complete(string projectDirectory, GitProjectState state)
        {
            Call call;
            lock (_gate)
            {
                call = _calls.First(candidate =>
                    candidate.ProjectDirectory == projectDirectory
                    && !candidate.Completion.Task.IsCompleted);
            }

            call.Completion.TrySetResult(new GitProjectStatus(state));
        }

        public void CompleteCall(int index, GitProjectState state)
        {
            Call call;
            lock (_gate)
            {
                call = _calls[index];
            }

            call.Completion.TrySetResult(new GitProjectStatus(state));
        }

        public Task WaitForStartedCountAsync(int count) =>
            WaitUntilAsync(() => _calls.Count >= count);

        public Task WaitForCompletedCountAsync(int count) =>
            WaitUntilAsync(() => _completed >= count);

        private async Task WaitUntilAsync(Func<bool> condition)
        {
            var timeout = DateTime.UtcNow.AddSeconds(2);
            while (true)
            {
                lock (_gate)
                {
                    if (condition())
                    {
                        return;
                    }
                }

                if (DateTime.UtcNow >= timeout)
                {
                    throw new TimeoutException("The expected Git call did not start.");
                }

                await Task.Delay(10);
            }
        }

        public sealed record Call(
            string ProjectDirectory,
            bool IncludeRemotes)
        {
            public TaskCompletionSource<GitProjectStatus> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
