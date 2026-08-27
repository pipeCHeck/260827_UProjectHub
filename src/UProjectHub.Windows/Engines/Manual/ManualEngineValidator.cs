using System.Globalization;
using System.Security;
using System.Text.Json;
using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Engines.Manual;

public sealed class ManualEngineValidator
{
    public async Task<EngineProviderResult> ValidateAsync(
        string? manualRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryNormalizeRootPath(
            manualRoot,
            out var rootPath,
            out var pathError))
        {
            return InvalidRoot(manualRoot, pathError);
        }

        var editorPath = Path.Combine(
            rootPath,
            "Engine",
            "Binaries",
            "Win64",
            "UnrealEditor.exe");
        var buildVersionPath = Path.Combine(
            rootPath,
            "Engine",
            "Build",
            "Build.version");

        if (!File.Exists(buildVersionPath))
        {
            return DiagnosticCandidate(
                rootPath,
                editorPath,
                buildVersionPath,
                "Build.version was not found.");
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(
                buildVersionPath,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            return DiagnosticCandidate(
                rootPath,
                editorPath,
                buildVersionPath,
                exception.Message);
        }

        if (!TryParseVersion(json, out var version, out var parseError))
        {
            return DiagnosticCandidate(
                rootPath,
                editorPath,
                buildVersionPath,
                parseError);
        }

        var isUsable = File.Exists(editorPath);
        var engine = new InstalledEngine(
            DisplayName: $"Unreal Engine {version} (Manual)",
            Association: version,
            DisplayVersion: version,
            RootPath: rootPath,
            EditorPath: editorPath,
            Source: EngineSource.Manual,
            IsUsable: isUsable);

        return isUsable
            ? new EngineProviderResult([engine], [])
            : new EngineProviderResult(
                [engine],
                [new EngineProviderIssue(
                    editorPath,
                    "Expected Unreal Editor executable was not found.")]);
    }

    private static EngineProviderResult InvalidRoot(
        string? manualRoot,
        string message) =>
        new(
            [],
            [new EngineProviderIssue(
                manualRoot ?? "Manual engine root",
                message)]);

    private static EngineProviderResult DiagnosticCandidate(
        string rootPath,
        string editorPath,
        string issueContext,
        string issueMessage)
    {
        var engine = new InstalledEngine(
            DisplayName: $"Unreal Engine (Manual) — {rootPath}",
            Association: null,
            DisplayVersion: null,
            RootPath: rootPath,
            EditorPath: editorPath,
            Source: EngineSource.Manual,
            IsUsable: false);
        return new EngineProviderResult(
            [engine],
            [new EngineProviderIssue(issueContext, issueMessage)]);
    }

    private static bool TryNormalizeRootPath(
        string? manualRoot,
        out string rootPath,
        out string error)
    {
        rootPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(manualRoot))
        {
            error = "Manual engine root must be a non-empty path.";
            return false;
        }

        var trimmedRoot = manualRoot.Trim();
        if (!Path.IsPathFullyQualified(trimmedRoot))
        {
            error = "Manual engine root must be an absolute path.";
            return false;
        }

        try
        {
            rootPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(trimmedRoot));
            return true;
        }
        catch (Exception exception) when (IsExpectedPathFailure(exception))
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryParseVersion(
        string json,
        out string version,
        out string error)
    {
        version = string.Empty;
        error = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !TryGetNonNegativeInt32(
                    document.RootElement,
                    "MajorVersion",
                    out var major)
                || !TryGetNonNegativeInt32(
                    document.RootElement,
                    "MinorVersion",
                    out var minor))
            {
                error = "Build.version must contain non-negative integer MajorVersion and MinorVersion values.";
                return false;
            }

            int? patch = null;
            if (document.RootElement.TryGetProperty("PatchVersion", out var patchElement))
            {
                if (patchElement.ValueKind != JsonValueKind.Number
                    || !patchElement.TryGetInt32(out var parsedPatch)
                    || parsedPatch < 0)
                {
                    error = "Build.version PatchVersion must be a non-negative integer when present.";
                    return false;
                }

                patch = parsedPatch;
            }

            var majorMinor = string.Create(
                CultureInfo.InvariantCulture,
                $"{major}.{minor}");
            version = patch is int patchValue
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{majorMinor}.{patchValue}")
                : majorMinor;
            return true;
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryGetNonNegativeInt32(
        JsonElement root,
        string propertyName,
        out int value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var element)
               && element.ValueKind == JsonValueKind.Number
               && element.TryGetInt32(out value)
               && value >= 0;
    }

    private static bool IsExpectedPathFailure(Exception exception) =>
        exception is ArgumentException
            or NotSupportedException
            or SecurityException
            or IOException;

    private static bool IsExpectedReadFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException;
}
