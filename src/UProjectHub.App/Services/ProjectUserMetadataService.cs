using System.IO;
using System.Security;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Settings;

namespace UProjectHub.App.Services;

public sealed record ProjectUserMetadataResult(
    bool IsSuccess,
    UnrealProject? Project = null,
    string? ErrorMessage = null);

public sealed class ProjectUserMetadataService
{
    private readonly ProjectCatalog _catalog;
    private readonly SettingsMutationService _settings;

    public ProjectUserMetadataService(
        ProjectCatalog catalog,
        SettingsMutationService settings)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public event Action<ProjectCatalogSnapshot>? CatalogChanged;

    public Task<ProjectUserMetadataResult> AddTagAsync(
        ProjectPath projectPath,
        string? tag,
        CancellationToken cancellationToken = default)
    {
        var normalized = tag?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return Task.FromResult(Failed("A tag cannot be empty."));
        }

        return UpdateAsync(
            projectPath,
            state =>
            {
                if (state.Tags.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    return null;
                }

                return state with
                {
                    Tags = [.. state.Tags, normalized],
                };
            },
            project => project with
            {
                Tags = [.. project.Tags, normalized],
            },
            "That tag already exists.",
            cancellationToken);
    }

    public Task<ProjectUserMetadataResult> RemoveTagAsync(
        ProjectPath projectPath,
        string? tag,
        CancellationToken cancellationToken = default)
    {
        var normalized = tag?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return Task.FromResult(Failed("A tag is required."));
        }

        return UpdateAsync(
            projectPath,
            state =>
            {
                var remaining = state.Tags
                    .Where(value => !string.Equals(
                        value,
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return remaining.Length == state.Tags.Count
                    ? null
                    : state with { Tags = remaining };
            },
            project => project with
            {
                Tags = project.Tags
                    .Where(value => !string.Equals(
                        value,
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
            },
            "The tag no longer exists.",
            cancellationToken);
    }

    public Task<ProjectUserMetadataResult> SaveNoteAsync(
        ProjectPath projectPath,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var normalized = note ?? string.Empty;
        return UpdateAsync(
            projectPath,
            state => string.Equals(state.Note, normalized, StringComparison.Ordinal)
                ? state
                : state with { Note = normalized },
            project => project with { Note = normalized },
            unavailableMessage: null,
            cancellationToken);
    }

    private async Task<ProjectUserMetadataResult> UpdateAsync(
        ProjectPath projectPath,
        Func<ProjectUserState, ProjectUserState?> updateState,
        Func<UnrealProject, UnrealProject> updateProject,
        string? unavailableMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        if (!_catalog.TryGet(projectPath, out var project))
        {
            return Failed("The project is no longer in the catalog.");
        }

        try
        {
            var hasMutation = false;
            await _settings.UpdateAsync(settings =>
            {
                var currentState = settings.ProjectUserStates.FirstOrDefault(state =>
                        state.ProjectPath.Equals(projectPath))
                    ?? new ProjectUserState(
                        projectPath,
                        project.IsFavorite,
                        project.LastLaunched)
                    {
                        Tags = ProjectTagNormalizer.Normalize(project.Tags),
                        Note = project.Note,
                    };
                var updatedState = updateState(currentState);
                if (updatedState is null)
                {
                    return settings;
                }

                if (ReferenceEquals(currentState, updatedState))
                {
                    return settings;
                }

                hasMutation = true;
                return ReplaceUserState(settings, updatedState with
                {
                    Tags = ProjectTagNormalizer.Normalize(updatedState.Tags),
                    Note = updatedState.Note ?? string.Empty,
                });
            }, cancellationToken);

            if (!hasMutation && unavailableMessage is not null)
            {
                return Failed(unavailableMessage);
            }

            if (!_catalog.TryUpdate(projectPath, updateProject, out var updatedProject))
            {
                return Failed("The project is no longer in the catalog.");
            }

            CatalogChanged?.Invoke(_catalog.GetSnapshot());
            return new ProjectUserMetadataResult(true, updatedProject);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException)
        {
            return Failed(exception.Message);
        }
    }

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

    private static ProjectUserMetadataResult Failed(string message) =>
        new(false, ErrorMessage: message);
}
