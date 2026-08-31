using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Windows.SourceControl;

namespace UProjectHub.App.Services;

public sealed class ProjectGitStatusStore : IAsyncDisposable
{
    private readonly IGitStatusService _git;
    private readonly IUiDispatcher _dispatcher;
    private readonly object _gate = new();
    private readonly Queue<RefreshRequest> _priorityQueue = [];
    private readonly Queue<RefreshRequest> _backgroundQueue = [];
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Task[] _workers;
    private readonly Dictionary<string, GitProjectStatus> _statuses =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _latestRevisions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<GitProjectStatus?>>
        _backgroundPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<GitProjectStatus?>>
        _explicitPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TaskCompletionSource<GitProjectStatus?>>
        _revalidationsWaitingForExplicit =
            new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _catalogPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private long _revision;
    private bool _isDisposed;

    public ProjectGitStatusStore(
        IGitStatusService git,
        IUiDispatcher dispatcher,
        int maxConcurrency = 2)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        }

        _workers = Enumerable.Range(0, maxConcurrency)
            .Select(_ => RunWorkerAsync())
            .ToArray();
    }

    public event EventHandler<ProjectGitStatusChangedEventArgs>? StatusChanged;

    public Task UpdateCatalog(IEnumerable<UnrealProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var snapshot = projects.ToArray();
        var currentPaths = snapshot
            .Select(project => project.ProjectFilePath.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = new List<Task>();
        var signalCount = 0;

        lock (_gate)
        {
            ThrowIfDisposed();
            foreach (var removedPath in _catalogPaths.Where(path =>
                         !currentPaths.Contains(path)))
            {
                _statuses.Remove(removedPath);
                _latestRevisions[removedPath] = ++_revision;
                _backgroundPending.Remove(removedPath);
                _explicitPending.Remove(removedPath);
                if (_revalidationsWaitingForExplicit.Remove(
                        removedPath,
                        out var revalidation))
                {
                    revalidation.TrySetResult(null);
                }
            }

            foreach (var unavailablePath in snapshot
                         .Where(project =>
                             project.ProjectState != ProjectState.Available)
                         .Select(project => project.ProjectFilePath.Value))
            {
                var hadPendingState = _statuses.Remove(unavailablePath)
                    | _backgroundPending.Remove(unavailablePath)
                    | _explicitPending.Remove(unavailablePath);
                if (_revalidationsWaitingForExplicit.Remove(
                        unavailablePath,
                        out var revalidation))
                {
                    revalidation.TrySetResult(null);
                    hadPendingState = true;
                }

                if (hadPendingState)
                {
                    _latestRevisions[unavailablePath] = ++_revision;
                }
            }

            _catalogPaths = currentPaths;
            foreach (var project in snapshot.Where(project =>
                         project.ProjectState == ProjectState.Available))
            {
                var path = project.ProjectFilePath.Value;
                if (_statuses.ContainsKey(path))
                {
                    continue;
                }

                if (_explicitPending.TryGetValue(path, out var explicitRefresh))
                {
                    pending.Add(explicitRefresh);
                    continue;
                }

                if (_backgroundPending.TryGetValue(path, out var existing))
                {
                    pending.Add(existing);
                    continue;
                }

                var request = CreateRequest(
                    project,
                    includeRemotes: false,
                    isExplicit: false);
                _backgroundQueue.Enqueue(request);
                _backgroundPending[path] = request.Completion.Task;
                pending.Add(request.Completion.Task);
                signalCount++;
            }
        }

        if (signalCount > 0)
        {
            _queueSignal.Release(signalCount);
        }

        return pending.Count == 0 ? Task.CompletedTask : Task.WhenAll(pending);
    }

    public async Task<GitProjectStatus?> RefreshAsync(
        UnrealProject project,
        bool includeRemotes = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        RefreshRequest request;
        lock (_gate)
        {
            ThrowIfDisposed();
            request = CreateRequest(
                project,
                includeRemotes,
                isExplicit: true,
                cancellationToken);
            _priorityQueue.Enqueue(request);
            _explicitPending[project.ProjectFilePath.Value] =
                request.Completion.Task;
        }

        _queueSignal.Release();
        return await request.Completion.Task
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task RevalidateCatalog(IEnumerable<UnrealProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var snapshot = projects.ToArray();
        var pending = new List<Task<GitProjectStatus?>>();
        var signalCount = 0;

        lock (_gate)
        {
            ThrowIfDisposed();
            foreach (var project in snapshot.Where(project =>
                         project.ProjectState == ProjectState.Available
                         && _catalogPaths.Contains(
                             project.ProjectFilePath.Value)))
            {
                var path = project.ProjectFilePath.Value;
                if (_revalidationsWaitingForExplicit.TryGetValue(
                        path,
                        out var existingRevalidation))
                {
                    pending.Add(existingRevalidation.Task);
                    continue;
                }

                var revalidation =
                    new TaskCompletionSource<GitProjectStatus?>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                _revalidationsWaitingForExplicit[path] = revalidation;
                pending.Add(revalidation.Task);

                if (_explicitPending.ContainsKey(path))
                {
                    continue;
                }

                var request = CreateRequest(
                    project,
                    includeRemotes: false,
                    isExplicit: false);
                _backgroundQueue.Enqueue(request);
                _backgroundPending[path] = request.Completion.Task;
                signalCount++;
            }
        }

        if (pending.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (signalCount > 0)
        {
            _queueSignal.Release(signalCount);
        }

        return Task.WhenAll(pending);
    }

    public GitProjectStatus? TryGet(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        lock (_gate)
        {
            return _statuses.TryGetValue(project.ProjectFilePath.Value, out var status)
                ? status
                : null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        _lifetimeCancellation.Cancel();
        _queueSignal.Release(_workers.Length);
        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Lifetime cancellation is the normal worker shutdown path.
        }

        lock (_gate)
        {
            CancelPending(_priorityQueue);
            CancelPending(_backgroundQueue);
            foreach (var revalidation in
                     _revalidationsWaitingForExplicit.Values)
            {
                revalidation.TrySetCanceled(_lifetimeCancellation.Token);
            }

            _backgroundPending.Clear();
            _explicitPending.Clear();
            _revalidationsWaitingForExplicit.Clear();
        }

        _lifetimeCancellation.Dispose();
        _queueSignal.Dispose();
    }

    private RefreshRequest CreateRequest(
        UnrealProject project,
        bool includeRemotes,
        bool isExplicit,
        CancellationToken cancellationToken = default)
    {
        var revision = ++_revision;
        _latestRevisions[project.ProjectFilePath.Value] = revision;
        return new RefreshRequest(
            project,
            includeRemotes,
            isExplicit,
            revision,
            cancellationToken);
    }

    private async Task RunWorkerAsync()
    {
        var lifetimeToken = _lifetimeCancellation.Token;
        while (true)
        {
            try
            {
                await _queueSignal.WaitAsync(lifetimeToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            RefreshRequest? request;
            lock (_gate)
            {
                request = _priorityQueue.Count > 0
                    ? _priorityQueue.Dequeue()
                    : _backgroundQueue.Count > 0
                        ? _backgroundQueue.Dequeue()
                        : null;
            }

            if (request is null)
            {
                continue;
            }

            await ProcessRequestAsync(request, lifetimeToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessRequestAsync(
        RefreshRequest request,
        CancellationToken lifetimeToken)
    {
        if (!ShouldRun(request))
        {
            CompleteWithoutApplying(request);
            return;
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeToken,
                request.CancellationToken);
            var status = await _git.GetStatusAsync(
                request.Project.ProjectDirectory,
                request.IncludeRemotes,
                linked.Token).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(
                () => Apply(request, status),
                lifetimeToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            lifetimeToken.IsCancellationRequested
            || request.CancellationToken.IsCancellationRequested)
        {
            request.Completion.TrySetCanceled(
                request.CancellationToken.IsCancellationRequested
                    ? request.CancellationToken
                    : lifetimeToken);
            RemovePending(request);
            QueueBackgroundFallbackAfterCancelledExplicit(request);
        }
        catch (Exception exception)
        {
            var failed = new GitProjectStatus(
                GitProjectState.Failed,
                ErrorMessage: exception.Message);
            try
            {
                await _dispatcher.InvokeAsync(
                    () => Apply(request, failed),
                    lifetimeToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
            {
                request.Completion.TrySetCanceled(lifetimeToken);
                RemovePending(request);
            }
        }
    }

    private bool ShouldRun(RefreshRequest request)
    {
        lock (_gate)
        {
            var path = request.Project.ProjectFilePath.Value;
            return !request.CancellationToken.IsCancellationRequested
                && _catalogPaths.Contains(path)
                && _latestRevisions.TryGetValue(path, out var revision)
                && revision == request.Revision;
        }
    }

    private void Apply(RefreshRequest request, GitProjectStatus status)
    {
        var path = request.Project.ProjectFilePath.Value;
        var applied = false;
        GitProjectStatus? current;
        TaskCompletionSource<GitProjectStatus?>? sharedRevalidation = null;
        lock (_gate)
        {
            if (_catalogPaths.Contains(path)
                && _latestRevisions.TryGetValue(path, out var revision)
                && revision == request.Revision)
            {
                _statuses[path] = status;
                applied = true;
                _revalidationsWaitingForExplicit.Remove(
                    path,
                    out sharedRevalidation);
            }

            current = _statuses.TryGetValue(path, out var stored) ? stored : null;
            RemoveBackgroundPendingUnsafe(request);
            RemoveExplicitPendingUnsafe(request);
        }

        request.Completion.TrySetResult(current);
        sharedRevalidation?.TrySetResult(status);
        if (applied)
        {
            StatusChanged?.Invoke(
                this,
                new ProjectGitStatusChangedEventArgs(
                    request.Project.ProjectFilePath,
                    status));
        }
    }

    private void CompleteWithoutApplying(RefreshRequest request)
    {
        GitProjectStatus? current;
        lock (_gate)
        {
            var path = request.Project.ProjectFilePath.Value;
            current = _statuses.TryGetValue(path, out var stored) ? stored : null;
            RemoveBackgroundPendingUnsafe(request);
            RemoveExplicitPendingUnsafe(request);
        }

        request.Completion.TrySetResult(current);
        QueueBackgroundFallbackAfterCancelledExplicit(request);
    }

    private void QueueBackgroundFallbackAfterCancelledExplicit(
        RefreshRequest request)
    {
        var queued = false;
        lock (_gate)
        {
            var path = request.Project.ProjectFilePath.Value;
            if (!_isDisposed
                && request.IsExplicit
                && request.CancellationToken.IsCancellationRequested
                && _catalogPaths.Contains(path)
                && (_revalidationsWaitingForExplicit.ContainsKey(path)
                    || !_statuses.ContainsKey(path))
                && _latestRevisions.TryGetValue(path, out var revision)
                && revision == request.Revision)
            {
                var fallback = CreateRequest(
                    request.Project,
                    includeRemotes: false,
                    isExplicit: false);
                _backgroundQueue.Enqueue(fallback);
                _backgroundPending[path] = fallback.Completion.Task;
                queued = true;
            }
        }

        if (queued)
        {
            _queueSignal.Release();
        }
    }

    private void RemovePending(RefreshRequest request)
    {
        lock (_gate)
        {
            RemoveBackgroundPendingUnsafe(request);
            RemoveExplicitPendingUnsafe(request);
        }
    }

    private void RemoveBackgroundPendingUnsafe(RefreshRequest request)
    {
        var path = request.Project.ProjectFilePath.Value;
        if (_backgroundPending.TryGetValue(path, out var pending)
            && ReferenceEquals(pending, request.Completion.Task))
        {
            _backgroundPending.Remove(path);
        }
    }

    private void RemoveExplicitPendingUnsafe(RefreshRequest request)
    {
        var path = request.Project.ProjectFilePath.Value;
        if (_explicitPending.TryGetValue(path, out var pending)
            && ReferenceEquals(pending, request.Completion.Task))
        {
            _explicitPending.Remove(path);
        }
    }

    private static void CancelPending(Queue<RefreshRequest> queue)
    {
        while (queue.TryDequeue(out var request))
        {
            request.Completion.TrySetCanceled();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private sealed record RefreshRequest(
        UnrealProject Project,
        bool IncludeRemotes,
        bool IsExplicit,
        long Revision,
        CancellationToken CancellationToken)
    {
        public TaskCompletionSource<GitProjectStatus?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public sealed record ProjectGitStatusChangedEventArgs(
    ProjectPath ProjectPath,
    GitProjectStatus Status);
