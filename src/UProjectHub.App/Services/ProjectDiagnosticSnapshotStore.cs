using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;

namespace UProjectHub.App.Services;

public sealed class ProjectDiagnosticSnapshotStore(
    ProjectDiagnosticsService diagnostics)
{
    private readonly ProjectDiagnosticsService _diagnostics = diagnostics
        ?? throw new ArgumentNullException(nameof(diagnostics));
    private readonly object _gate = new();
    private readonly Dictionary<string, ProjectDiagnosticReport> _reports =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pathRevisions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _latestProjectRefreshes =
        new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _catalogPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _hasCatalogSnapshot;
    private long _revision;

    public event EventHandler<ProjectDiagnosticSnapshotChangedEventArgs>?
        SnapshotChanged;

    public ProjectDiagnosticSnapshot CreateSnapshot(
        IEnumerable<UnrealProject> projects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var projectSnapshot = projects.ToArray();
        long startRevision;
        lock (_gate)
        {
            startRevision = _revision;
        }

        var reports = new List<ProjectDiagnosticReport>();
        foreach (var project in projectSnapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reports.Add(_diagnostics.Diagnose(project));
        }

        return new ProjectDiagnosticSnapshot(
            startRevision,
            Array.AsReadOnly(reports.ToArray()));
    }

    public void Replace(
        ProjectDiagnosticSnapshot snapshot,
        IEnumerable<UnrealProject> currentProjects)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(currentProjects);

        var currentPaths = currentProjects
            .Select(project => project.ProjectFilePath.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            SynchronizeCatalog(currentPaths);
            foreach (var report in snapshot.Reports)
            {
                var path = report.ProjectPath.Value;
                if (!currentPaths.Contains(path)
                    || GetPathRevision(path) > snapshot.StartRevision)
                {
                    continue;
                }

                _reports[path] = report;
            }
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
        lock (_gate)
        {
            SynchronizeCatalog(retainedPaths);
        }
    }

    public async Task<ProjectDiagnosticReport?> RefreshAsync(
        UnrealProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var refreshRevision = BeginProjectRefresh(project);
        var diagnosticTask = Task.Run(
            () => _diagnostics.Diagnose(project),
            CancellationToken.None);
        var report = await diagnosticTask.WaitAsync(cancellationToken);
        return ApplyProjectRefresh(project, report, refreshRevision);
    }

    private ProjectDiagnosticReport? ApplyProjectRefresh(
        UnrealProject project,
        ProjectDiagnosticReport report,
        long refreshRevision)
    {
        var path = project.ProjectFilePath.Value;
        lock (_gate)
        {
            if (!_latestProjectRefreshes.TryGetValue(path, out var latestRevision)
                || latestRevision != refreshRevision
                || (_hasCatalogSnapshot && !_catalogPaths.Contains(path)))
            {
                return _reports.TryGetValue(path, out var currentReport)
                    ? currentReport
                    : null;
            }

            _reports[path] = report;
            _pathRevisions[path] = refreshRevision;
        }

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

        lock (_gate)
        {
            return _reports.TryGetValue(
                project.ProjectFilePath.Value,
                out var report)
                ? report
                : null;
        }
    }

    private long BeginProjectRefresh(UnrealProject project)
    {
        lock (_gate)
        {
            var revision = ++_revision;
            _latestProjectRefreshes[project.ProjectFilePath.Value] = revision;
            return revision;
        }
    }

    private void SynchronizeCatalog(HashSet<string> currentPaths)
    {
        if (!_hasCatalogSnapshot)
        {
            foreach (var path in currentPaths)
            {
                MarkPathChanged(path);
            }

            _catalogPaths = currentPaths;
            _hasCatalogSnapshot = true;
            return;
        }

        foreach (var path in _catalogPaths
                     .Where(path => !currentPaths.Contains(path))
                     .Concat(currentPaths.Where(path => !_catalogPaths.Contains(path)))
                     .ToArray())
        {
            MarkPathChanged(path);
        }

        foreach (var removedPath in _catalogPaths
                     .Where(path => !currentPaths.Contains(path)))
        {
            _reports.Remove(removedPath);
        }

        _catalogPaths = currentPaths;
    }

    private void MarkPathChanged(string path)
    {
        var revision = ++_revision;
        _pathRevisions[path] = revision;
        _latestProjectRefreshes[path] = revision;
    }

    private long GetPathRevision(string path) =>
        _pathRevisions.TryGetValue(path, out var revision) ? revision : 0;
}

public sealed record ProjectDiagnosticSnapshot(
    long StartRevision,
    IReadOnlyList<ProjectDiagnosticReport> Reports);

public sealed record ProjectDiagnosticSnapshotChangedEventArgs(
    ProjectPath? ProjectPath,
    ProjectDiagnosticReport? Report,
    bool IsFullSnapshot);
