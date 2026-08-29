using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;

namespace UProjectHub.App.ViewModels;

public sealed class StatusBarViewModel : ObservableObject
{
    private readonly LocalizationService? _localization;
    private string? _statusKey;
    private string _statusText;
    private bool _isOperationActive;
    private bool _areAnimationsEnabled = true;

    public StatusBarViewModel(LocalizationService? localization = null)
    {
        _localization = localization;
        _statusKey = "String.StatusReady";
        _statusText = Localize(_statusKey, "Ready");
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }
    }

    public string StatusText => _statusText;

    public bool IsOperationActive => _isOperationActive;

    public bool AreAnimationsEnabled => _areAnimationsEnabled;

    public void SetStatus(string statusText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusText);
        _statusKey = null;
        SetProperty(ref _statusText, statusText, nameof(StatusText));
    }

    public void SetLocalizedStatus(string resourceKey, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        _statusKey = resourceKey;
        SetProperty(
            ref _statusText,
            Localize(resourceKey, fallback),
            nameof(StatusText));
    }

    public void SetOperationActive(bool isOperationActive)
    {
        SetProperty(ref _isOperationActive, isOperationActive, nameof(IsOperationActive));
    }

    public void SetAnimationsEnabled(bool areAnimationsEnabled)
    {
        SetProperty(
            ref _areAnimationsEnabled,
            areAnimationsEnabled,
            nameof(AreAnimationsEnabled));
    }

    private string Localize(string key, string fallback) =>
        _localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;

    private void OnLanguageChanged(object? sender, EventArgs eventArgs)
    {
        if (_statusKey is not null)
        {
            SetProperty(
                ref _statusText,
                Localize(_statusKey, _statusText),
                nameof(StatusText));
        }
    }
}
