using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Storage;

namespace UProjectHub.Core.Cache;

public sealed class JsonProjectCacheRepository : IProjectCacheRepository
{
    private static readonly string[] RequiredEntryProperties =
    [
        "projectFilePath",
        "name",
        "projectType",
        "lastModified",
        "projectState",
        "engineState",
    ];

    private readonly string _cacheFilePath;
    private readonly AtomicJsonFileWriter _writer;

    public JsonProjectCacheRepository(
        string cacheFilePath,
        AtomicJsonFileWriter writer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheFilePath);
        ArgumentNullException.ThrowIfNull(writer);

        _cacheFilePath = Path.GetFullPath(cacheFilePath);
        _writer = writer;
    }

    public async Task<ProjectCacheDocument> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var jsonDocument = await CacheJsonSerialization.TryLoadAsync(
            _cacheFilePath,
            cancellationToken);
        if (!CacheJsonSerialization.TryGetEntries(
            jsonDocument,
            ProjectCacheDocument.CurrentSchemaVersion,
            "projects",
            out var entries))
        {
            return new ProjectCacheDocument();
        }

        var projects = new List<ProjectCacheEntry>();
        foreach (var element in entries.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CacheJsonSerialization.HasProperties(
                    element,
                    RequiredEntryProperties)
                || !CacheJsonSerialization.TryDeserialize(
                    element,
                    out ProjectCacheEntry? entry)
                || !IsValid(entry))
            {
                continue;
            }

            projects.Add(entry);
        }

        return new ProjectCacheDocument { Projects = projects };
    }

    public Task SaveAsync(
        ProjectCacheDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var currentDocument = new ProjectCacheDocument
        {
            Projects = document.Projects ?? [],
        };

        return _writer.WriteAsync(
            _cacheFilePath,
            currentDocument,
            CacheJsonSerialization.Options,
            preserveBackup: false,
            cancellationToken);
    }

    private static bool IsValid(
        [NotNullWhen(true)] ProjectCacheEntry? entry) =>
        entry is not null
        && entry.ProjectFilePath is not null
        && !string.IsNullOrWhiteSpace(entry.Name)
        && entry.LastModified != default
        && Enum.IsDefined(entry.ProjectType)
        && Enum.IsDefined(entry.ProjectState)
        && Enum.IsDefined(entry.EngineState);
}

internal static class CacheJsonSerialization
{
    internal static JsonSerializerOptions Options { get; } = CreateOptions();

    internal static async Task<JsonDocument?> TryLoadAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static bool TryGetEntries(
        JsonDocument? document,
        int currentSchemaVersion,
        string propertyName,
        out JsonElement entries)
    {
        entries = default;
        if (document is null
            || document.RootElement.ValueKind != JsonValueKind.Object
            || !TryGetProperty(
                document.RootElement,
                "schemaVersion",
                out var schemaVersion)
            || schemaVersion.ValueKind != JsonValueKind.Number
            || !schemaVersion.TryGetInt32(out var version)
            || version != currentSchemaVersion
            || !TryGetProperty(
                document.RootElement,
                propertyName,
                out entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            entries = default;
            return false;
        }

        return true;
    }

    internal static bool HasProperties(
        JsonElement element,
        IEnumerable<string> propertyNames) =>
        element.ValueKind == JsonValueKind.Object
        && propertyNames.All(name => TryGetProperty(element, name, out _));

    internal static bool TryDeserialize<T>(
        JsonElement element,
        out T? value)
        where T : class
    {
        try
        {
            value = element.Deserialize<T>(Options);
            return value is not null;
        }
        catch (JsonException)
        {
            value = null;
            return false;
        }
        catch (NotSupportedException)
        {
            value = null;
            return false;
        }
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(
                property.Name,
                propertyName,
                StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        options.Converters.Add(new ProjectPathJsonConverter());
        return options;
    }

    private sealed class ProjectPathJsonConverter : JsonConverter<ProjectPath>
    {
        public override ProjectPath Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Project path must be a JSON string.");
            }

            var path = reader.GetString();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new JsonException("Project path must not be empty.");
            }

            try
            {
                return new ProjectPath(path);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                or IOException
                or NotSupportedException)
            {
                throw new JsonException("Project path is invalid.", exception);
            }
        }

        public override void Write(
            Utf8JsonWriter writer,
            ProjectPath value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }
}
