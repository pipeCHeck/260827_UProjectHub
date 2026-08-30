namespace UProjectHub.App.ViewModels;

public sealed class ProjectDetailsViewModel
{
    public ProjectDetailsViewModel(
        ProjectOverviewViewModel overview,
        ProjectDiagnosticsViewModel diagnostics)
    {
        Overview = overview ?? throw new ArgumentNullException(nameof(overview));
        Diagnostics = diagnostics
            ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public string Name => Overview.Name;

    public ProjectOverviewViewModel Overview { get; }

    public ProjectDiagnosticsViewModel Diagnostics { get; }
}
