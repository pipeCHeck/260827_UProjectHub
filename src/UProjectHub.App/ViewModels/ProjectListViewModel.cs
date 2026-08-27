using System.Collections.ObjectModel;
using UProjectHub.App.Infrastructure;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Models;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectListViewModel : ObservableObject
{
    private readonly ObservableCollection<ProjectRowViewModel> _rows = [];
    private int _totalCount;
    private int _visibleCount;

    public ProjectListViewModel()
    {
        Rows = new ReadOnlyObservableCollection<ProjectRowViewModel>(_rows);
    }

    public ReadOnlyObservableCollection<ProjectRowViewModel> Rows { get; }

    public int TotalCount => _totalCount;

    public int VisibleCount => _visibleCount;

    public string ShowingCountText => $"Showing {VisibleCount} of {TotalCount}";

    public bool HasVisibleRows => VisibleCount > 0;

    public bool IsNoProjectsState => TotalCount == 0;

    public bool IsNoResultsState => TotalCount > 0 && VisibleCount == 0;

    public void SetSnapshot(ProjectCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        SetRows(snapshot.Projects, snapshot.Projects.Count);
    }

    public void SetVisibleProjects(IEnumerable<UnrealProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        SetRows(projects, TotalCount);
    }

    private void SetRows(IEnumerable<UnrealProject> projects, int totalCount)
    {
        var rows = projects.Select(project => new ProjectRowViewModel(project)).ToArray();

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
}
