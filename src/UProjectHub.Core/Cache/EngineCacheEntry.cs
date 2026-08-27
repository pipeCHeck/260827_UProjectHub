using UProjectHub.Core.Models;

namespace UProjectHub.Core.Cache;

public sealed record EngineCacheEntry(
    string DisplayName,
    string? Association,
    string? DisplayVersion,
    string RootPath,
    string EditorPath,
    EngineSource Source,
    bool IsUsable);
