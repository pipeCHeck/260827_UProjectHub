using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Cache;

public sealed record ProjectCacheEntry(
    ProjectPath ProjectFilePath,
    string Name,
    string? EngineAssociation,
    string? EngineDisplayVersion,
    ProjectType ProjectType,
    DateTimeOffset LastModified,
    ProjectState ProjectState,
    EngineResolutionState EngineState);
