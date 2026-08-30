using System.Text.Json;
using System.Text.Json.Serialization;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Storage;

namespace UProjectHub.Core.Settings;

public sealed class JsonSettingsRepository : ISettingsRepository
{
    private static readonly JsonSerializerOptions SerializerOptions =
        CreateSerializerOptions();

    private readonly string _settingsFilePath;
    private readonly string _backupFilePath;
    private readonly AtomicJsonFileWriter _writer;

    public JsonSettingsRepository(
        string settingsFilePath,
        AtomicJsonFileWriter writer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        ArgumentNullException.ThrowIfNull(writer);

        _settingsFilePath = Path.GetFullPath(settingsFilePath);
        _backupFilePath = $"{_settingsFilePath}.bak";
        _writer = writer;
    }

    public async Task<AppSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = await TryLoadAsync(_settingsFilePath, cancellationToken)
            ?? await TryLoadAsync(_backupFilePath, cancellationToken);

        return Normalize(settings ?? new AppSettings());
    }

    public Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return _writer.WriteAsync(
            _settingsFilePath,
            Normalize(settings),
            SerializerOptions,
            cancellationToken);
    }

    private static async Task<AppSettings?> TryLoadAsync(
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

            return await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                SerializerOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
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

    private static AppSettings Normalize(AppSettings settings) =>
        settings with
        {
            ProjectSearchRoots = settings.ProjectSearchRoots ?? [],
            ManualEngineRoots = settings.ManualEngineRoots ?? [],
            ProjectUserStates = settings.ProjectUserStates?
                .Where(state => state is not null)
                .Select(state => state with
                {
                    Tags = ProjectTagNormalizer.Normalize(state.Tags),
                    Note = state.Note ?? string.Empty,
                })
                .ToArray() ?? [],
            ActiveSort = settings.ActiveSort ?? new(),
            VisibleFilters = settings.VisibleFilters ?? new(),
            ColumnLayout = settings.ColumnLayout ?? [],
        };

    private static JsonSerializerOptions CreateSerializerOptions()
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
