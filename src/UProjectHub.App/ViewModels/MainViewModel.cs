using System.Windows.Input;
using UProjectHub.App.Infrastructure;

namespace UProjectHub.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private int _projectCount;

    public MainViewModel(StatusBarViewModel statusBar, Action? settingsAction = null)
    {
        StatusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        SettingsCommand = new RelayCommand(
            () => settingsAction!(),
            () => settingsAction is not null);
    }

    public string Title => "Unreal Projects";

    public int ProjectCount => _projectCount;

    public string ProjectCountText => $"{ProjectCount} projects";

    public StatusBarViewModel StatusBar { get; }

    public ICommand SettingsCommand { get; }

    public void SetProjectCount(int projectCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(projectCount);

        if (SetProperty(ref _projectCount, projectCount, nameof(ProjectCount)))
        {
            OnPropertyChanged(nameof(ProjectCountText));
        }
    }
}
