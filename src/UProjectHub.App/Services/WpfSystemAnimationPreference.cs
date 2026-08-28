using System.ComponentModel;
using System.Windows;

namespace UProjectHub.App.Services;

public sealed class WpfSystemAnimationPreference : ISystemAnimationPreference, IDisposable
{
    private bool _areAnimationsEnabled = SystemParameters.ClientAreaAnimation;
    private bool _isDisposed;

    public WpfSystemAnimationPreference()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    public bool AreAnimationsEnabled => _areAnimationsEnabled;

    public event EventHandler? PreferenceChanged;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _isDisposed = true;
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(SystemParameters.ClientAreaAnimation))
        {
            return;
        }

        var currentValue = SystemParameters.ClientAreaAnimation;
        if (_areAnimationsEnabled == currentValue)
        {
            return;
        }

        _areAnimationsEnabled = currentValue;
        PreferenceChanged?.Invoke(this, EventArgs.Empty);
    }
}
