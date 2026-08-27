using UProjectHub.App.Converters;
using UProjectHub.Core.Models;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectRowViewModel
{
    public ProjectRowViewModel(
        UnrealProject project,
        ProjectContextActionsViewModel? contextActions = null)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        ContextActions = contextActions;
    }

    public UnrealProject Project { get; }

    public ProjectContextActionsViewModel? ContextActions { get; }

    public bool IsFavorite => Project.IsFavorite;

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    public string Name => Project.Name;

    public string ProjectPath => Project.ProjectFilePath.Value;

    public string ProjectDirectory => Project.ProjectDirectory;

    public string EngineDisplay => FirstNonEmpty(
        Project.EngineDisplayVersion,
        Project.EngineAssociation) ?? "—";

    public string TypeDisplay => Project.ProjectState == ProjectState.Broken
        ? "—"
        : Project.ProjectType switch
        {
            ProjectType.Cpp => "C++",
            ProjectType.Blueprint => "Blueprint",
            _ => "—",
        };

    public DateTimeOffset LastModified => Project.LastModified;

    public DateTimeOffset? LastLaunched => Project.LastLaunched;

    public ProjectState ProjectState => Project.ProjectState;

    public EngineResolutionState EngineState => Project.EngineState;

    public string StateMessage => ProjectStateMessageConverter.GetMessage(ProjectState);

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
