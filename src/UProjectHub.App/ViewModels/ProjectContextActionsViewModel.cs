using System.Windows.Input;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Models;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectContextActionsViewModel : ObservableObject
{
    private readonly UnrealProject _project;
    private readonly ProjectActionService _actions;
    private readonly Action<ProjectInformationViewModel> _showInformation;
    private ProjectActionResult? _lastResult;

    public ProjectContextActionsViewModel(
        UnrealProject project,
        ProjectActionService actions,
        Action<ProjectInformationViewModel> showInformation)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _showInformation = showInformation
            ?? throw new ArgumentNullException(nameof(showInformation));

        OpenProjectCommand = new AsyncRelayCommand(
            OpenProjectAsync,
            () => CanOpenProject);
        OpenInVisualStudioCommand = new RelayCommand(
            () => LastResult = _actions.OpenInVisualStudio(_project),
            () => CanOpenInVisualStudio);
        OpenProjectFolderCommand = new RelayCommand(
            () => LastResult = _actions.OpenProjectFolder(_project));
        RevealProjectFileCommand = new RelayCommand(
            () => LastResult = _actions.RevealProjectFile(_project));
        CopyPathCommand = new RelayCommand(
            () => LastResult = _actions.CopyProjectPath(_project));
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync);
        ProjectInformationCommand = new RelayCommand(
            () => _showInformation(new ProjectInformationViewModel(_project)));
        RemoveFromListCommand = new AsyncRelayCommand(
            RemoveFromListAsync,
            () => CanRemoveFromList);
    }

    public bool CanOpenProject => _actions.CanOpenProject(_project);

    public bool CanOpenInVisualStudio => _actions.CanOpenInVisualStudio(_project);

    public bool CanRemoveFromList => _actions.CanRemoveFromList(_project);

    public string ToggleFavoriteLabel => _project.IsFavorite
        ? "Remove from Favorites"
        : "Add to Favorites";

    public ProjectActionResult? LastResult
    {
        get => _lastResult;
        private set => SetProperty(ref _lastResult, value);
    }

    public ICommand OpenProjectCommand { get; }

    public ICommand OpenInVisualStudioCommand { get; }

    public ICommand OpenProjectFolderCommand { get; }

    public ICommand RevealProjectFileCommand { get; }

    public ICommand CopyPathCommand { get; }

    public ICommand ToggleFavoriteCommand { get; }

    public ICommand ProjectInformationCommand { get; }

    public ICommand RemoveFromListCommand { get; }

    private async Task OpenProjectAsync()
    {
        LastResult = await _actions.OpenProjectAsync(_project);
    }

    private async Task ToggleFavoriteAsync()
    {
        LastResult = await _actions.ToggleFavoriteAsync(_project);
    }

    private async Task RemoveFromListAsync()
    {
        LastResult = await _actions.RemoveMissingAsync(_project);
    }
}
