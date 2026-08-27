using System.Globalization;
using System.Security;
using UProjectHub.Core.Models;
using UProjectHub.Core.Versions;

namespace UProjectHub.Windows.Engines.Launcher;

public sealed class LauncherEngineProvider : IEngineProvider
{
    private const string LauncherAppNamePrefix = "UE_";

    private readonly string _manifestFilePath;
    private readonly LauncherInstalledManifestParser _manifestParser;

    public LauncherEngineProvider()
        : this(
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "Epic",
                "UnrealEngineLauncher",
                "LauncherInstalled.dat"),
            new LauncherInstalledManifestParser())
    {
    }

    public LauncherEngineProvider(
        string manifestFilePath,
        LauncherInstalledManifestParser manifestParser)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestFilePath);
        ArgumentNullException.ThrowIfNull(manifestParser);

        _manifestFilePath = Path.GetFullPath(manifestFilePath);
        _manifestParser = manifestParser;
    }

    public async Task<EngineProviderResult> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_manifestFilePath))
        {
            return EngineProviderResult.Empty();
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(
                _manifestFilePath,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            return new EngineProviderResult(
                [],
                [new EngineProviderIssue(_manifestFilePath, exception.Message)]);
        }

        var parseResult = _manifestParser.Parse(json);
        if (!parseResult.IsSuccess)
        {
            return new EngineProviderResult(
                [],
                [new EngineProviderIssue(
                    _manifestFilePath,
                    parseResult.Error ?? "Launcher manifest could not be parsed.")]);
        }

        var engines = new List<InstalledEngine>();
        var issues = new List<EngineProviderIssue>();
        foreach (var entry in parseResult.Manifest!.InstallationList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddEntry(entry, engines, issues);
        }

        return new EngineProviderResult(engines, issues);
    }

    private void AddEntry(
        LauncherInstalledManifestEntry entry,
        ICollection<InstalledEngine> engines,
        ICollection<EngineProviderIssue> issues)
    {
        var appName = entry.AppName?.Trim();
        if (string.IsNullOrEmpty(appName))
        {
            issues.Add(new EngineProviderIssue(
                _manifestFilePath,
                "Launcher installation entry has no AppName."));
            return;
        }

        if (!appName.StartsWith(
            LauncherAppNamePrefix,
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var rawVersion = appName[LauncherAppNamePrefix.Length..];
        if (!EngineVersion.TryParse(rawVersion, out var engineVersion))
        {
            issues.Add(new EngineProviderIssue(
                appName,
                "Launcher Unreal Engine AppName has an invalid numeric version."));
            return;
        }

        if (!TryNormalizeRootPath(
            entry.InstallLocation,
            out var rootPath,
            out var pathError))
        {
            issues.Add(new EngineProviderIssue(appName, pathError));
            return;
        }

        var version = FormatVersion(engineVersion);
        var editorPath = Path.Combine(
            rootPath,
            "Engine",
            "Binaries",
            "Win64",
            "UnrealEditor.exe");
        var isUsable = File.Exists(editorPath);

        engines.Add(new InstalledEngine(
            DisplayName: $"Unreal Engine {version}",
            Association: version,
            DisplayVersion: version,
            RootPath: rootPath,
            EditorPath: editorPath,
            Source: EngineSource.Launcher,
            IsUsable: isUsable));

        if (!isUsable)
        {
            issues.Add(new EngineProviderIssue(
                editorPath,
                "Expected Unreal Editor executable was not found."));
        }
    }

    private static bool TryNormalizeRootPath(
        string? rawPath,
        out string rootPath,
        out string error)
    {
        rootPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            error = "Launcher Unreal Engine entry has no install location.";
            return false;
        }

        var trimmedPath = rawPath.Trim();
        if (!Path.IsPathFullyQualified(trimmedPath))
        {
            error = "Launcher Unreal Engine install location is not absolute.";
            return false;
        }

        try
        {
            rootPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(trimmedPath));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or SecurityException
            or IOException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string FormatVersion(EngineVersion version)
    {
        var majorMinor = string.Create(
            CultureInfo.InvariantCulture,
            $"{version.Major}.{version.Minor}");
        return version.Patch is int patch
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{majorMinor}.{patch}")
            : majorMinor;
    }

    private static bool IsExpectedReadFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException;
}
