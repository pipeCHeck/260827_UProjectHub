using System.Security;
using UProjectHub.Core.Models;
using UProjectHub.Windows.Registry;

namespace UProjectHub.Windows.Engines.SourceBuild;

public sealed class SourceBuildEngineProvider : IEngineProvider
{
    private const string RegisteredBuildsSubKey =
        @"SOFTWARE\Epic Games\Unreal Engine\Builds";

    private readonly IRegistryReader _registryReader;

    public SourceBuildEngineProvider(IRegistryReader registryReader)
    {
        ArgumentNullException.ThrowIfNull(registryReader);
        _registryReader = registryReader;
    }

    public Task<EngineProviderResult> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<RegistryValueEntry> entries;
        try
        {
            entries = _registryReader.ReadCurrentUserValues(
                RegisteredBuildsSubKey,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedRegistryReadFailure(exception))
        {
            return Task.FromResult(new EngineProviderResult(
                [],
                [new EngineProviderIssue(
                    RegisteredBuildsSubKey,
                    exception.Message)]));
        }

        var engines = new List<InstalledEngine>();
        var issues = new List<EngineProviderIssue>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddEntry(entry, engines, issues);
        }

        return Task.FromResult(new EngineProviderResult(engines, issues));
    }

    private static void AddEntry(
        RegistryValueEntry entry,
        ICollection<InstalledEngine> engines,
        ICollection<EngineProviderIssue> issues)
    {
        if (!Guid.TryParse(entry.Name?.Trim(), out var identifier))
        {
            issues.Add(new EngineProviderIssue(
                entry.Name ?? RegisteredBuildsSubKey,
                "Registered Unreal Engine build name is not a GUID."));
            return;
        }

        if (!TryNormalizeRootPath(entry.Value, out var rootPath, out var pathError))
        {
            issues.Add(new EngineProviderIssue(entry.Name, pathError));
            return;
        }

        var association = identifier.ToString("B");
        var editorPath = Path.Combine(
            rootPath,
            "Engine",
            "Binaries",
            "Win64",
            "UnrealEditor.exe");
        var isUsable = File.Exists(editorPath);

        engines.Add(new InstalledEngine(
            DisplayName: $"Unreal Engine Source Build {association}",
            Association: association,
            DisplayVersion: null,
            RootPath: rootPath,
            EditorPath: editorPath,
            Source: EngineSource.SourceBuild,
            IsUsable: isUsable));

        if (!isUsable)
        {
            issues.Add(new EngineProviderIssue(
                editorPath,
                "Expected Unreal Editor executable was not found."));
        }
    }

    private static bool TryNormalizeRootPath(
        object? value,
        out string rootPath,
        out string error)
    {
        rootPath = string.Empty;
        error = string.Empty;

        if (value is not string rawPath || string.IsNullOrWhiteSpace(rawPath))
        {
            error = "Registered Unreal Engine build path must be a non-empty string.";
            return false;
        }

        var trimmedPath = rawPath.Trim();
        if (!Path.IsPathFullyQualified(trimmedPath))
        {
            error = "Registered Unreal Engine build path is not absolute.";
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

    private static bool IsExpectedRegistryReadFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException;
}
