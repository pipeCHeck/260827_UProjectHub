using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;
using UProjectHub.Core.Settings;
using UProjectHub.Windows.Launching;

namespace UProjectHub.App.Services;

public sealed record ProjectActionResult(bool IsSuccess, string? ErrorMessage = null)
{
    public static ProjectActionResult Succeeded() => new(true);

    public static ProjectActionResult Failed(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new ProjectActionResult(false, errorMessage);
    }
}

public sealed class ProjectActionService
{
    private readonly ProjectCatalog _catalog;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ManagedProjectRemovalService _removalService;
    private readonly IUnrealEditorLauncher _unrealEditorLauncher;
    private readonly IExplorerLauncher _explorerLauncher;
    private readonly IVisualStudioLauncher _visualStudioLauncher;
    private readonly IClipboardService _clipboardService;
    private readonly Func<UnrealProject, EngineResolution> _resolutionAccessor;
    private readonly IAppLogger _logger;

    public ProjectActionService(
        ProjectCatalog catalog,
        ISettingsRepository settingsRepository,
        ManagedProjectRemovalService removalService,
        IUnrealEditorLauncher unrealEditorLauncher,
        IExplorerLauncher explorerLauncher,
        IVisualStudioLauncher visualStudioLauncher,
        IClipboardService clipboardService,
        Func<UnrealProject, EngineResolution> resolutionAccessor,
        IAppLogger? logger = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _settingsRepository = settingsRepository
            ?? throw new ArgumentNullException(nameof(settingsRepository));
        _removalService = removalService
            ?? throw new ArgumentNullException(nameof(removalService));
        _unrealEditorLauncher = unrealEditorLauncher
            ?? throw new ArgumentNullException(nameof(unrealEditorLauncher));
        _explorerLauncher = explorerLauncher
            ?? throw new ArgumentNullException(nameof(explorerLauncher));
        _visualStudioLauncher = visualStudioLauncher
            ?? throw new ArgumentNullException(nameof(visualStudioLauncher));
        _clipboardService = clipboardService
            ?? throw new ArgumentNullException(nameof(clipboardService));
        _resolutionAccessor = resolutionAccessor
            ?? throw new ArgumentNullException(nameof(resolutionAccessor));
        _logger = logger ?? new NullAppLogger();
    }

    public event Action<ProjectCatalogSnapshot>? CatalogChanged;

    public bool CanOpenProject(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.ProjectState != ProjectState.Available)
        {
            return false;
        }

        var resolution = _resolutionAccessor(project);
        return resolution.State == EngineResolutionState.Resolved
            && resolution.ResolvedCandidate is { IsUsable: true };
    }

    public bool CanOpenInVisualStudio(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.ProjectState == ProjectState.Available
            && project.ProjectType == ProjectType.Cpp
            && _visualStudioLauncher.CanOpenSolution(project);
    }

    public VisualStudioSolutionSelection LocateVisualStudioSolution(
        UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return _visualStudioLauncher.LocateSolution(project);
    }

    public bool CanRemoveFromList(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.ProjectState == ProjectState.Missing;
    }

    public async Task<ProjectActionResult> ToggleFavoriteAsync(
        UnrealProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!_catalog.TryGet(project.ProjectFilePath, out var currentProject))
        {
            return ProjectActionResult.Failed("The project is no longer in the catalog.");
        }

        try
        {
            var settings = await _settingsRepository.LoadAsync(cancellationToken);
            var currentState = FindUserState(settings, project);
            var updatedState = new ProjectUserState(
                project.ProjectFilePath,
                IsFavorite: !(currentState?.IsFavorite ?? false),
                LastLaunched: currentState?.LastLaunched);
            var updatedSettings = ReplaceUserState(settings, updatedState);

            await _settingsRepository.SaveAsync(updatedSettings, cancellationToken);

            _catalog.Upsert(currentProject with
            {
                IsFavorite = updatedState.IsFavorite,
            });
            PublishCatalogChanged();
            return ProjectActionResult.Succeeded();
        }
        catch (Exception exception) when (IsExpectedExternalFailure(exception))
        {
            return ProjectActionResult.Failed(exception.Message);
        }
    }

    public async Task<ProjectActionResult> OpenProjectAsync(
        UnrealProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!_catalog.TryGet(project.ProjectFilePath, out var currentProject)
            || currentProject.ProjectState != ProjectState.Available)
        {
            return ProjectActionResult.Failed("The project is not available to open.");
        }

        var resolution = _resolutionAccessor(currentProject);
        if (resolution.State != EngineResolutionState.Resolved
            || resolution.ResolvedCandidate is not { IsUsable: true })
        {
            return ProjectActionResult.Failed(
                "The project's Unreal Engine is not uniquely resolved.");
        }

        var launchResult = _unrealEditorLauncher.Launch(currentProject, resolution);
        if (!launchResult.IsSuccess)
        {
            _logger.Warning(
                $"Project launch failed for {currentProject.ProjectFilePath.Value}: "
                + (launchResult.ErrorMessage ?? "Unknown launch failure."));
            return FromLaunchResult(launchResult);
        }

        if (launchResult.LaunchedAtUtc is not { } launchedAtUtc)
        {
            _logger.Warning(
                $"Project launch returned no timestamp for {currentProject.ProjectFilePath.Value}.");
            return ProjectActionResult.Failed(
                "The editor started without a launch timestamp.");
        }

        try
        {
            var settings = await _settingsRepository.LoadAsync(cancellationToken);
            var currentState = FindUserState(settings, currentProject);
            var updatedState = new ProjectUserState(
                currentProject.ProjectFilePath,
                IsFavorite: currentState?.IsFavorite ?? false,
                LastLaunched: launchedAtUtc);
            var updatedSettings = ReplaceUserState(settings, updatedState);

            await _settingsRepository.SaveAsync(updatedSettings, cancellationToken);

            _catalog.Upsert(currentProject with
            {
                IsFavorite = updatedState.IsFavorite,
                LastLaunched = launchedAtUtc,
            });
            PublishCatalogChanged();
            return ProjectActionResult.Succeeded();
        }
        catch (Exception exception) when (IsExpectedExternalFailure(exception))
        {
            _logger.Error(
                $"Launch history could not be saved for {currentProject.ProjectFilePath.Value}.",
                exception);
            return ProjectActionResult.Failed(
                $"The editor started, but launch history could not be saved: {exception.Message}");
        }
    }

    public ProjectActionResult OpenProjectFolder(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return FromLaunchResult(_explorerLauncher.OpenProjectFolder(project));
    }

    public ProjectActionResult CopyProjectPath(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        try
        {
            _clipboardService.SetText(project.ProjectFilePath.Value);
            return ProjectActionResult.Succeeded();
        }
        catch (Exception exception) when (IsExpectedExternalFailure(exception))
        {
            return ProjectActionResult.Failed(exception.Message);
        }
    }

    public ProjectActionResult OpenInVisualStudio(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.ProjectState != ProjectState.Available
            || project.ProjectType != ProjectType.Cpp)
        {
            return ProjectActionResult.Failed(
                "Open in Visual Studio is unavailable for this project.");
        }

        return FromLaunchResult(_visualStudioLauncher.OpenSolution(project));
    }

    public async Task<ProjectActionResult> RemoveMissingAsync(
        UnrealProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!CanRemoveFromList(project))
        {
            return ProjectActionResult.Failed(
                "Only missing projects can be removed from the list.");
        }

        try
        {
            var removalResult = await _removalService.RemoveMissingAsync(
                project.ProjectFilePath,
                cancellationToken);
            if (removalResult != ManagedProjectRemovalResult.Removed)
            {
                return ProjectActionResult.Failed(
                    "The project could not be removed from the list.");
            }

            PublishCatalogChanged();
            return ProjectActionResult.Succeeded();
        }
        catch (Exception exception) when (IsExpectedExternalFailure(exception))
        {
            return ProjectActionResult.Failed(exception.Message);
        }
    }

    private void PublishCatalogChanged()
    {
        CatalogChanged?.Invoke(_catalog.GetSnapshot());
    }

    private static ProjectUserState? FindUserState(
        AppSettings settings,
        UnrealProject project) =>
        settings.ProjectUserStates.FirstOrDefault(state =>
            state.ProjectPath.Equals(project.ProjectFilePath));

    private static AppSettings ReplaceUserState(
        AppSettings settings,
        ProjectUserState updatedState)
    {
        var states = settings.ProjectUserStates.ToList();
        var index = states.FindIndex(state =>
            state.ProjectPath.Equals(updatedState.ProjectPath));
        if (index >= 0)
        {
            states[index] = updatedState;
        }
        else
        {
            states.Add(updatedState);
        }

        return settings with { ProjectUserStates = states };
    }

    private static ProjectActionResult FromLaunchResult(LaunchResult result) =>
        result.IsSuccess
            ? ProjectActionResult.Succeeded()
            : ProjectActionResult.Failed(
                result.ErrorMessage ?? "The action could not be completed.");

    private static bool IsExpectedExternalFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ExternalException;
}
