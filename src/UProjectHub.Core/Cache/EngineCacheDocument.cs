namespace UProjectHub.Core.Cache;

public sealed record EngineCacheDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<EngineCacheEntry> Engines { get; init; } = [];
}
