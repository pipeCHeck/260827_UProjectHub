using System.Text.Json;

namespace UProjectHub.Windows.Engines.Launcher;

public sealed class LauncherInstalledManifestParser
{
    public LauncherInstalledManifestParseResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return LauncherInstalledManifestParseResult.Failure(
                "Launcher manifest JSON is empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return LauncherInstalledManifestParseResult.Failure(
                    "Launcher manifest JSON root must be an object.");
            }

            var entries = new List<LauncherInstalledManifestEntry>();
            if (document.RootElement.TryGetProperty(
                    "InstallationList",
                    out var installationList)
                && installationList.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in installationList.EnumerateArray())
                {
                    entries.Add(ParseEntry(entry));
                }
            }

            return LauncherInstalledManifestParseResult.Success(
                new LauncherInstalledManifest(
                    Array.AsReadOnly(entries.ToArray())));
        }
        catch (Exception exception) when (exception is JsonException
            or NotSupportedException)
        {
            return LauncherInstalledManifestParseResult.Failure(
                exception.Message);
        }
    }

    private static LauncherInstalledManifestEntry ParseEntry(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return new LauncherInstalledManifestEntry(null, null, null);
        }

        return new LauncherInstalledManifestEntry(
            GetString(entry, "AppName"),
            GetString(entry, "InstallLocation"),
            GetString(entry, "AppVersion"));
    }

    private static string? GetString(JsonElement entry, string propertyName)
    {
        return entry.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
