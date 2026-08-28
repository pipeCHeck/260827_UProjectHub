using System.Windows.Threading;

namespace UProjectHub.App.Services;

public interface IUiDispatcher
{
    Task InvokeAsync(
        Action action,
        CancellationToken cancellationToken = default);
}

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task InvokeAsync(
        Action action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await _dispatcher.InvokeAsync(
            action,
            DispatcherPriority.DataBind,
            cancellationToken);
    }
}

public sealed class UiUpdateBatcher<T>
{
    private readonly int _batchSize;
    private readonly Func<IReadOnlyList<T>, CancellationToken, Task> _publishAsync;
    private readonly object _gate = new();
    private readonly List<T> _pending = [];
    private Task _publishTail = Task.CompletedTask;

    public UiUpdateBatcher(
        int batchSize,
        Func<IReadOnlyList<T>, CancellationToken, Task> publishAsync)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        _batchSize = batchSize;
        _publishAsync = publishAsync ?? throw new ArgumentNullException(nameof(publishAsync));
    }

    public void Add(T item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _pending.Add(item);
            if (_pending.Count < _batchSize)
            {
                return;
            }

            QueuePendingBatch(cancellationToken);
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_pending.Count > 0)
            {
                QueuePendingBatch(cancellationToken);
            }

            return _publishTail;
        }
    }

    private void QueuePendingBatch(CancellationToken cancellationToken)
    {
        var batch = Array.AsReadOnly(_pending.ToArray());
        _pending.Clear();
        _publishTail = PublishAfterAsync(
            _publishTail,
            batch,
            cancellationToken);
    }

    private async Task PublishAfterAsync(
        Task previous,
        IReadOnlyList<T> batch,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        await previous;
        cancellationToken.ThrowIfCancellationRequested();
        await _publishAsync(batch, cancellationToken);
    }
}
