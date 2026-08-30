using UProjectHub.Core.Diagnostics;

namespace UProjectHub.App.ViewModels;

public enum ProjectDetailsSection
{
    Overview = 0,
    Diagnostics = 1,
    TagsAndNotes = 2,
}

public sealed class ProjectDetailsViewModel : IDisposable
{
    private readonly object _lifetimeGate = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _isDisposed;

    public ProjectDetailsViewModel(
        ProjectOverviewViewModel overview,
        ProjectDiagnosticsViewModel diagnostics,
        ProjectNotesViewModel? notes = null,
        ProjectDetailsSection initialSection = ProjectDetailsSection.Overview)
    {
        Overview = overview ?? throw new ArgumentNullException(nameof(overview));
        Diagnostics = diagnostics
            ?? throw new ArgumentNullException(nameof(diagnostics));
        Notes = notes;
        SelectedSection = initialSection;
    }

    public string Name => Overview.Name;

    public ProjectOverviewViewModel Overview { get; }

    public ProjectDiagnosticsViewModel Diagnostics { get; }

    public ProjectNotesViewModel? Notes { get; }

    public ProjectDetailsSection SelectedSection { get; }

    public int SelectedTabIndex => (int)SelectedSection;

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
