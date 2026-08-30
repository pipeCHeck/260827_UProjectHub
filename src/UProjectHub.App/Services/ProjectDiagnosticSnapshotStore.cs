using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;

namespace UProjectHub.App.Services;

public sealed class ProjectDiagnosticSnapshotStore(
    ProjectDiagnosticsService diagnostics)
{
    private readonly ProjectDiagnosticsService _diagnostics = diagnostics
        ?? throw new ArgumentNullException(nameof(diagnostics));
    private readonly Dictionary<string, ProjectDiagnosticReport> _reports =
        new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<ProjectDiagnosticSnapshotChangedEventArgs>?
        SnapshotChanged;

    public void Refresh(IEnumerable<UnrealProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        foreach (var project in projects)
        {
            Refresh(project);
        }
    }

    public ProjectDiagnosticReport Refresh(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var report = _diagnostics.Diagnose(project);
        _reports[project.ProjectFilePath.Value] = report;
        SnapshotChanged?.Invoke(
            this,
            new ProjectDiagnosticSnapshotChangedEventArgs(
                project.ProjectFilePath,
                report));
        return report;
    }

    public ProjectDiagnosticReport Get(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return _reports.TryGetValue(project.ProjectFilePath.Value, out var report)
            ? report
            : Refresh(project);
    }
}

public sealed record ProjectDiagnosticSnapshotChangedEventArgs(
    ProjectPath ProjectPath,
    ProjectDiagnosticReport Report);
