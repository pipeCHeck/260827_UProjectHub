using System.Windows.Input;

namespace UProjectHub.App.Infrastructure;

public sealed class AsyncRelayCommand : ObservableObject, ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    public AsyncRelayCommand(
        Func<object?, Task> execute,
        Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (SetProperty(ref _isExecuting, value))
            {
                RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanExecute(object? parameter)
    {
        return !IsExecuting && (_canExecute?.Invoke(parameter) ?? true);
    }

    public async void Execute(object? parameter)
    {
        await ExecuteAsync(parameter);
    }

    public Task ExecuteAsync() => ExecuteAsync(null);

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        IsExecuting = true;

        try
        {
            await _execute(parameter);
        }
        finally
        {
            IsExecuting = false;
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
