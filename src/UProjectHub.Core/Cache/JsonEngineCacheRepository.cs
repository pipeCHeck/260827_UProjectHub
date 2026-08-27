using System.Diagnostics.CodeAnalysis;
using UProjectHub.Core.Storage;

namespace UProjectHub.Core.Cache;

public sealed class JsonEngineCacheRepository : IEngineCacheRepository
{
    private static readonly string[] RequiredEntryProperties =
    [
        "displayName",
        "rootPath",
        "editorPath",
        "source",
        "isUsable",
    ];

    private readonly string _cacheFilePath;
    private readonly AtomicJsonFileWriter _writer;

    public JsonEngineCacheRepository(
        string cacheFilePath,
        AtomicJsonFileWriter writer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheFilePath);
        ArgumentNullException.ThrowIfNull(writer);

        _cacheFilePath = Path.GetFullPath(cacheFilePath);
        _writer = writer;
    }

    public async Task<EngineCacheDocument> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var jsonDocument = await CacheJsonSerialization.TryLoadAsync(
            _cacheFilePath,
            cancellationToken);
        if (!CacheJsonSerialization.TryGetEntries(
            jsonDocument,
            EngineCacheDocument.CurrentSchemaVersion,
            "engines",
            out var entries))
        {
            return new EngineCacheDocument();
        }

        var engines = new List<EngineCacheEntry>();
        foreach (var element in entries.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CacheJsonSerialization.HasProperties(
                    element,
                    RequiredEntryProperties)
                || !CacheJsonSerialization.TryDeserialize(
                    element,
                    out EngineCacheEntry? entry)
                || !IsValid(entry))
            {
                continue;
            }

            engines.Add(entry);
        }

        return new EngineCacheDocument { Engines = engines };
    }

    public Task SaveAsync(
        EngineCacheDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var currentDocument = new EngineCacheDocument
        {
            Engines = document.Engines ?? [],
        };

        return _writer.WriteAsync(
            _cacheFilePath,
            currentDocument,
            CacheJsonSerialization.Options,
            preserveBackup: false,
            cancellationToken);
    }

    private static bool IsValid(
        [NotNullWhen(true)] EngineCacheEntry? entry) =>
        entry is not null
        && !string.IsNullOrWhiteSpace(entry.DisplayName)
        && !string.IsNullOrWhiteSpace(entry.RootPath)
        && !string.IsNullOrWhiteSpace(entry.EditorPath)
        && Enum.IsDefined(entry.Source);
}
