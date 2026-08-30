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

    public ProjectDiagnosticSnapshot CreateSnapshot(
        IEnumerable<UnrealProject> projects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var reports = new List<ProjectDiagnosticReport>();
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reports.Add(_diagnostics.Diagnose(project));
        }

        return new ProjectDiagnosticSnapshot(
            Array.AsReadOnly(reports.ToArray()));
    }

    public void Replace(ProjectDiagnosticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _reports.Clear();
        foreach (var report in snapshot.Reports)
        {
            _reports[report.ProjectPath.Value] = report;
        }

        SnapshotChanged?.Invoke(
            this,
            new ProjectDiagnosticSnapshotChangedEventArgs(
                ProjectPath: null,
                Report: null,
                IsFullSnapshot: true));
    }

    public void Prune(IEnumerable<UnrealProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var retainedPaths = projects
            .Select(project => project.ProjectFilePath.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removedPath in _reports.Keys
                     .Where(path => !retainedPaths.Contains(path))
                     .ToArray())
        {
            _reports.Remove(removedPath);
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
                report,
                IsFullSnapshot: false));
        return report;
    }

    public ProjectDiagnosticReport? TryGet(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return _reports.TryGetValue(project.ProjectFilePath.Value, out var report)
            ? report
            : null;
    }

    public ProjectDiagnosticReport Get(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return _reports.TryGetValue(project.ProjectFilePath.Value, out var report)
            ? report
            : Refresh(project);
    }
}

public sealed record ProjectDiagnosticSnapshot(
    IReadOnlyList<ProjectDiagnosticReport> Reports);

public sealed record ProjectDiagnosticSnapshotChangedEventArgs(
    ProjectPath? ProjectPath,
    ProjectDiagnosticReport? Report,
    bool IsFullSnapshot);
