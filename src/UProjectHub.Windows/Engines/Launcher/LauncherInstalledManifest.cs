namespace UProjectHub.Windows.Engines.Launcher;

public sealed record LauncherInstalledManifest(
    IReadOnlyList<LauncherInstalledManifestEntry> InstallationList);

public sealed record LauncherInstalledManifestEntry(
    string? AppName,
    string? InstallLocation,
    string? AppVersion);

public sealed class LauncherInstalledManifestParseResult
{
    private LauncherInstalledManifestParseResult(
        LauncherInstalledManifest? manifest,
        string? error)
    {
        Manifest = manifest;
        Error = error;
    }

    public bool IsSuccess => Manifest is not null;

    public LauncherInstalledManifest? Manifest { get; }

    public string? Error { get; }

    public static LauncherInstalledManifestParseResult Success(
        LauncherInstalledManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new LauncherInstalledManifestParseResult(manifest, null);
    }

    public static LauncherInstalledManifestParseResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new LauncherInstalledManifestParseResult(null, error);
    }
}
