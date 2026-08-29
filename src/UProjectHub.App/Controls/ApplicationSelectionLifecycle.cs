using System.Windows;
using System.Windows.Interop;

namespace UProjectHub.App.Controls;

public sealed class ApplicationActivationChangedEventArgs(
    bool isApplicationActive) : EventArgs
{
    public bool IsApplicationActive { get; } = isApplicationActive;
}

public interface IApplicationActivationSource
{
    event EventHandler<ApplicationActivationChangedEventArgs>?
        ActivationChanged;
}

public sealed class ApplicationSelectionLifecycle : IDisposable
{
    private readonly IApplicationActivationSource _activationSource;
    private readonly Action _clearSelection;
    private bool _isAttached;

    public ApplicationSelectionLifecycle(
        IApplicationActivationSource activationSource,
        Action clearSelection)
    {
        _activationSource = activationSource
            ?? throw new ArgumentNullException(nameof(activationSource));
        _clearSelection = clearSelection
            ?? throw new ArgumentNullException(nameof(clearSelection));
    }

    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _activationSource.ActivationChanged += OnActivationChanged;
        _isAttached = true;
    }

    public void Dispose()
    {
        if (!_isAttached)
        {
            return;
        }

        _activationSource.ActivationChanged -= OnActivationChanged;
        _isAttached = false;
    }

    private void OnActivationChanged(
        object? sender,
        ApplicationActivationChangedEventArgs eventArgs)
    {
        if (!eventArgs.IsApplicationActive)
        {
            _clearSelection();
        }
    }
}

internal sealed class WindowApplicationActivationSource :
    IApplicationActivationSource,
    IDisposable
{
    private const int WmActivateApp = 0x001C;

    private readonly HwndSource _source;
    private readonly HwndSourceHook _hook;
    private bool _isDisposed;

    public WindowApplicationActivationSource(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException(
                "The application window source is unavailable.");
        _hook = OnWindowMessage;
        _source.AddHook(_hook);
    }

    public event EventHandler<ApplicationActivationChangedEventArgs>?
        ActivationChanged;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _source.RemoveHook(_hook);
    }

    private IntPtr OnWindowMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (!_isDisposed && message == WmActivateApp)
        {
            ActivationChanged?.Invoke(
                this,
                new ApplicationActivationChangedEventArgs(
                    wordParameter != IntPtr.Zero));
        }

        return IntPtr.Zero;
    }
}
