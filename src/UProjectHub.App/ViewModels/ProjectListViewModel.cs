using System.Collections.ObjectModel;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Models;
using UProjectHub.Core.Settings;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectListViewModel : ObservableObject
{
    private readonly ObservableCollection<ProjectRowViewModel> _rows = [];
    private readonly Func<UnrealProject, ProjectContextActionsViewModel>?
        _contextActionsFactory;
    private readonly LocalizationService? _localization;
    private readonly ProjectDiagnosticSnapshotStore? _diagnostics;
    private readonly ProjectGitStatusStore? _gitStatuses;
    private int _totalCount;
    private int _visibleCount;
    private IReadOnlyList<ColumnLayoutState> _columnLayout = [];

    public ProjectListViewModel(
        Func<UnrealProject, ProjectContextActionsViewModel>?
            contextActionsFactory = null,
        LocalizationService? localization = null,
        ProjectDiagnosticSnapshotStore? diagnostics = null,
        ProjectGitStatusStore? gitStatuses = null)
    {
        _contextActionsFactory = contextActionsFactory;
        _localization = localization;
        _diagnostics = diagnostics;
        _gitStatuses = gitStatuses;
        if (_diagnostics is not null)
        {
            _diagnostics.SnapshotChanged += OnDiagnosticSnapshotChanged;
        }
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }
        if (_gitStatuses is not null)
        {
            _gitStatuses.StatusChanged += OnGitStatusChanged;
        }
        Rows = new ReadOnlyObservableCollection<ProjectRowViewModel>(_rows);
    }

    public ReadOnlyObservableCollection<ProjectRowViewModel> Rows { get; }

    public int TotalCount => _totalCount;

    public int VisibleCount => _visibleCount;

    public string ShowingCountText => string.Format(
        Localize("String.ShowingCountFormat", "Showing {0} of {1}"),
        VisibleCount,
        TotalCount);

    public bool HasVisibleRows => VisibleCount > 0;

    public bool IsNoProjectsState => TotalCount == 0;

    public bool IsNoResultsState => TotalCount > 0 && VisibleCount == 0;

    public IReadOnlyList<ColumnLayoutState> ColumnLayout => _columnLayout;

    public void SetColumnLayout(IReadOnlyList<ColumnLayoutState> columnLayout)
    {
        ArgumentNullException.ThrowIfNull(columnLayout);
        SetProperty(
            ref _columnLayout,
            Array.AsReadOnly(columnLayout.ToArray()),
            nameof(ColumnLayout));
    }

    public void SetSnapshot(ProjectCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _diagnostics?.Prune(snapshot.Projects);

        SetRows(snapshot.Projects, snapshot.Projects.Count);
        if (_gitStatuses is not null)
        {
            _ = _gitStatuses.UpdateCatalog(snapshot.Projects);
        }
    }

    public void SetVisibleProjects(IEnumerable<UnrealProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        SetRows(projects, TotalCount);
    }

    private void SetRows(IEnumerable<UnrealProject> projects, int totalCount)
    {
        var rows = projects
            .Select(project => new ProjectRowViewModel(
                project,
                _contextActionsFactory?.Invoke(project),
                _diagnostics?.TryGet(project),
                _localization,
                _gitStatuses?.TryGet(project)))
            .ToArray();

        _rows.Clear();
        foreach (var row in rows)
        {
            _rows.Add(row);
        }

        var totalChanged = SetProperty(ref _totalCount, totalCount, nameof(TotalCount));
        var visibleChanged = SetProperty(ref _visibleCount, rows.Length, nameof(VisibleCount));

        if (totalChanged || visibleChanged)
        {
            OnPropertyChanged(nameof(ShowingCountText));
            OnPropertyChanged(nameof(HasVisibleRows));
            OnPropertyChanged(nameof(IsNoProjectsState));
            OnPropertyChanged(nameof(IsNoResultsState));
        }
    }

    private string Localize(string key, string fallback) =>
        _localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;

    private void OnDiagnosticSnapshotChanged(
        object? sender,
        ProjectDiagnosticSnapshotChangedEventArgs eventArgs)
    {
        if (eventArgs.IsFullSnapshot)
        {
            foreach (var row in _rows)
            {
                row.UpdateDiagnosticReport(_diagnostics?.TryGet(row.Project));
            }

            return;
        }

        if (eventArgs.ProjectPath is null || eventArgs.Report is null)
        {
            return;
        }

        foreach (var row in _rows.Where(row =>
                     row.Project.ProjectFilePath.Equals(eventArgs.ProjectPath)))
        {
            row.UpdateDiagnosticReport(eventArgs.Report);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(ShowingCountText));
        foreach (var row in _rows)
        {
            row.RefreshDiagnosticPresentation();
            row.RefreshGitPresentation();
        }
    }

    private void OnGitStatusChanged(
        object? sender,
        ProjectGitStatusChangedEventArgs eventArgs)
    {
        foreach (var row in _rows.Where(row =>
                     row.Project.ProjectFilePath.Equals(eventArgs.ProjectPath)))
        {
            row.UpdateGitStatus(eventArgs.Status);
        }
    }
}
