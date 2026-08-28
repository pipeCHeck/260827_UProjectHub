using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using UProjectHub.App.Composition;

namespace UProjectHub.App;

public partial class App : Application
{
    private AppRuntime? _runtime;
    private Task? _startupTask;
    private bool _shutdownInProgress;
    private bool _shutdownReady;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var bootstrapper = new AppBootstrapper();
        _runtime = bootstrapper.Build();
        var window = new MainWindow
        {
            DataContext = _runtime.MainViewModel,
        };
        window.Closing += OnMainWindowClosing;

        MainWindow = window;
        window.Show();

        _startupTask = _runtime.Coordinator.StartAsync();
        try
        {
            await _startupTask;
        }
        catch (OperationCanceledException)
        {
            // Window shutdown owns cancellation of startup/background work.
        }
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_shutdownReady || _runtime is null)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (_shutdownInProgress)
        {
            return;
        }

        _shutdownInProgress = true;
        await _runtime.Coordinator.StopAsync();
        if (_startupTask is not null)
        {
            try
            {
                await _startupTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _runtime.MotionService.Dispose();
        _shutdownReady = true;
        if (sender is Window window)
        {
            _ = window.Dispatcher.BeginInvoke(
                window.Close,
                DispatcherPriority.ApplicationIdle);
        }
    }
}
