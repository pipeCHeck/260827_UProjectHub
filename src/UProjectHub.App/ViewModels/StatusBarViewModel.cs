using UProjectHub.App.Infrastructure;

namespace UProjectHub.App.ViewModels;

public sealed class StatusBarViewModel : ObservableObject
{
    private string _statusText = "Ready";
    private bool _isOperationActive;

    public string StatusText => _statusText;

    public bool IsOperationActive => _isOperationActive;

    public void SetStatus(string statusText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusText);
        SetProperty(ref _statusText, statusText, nameof(StatusText));
    }

    public void SetOperationActive(bool isOperationActive)
    {
        SetProperty(ref _isOperationActive, isOperationActive, nameof(IsOperationActive));
    }
}
