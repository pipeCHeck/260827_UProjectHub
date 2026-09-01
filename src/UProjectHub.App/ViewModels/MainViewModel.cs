using System.Windows.Input;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Models;
using UProjectHub.Core.Settings;

namespace UProjectHub.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly LocalizationService? _localization;
    private int _projectCount;

    public MainViewModel(
        StatusBarViewModel statusBar,
        Action? settingsAction = null,
        ProjectListViewModel? projectList = null,
        SearchFilterViewModel? searchFilter = null,
        ProjectActionService? projectActions = null,
        Func<Task>? refreshAction = null,
        NewProjectViewModel? newProject = null,
        LocalizationService? localization = null)
    {
        _localization = localization;
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }
        StatusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        ProjectList = projectList ?? new ProjectListViewModel();
        SearchFilter = searchFilter;
        NewProject = newProject;
        if (projectActions is not null)
        {
            projectActions.CatalogChanged += SetProjects;
        }
        SettingsCommand = new RelayCommand(
            () => settingsAction!(),
            () => settingsAction is not null);
        RefreshCommand = new AsyncRelayCommand(
            () => ExecuteRefreshAsync(refreshAction!),
            () => refreshAction is not null);
    }

    public string Title => Localize("String.AppTitle", "UProject Hub");

    public int ProjectCount => _projectCount;

    public string ProjectCountText => string.Format(
        Localize("String.ProjectCountFormat", "{0} projects"),
        ProjectCount);

    public StatusBarViewModel StatusBar { get; }

    public ProjectListViewModel ProjectList { get; }

    public SearchFilterViewModel? SearchFilter { get; }

    public NewProjectViewModel? NewProject { get; }

    public ICommand SettingsCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public void SetProjects(ProjectCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (SearchFilter is null)
        {
            ProjectList.SetSnapshot(snapshot);
        }
        else
        {
            SearchFilter.SetSnapshot(snapshot);
        }

        SetProjectCount(ProjectList.TotalCount);
    }

    public void SetEngines(IEnumerable<InstalledEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        NewProject?.SetEngines(engines);
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SearchFilter?.ApplySettings(settings);
        ProjectList.SetColumnLayout(settings.ColumnLayout);
    }

    public void SetProjectCount(int projectCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(projectCount);

        if (SetProperty(ref _projectCount, projectCount, nameof(ProjectCount)))
        {
            OnPropertyChanged(nameof(ProjectCountText));
        }
    }

    private string Localize(string key, string fallback) =>
        _localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;

    private static async Task ExecuteRefreshAsync(Func<Task> refreshAction)
    {
        try
        {
            await refreshAction();
        }
        catch (OperationCanceledException)
        {
            // Application shutdown cancels an active Refresh/F5 operation.
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ProjectCountText));
    }
}
