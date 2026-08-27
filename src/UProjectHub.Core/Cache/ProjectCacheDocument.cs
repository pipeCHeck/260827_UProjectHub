namespace UProjectHub.Core.Cache;

public sealed record ProjectCacheDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<ProjectCacheEntry> Projects { get; init; } = [];
}
