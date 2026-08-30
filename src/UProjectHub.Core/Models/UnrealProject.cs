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
    public IReadOnlyList<string> Tags { get; init; } = [];

    public string Note { get; init; } = string.Empty;

    public string ProjectDirectory =>
        Path.GetDirectoryName(ProjectFilePath.Value) ?? string.Empty;
}
