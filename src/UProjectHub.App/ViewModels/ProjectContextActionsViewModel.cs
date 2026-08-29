using System.Windows.Input;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Models;
using UProjectHub.Windows.Launching;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectContextActionsViewModel : ObservableObject
{
    private readonly UnrealProject _project;
    private readonly ProjectActionService _actions;
    private readonly Action<ProjectInformationViewModel> _showInformation;
    private readonly Action<GenerateProjectFilesViewModel> _showGenerateProjectFiles;
    private readonly LocalizationService? _localization;
    private readonly RelayCommand _openInVisualStudioCommand;
    private readonly RelayCommand _generateProjectFilesCommand;
    private ProjectActionResult? _lastResult;

    public ProjectContextActionsViewModel(
        UnrealProject project,
        ProjectActionService actions,
        Action<ProjectInformationViewModel> showInformation,
        Action<GenerateProjectFilesViewModel> showGenerateProjectFiles,
        LocalizationService? localization = null)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _showInformation = showInformation
            ?? throw new ArgumentNullException(nameof(showInformation));
        _showGenerateProjectFiles = showGenerateProjectFiles
            ?? throw new ArgumentNullException(nameof(showGenerateProjectFiles));
        _localization = localization;

        OpenProjectCommand = new AsyncRelayCommand(
            OpenProjectAsync,
            () => CanOpenProject);
        _openInVisualStudioCommand = new RelayCommand(
            () => LastResult = _actions.OpenInVisualStudio(_project),
            () => CanOpenInVisualStudio);
        _generateProjectFilesCommand = new RelayCommand(
            ShowGenerateProjectFiles,
            () => CanGenerateProjectFiles);
        OpenProjectFolderCommand = new RelayCommand(
            () => LastResult = _actions.OpenProjectFolder(_project));
        CopyPathCommand = new RelayCommand(
            () => LastResult = _actions.CopyProjectPath(_project));
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync);
        ProjectInformationCommand = new RelayCommand(
            () => _showInformation(new ProjectInformationViewModel(
                _project,
                localization: _localization)));
        RemoveFromListCommand = new AsyncRelayCommand(
            RemoveFromListAsync,
            () => CanRemoveFromList);
    }

    public bool CanOpenProject => _actions.CanOpenProject(_project);

    public bool CanOpenInVisualStudio => _actions.CanOpenInVisualStudio(_project);

    public string? OpenInVisualStudioUnavailableReason =>
        CanOpenInVisualStudio ? null : GetOpenInVisualStudioUnavailableReason();

    public bool CanGenerateProjectFiles =>
        _actions.PrepareProjectFileGeneration(_project).CanGenerate;

    public string? GenerateProjectFilesUnavailableReason =>
        CanGenerateProjectFiles ? null : GetGenerateProjectFilesUnavailableReason();

    public bool CanRemoveFromList => _actions.CanRemoveFromList(_project);

    public string ToggleFavoriteLabel => _project.IsFavorite
        ? Localize("String.RemoveFavorite", "Remove from Favorites")
        : Localize("String.AddFavorite", "Add to Favorites");

    public ProjectActionResult? LastResult
    {
        get => _lastResult;
        private set => SetProperty(ref _lastResult, value);
    }

    public ICommand OpenProjectCommand { get; }

    public ICommand OpenInVisualStudioCommand => _openInVisualStudioCommand;

    public ICommand GenerateProjectFilesCommand => _generateProjectFilesCommand;

    public ICommand OpenProjectFolderCommand { get; }

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

    private void ShowGenerateProjectFiles()
    {
        var preparation = _actions.PrepareProjectFileGeneration(_project);
        if (preparation.Request is not { } request)
        {
            return;
        }

        _showGenerateProjectFiles(new GenerateProjectFilesViewModel(
            request,
            cancellationToken => _actions.GenerateProjectFilesAsync(
                request,
                cancellationToken),
            RefreshSolutionActions,
            _localization));
    }

    private void RefreshSolutionActions()
    {
        _openInVisualStudioCommand.RaiseCanExecuteChanged();
        _generateProjectFilesCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenInVisualStudio));
        OnPropertyChanged(nameof(OpenInVisualStudioUnavailableReason));
        OnPropertyChanged(nameof(CanGenerateProjectFiles));
        OnPropertyChanged(nameof(GenerateProjectFilesUnavailableReason));
    }

    private string GetOpenInVisualStudioUnavailableReason()
    {
        if (_project.ProjectState != ProjectState.Available)
        {
            return Localize(
                "String.OpenVisualStudioProjectUnavailable",
                "The project is not available.");
        }

        if (_project.ProjectType != ProjectType.Cpp)
        {
            return Localize(
                "String.OpenVisualStudioCppOnly",
                "Existing .sln files can be opened only for C++ projects.");
        }

        var selection = _actions.LocateVisualStudioSolution(_project);
        return selection.State switch
        {
            VisualStudioSolutionState.Missing => Localize(
                "String.OpenVisualStudioSolutionMissing",
                "No existing .sln file was found. Generate Visual Studio project files to create one."),
            VisualStudioSolutionState.Multiple => Localize(
                "String.OpenVisualStudioSolutionMultiple",
                "Multiple .sln files were found, so no unique solution could be selected."),
            VisualStudioSolutionState.Inaccessible => Localize(
                "String.OpenVisualStudioSolutionInaccessible",
                "The project folder could not be inspected for .sln files."),
            _ => Localize(
                "String.OpenVisualStudioSolutionUnavailable",
                "The existing .sln file is unavailable."),
        };
    }

    private string GetGenerateProjectFilesUnavailableReason()
    {
        if (_project.ProjectState != ProjectState.Available)
        {
            return Localize(
                "String.GenerateProjectFilesProjectUnavailable",
                "The project is not available.");
        }

        if (_project.ProjectType != ProjectType.Cpp)
        {
            return Localize(
                "String.GenerateProjectFilesCppOnly",
                "Visual Studio project files can be generated only for C++ projects.");
        }

        if (_project.EngineState != EngineResolutionState.Resolved)
        {
            return Localize(
                "String.GenerateProjectFilesEngineUnavailable",
                "The project's Unreal Engine is not uniquely resolved and usable.");
        }

        var preparation = _actions.PrepareProjectFileGeneration(_project);
        return Localize(
            "String.GenerateProjectFilesToolUnavailable",
            preparation.UnavailableReason
            ?? "The resolved Unreal Engine cannot generate project files.");
    }

    private string Localize(string key, string fallback) =>
        _localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;
}
