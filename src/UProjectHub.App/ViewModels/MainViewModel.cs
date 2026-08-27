using System.Windows.Input;
using UProjectHub.App.Infrastructure;
using UProjectHub.Core.Catalog;

namespace UProjectHub.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private int _projectCount;

    public MainViewModel(
        StatusBarViewModel statusBar,
        Action? settingsAction = null,
        ProjectListViewModel? projectList = null)
    {
        StatusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        ProjectList = projectList ?? new ProjectListViewModel();
        SettingsCommand = new RelayCommand(
            () => settingsAction!(),
            () => settingsAction is not null);
    }

    public string Title => "Unreal Projects";

    public int ProjectCount => _projectCount;

    public string ProjectCountText => $"{ProjectCount} projects";

    public StatusBarViewModel StatusBar { get; }

    public ProjectListViewModel ProjectList { get; }

    public ICommand SettingsCommand { get; }

    public void SetProjects(ProjectCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ProjectList.SetSnapshot(snapshot);
        SetProjectCount(ProjectList.TotalCount);
    }

    public void SetProjectCount(int projectCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(projectCount);

        if (SetProperty(ref _projectCount, projectCount, nameof(ProjectCount)))
        {
            OnPropertyChanged(nameof(ProjectCountText));
        }
    }
}
