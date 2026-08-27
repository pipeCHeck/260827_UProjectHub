namespace UProjectHub.Core.Models;

public sealed record InstalledEngine(
    string DisplayName,
    string? Association,
    string? DisplayVersion,
    string RootPath,
    string EditorPath,
    EngineSource Source,
    bool IsUsable);
