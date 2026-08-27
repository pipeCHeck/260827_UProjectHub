using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Models;

public sealed record UnrealProject(
    string Name,
    ProjectPath ProjectFilePath,
    string? EngineAssociation,
    string? EngineDisplayVersion,
    ProjectType ProjectType,
    DateTimeOffset LastModified,
    DateTimeOffset? LastLaunched,
    bool IsFavorite,
    ProjectState ProjectState,
    EngineResolutionState EngineState)
{
    public string ProjectDirectory =>
        Path.GetDirectoryName(ProjectFilePath.Value) ?? string.Empty;
}
