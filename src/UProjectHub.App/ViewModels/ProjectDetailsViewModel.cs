using UProjectHub.Core.Diagnostics;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectDetailsViewModel : IDisposable
{
    private readonly object _lifetimeGate = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _isDisposed;

    public ProjectDetailsViewModel(
        ProjectOverviewViewModel overview,
        ProjectDiagnosticsViewModel diagnostics,
        ProjectNotesViewModel? notes = null)
    {
        Overview = overview ?? throw new ArgumentNullException(nameof(overview));
        Diagnostics = diagnostics
            ?? throw new ArgumentNullException(nameof(diagnostics));
        Notes = notes;
    }

    public string Name => Overview.Name;

    public ProjectOverviewViewModel Overview { get; }

    public ProjectDiagnosticsViewModel Diagnostics { get; }

    public ProjectNotesViewModel? Notes { get; }

    public async Task RefreshDiagnosticsAsync(
        Func<CancellationToken, Task<ProjectDiagnosticReport?>> refreshAsync)
    {
        ArgumentNullException.ThrowIfNull(refreshAsync);
        CancellationToken cancellationToken;
        lock (_lifetimeGate)
        {
            if (_isDisposed)
            {
                return;
            }

            cancellationToken = _lifetimeCancellation.Token;
        }

        try
        {
            var report = await refreshAsync(cancellationToken);
            if (report is null)
            {
                return;
            }

            lock (_lifetimeGate)
            {
                if (_isDisposed || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                Diagnostics.UpdateReport(report);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Closing the details surface cancels its derived refresh.
        }
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        Notes?.Dispose();
    }
}
