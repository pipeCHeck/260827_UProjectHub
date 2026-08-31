using System.Security;
using System.Text.Json;
using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Engines;

public sealed class EngineDiscoveryService
{
    private readonly IReadOnlyList<IEngineProvider> _providers;

    public EngineDiscoveryService(IEnumerable<IEngineProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = Array.AsReadOnly(providers.ToArray());
        if (_providers.Any(provider => provider is null))
        {
            throw new ArgumentException(
                "Engine provider collection cannot contain null values.",
                nameof(providers));
        }
    }

    public async Task<EngineDiscoveryResult> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var engines = new List<InstalledEngine>();
        var issues = new List<EngineProviderIssue>();
        var physicalEngines = new Dictionary<string, InstalledEngine>(
            StringComparer.OrdinalIgnoreCase);
        var engineIdentities = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EngineProviderResult providerResult;
            try
            {
                providerResult = await provider.DiscoverAsync(
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsExpectedProviderFailure(exception))
            {
                issues.Add(new EngineProviderIssue(
                    provider.GetType().Name,
                    exception.Message));
                continue;
            }

            issues.AddRange(providerResult.Issues);
            foreach (var engine in providerResult.Engines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryCanonicalizeEditorPath(
                    engine.EditorPath,
                    out var canonicalEditorPath,
                    out var pathError))
                {
                    engines.Add(engine);
                    issues.Add(new EngineProviderIssue(
                        engine.EditorPath,
                        pathError));
                    continue;
                }

                var identity = GetEngineIdentity(canonicalEditorPath, engine.Association);
                if (!engineIdentities.Add(identity))
                {
                    continue;
                }

                var candidate = engine;
                if (physicalEngines.TryGetValue(
                        canonicalEditorPath,
                        out var physicalEngine)
                    && EngineAssociationParser.Parse(engine.Association)
                        is GuidEngineAssociation registeredAlias)
                {
                    candidate = physicalEngine with
                    {
                        Association = registeredAlias.Identifier.ToString("B"),
                    };
                }
                else
                {
                    physicalEngines.TryAdd(canonicalEditorPath, engine);
                }

                engines.Add(candidate);
            }
        }

        return new EngineDiscoveryResult(engines, issues);
    }

    private static string GetEngineIdentity(
        string canonicalEditorPath,
        string? association)
    {
        var associationIdentity = EngineAssociationParser.Parse(association)
            is GuidEngineAssociation registeredAlias
            ? registeredAlias.Identifier.ToString("D")
            : "default";
        return $"{canonicalEditorPath}\0{associationIdentity}";
    }

    private static bool TryCanonicalizeEditorPath(
        string editorPath,
        out string canonicalPath,
        out string error)
    {
        canonicalPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(editorPath))
        {
            error = "Engine editor path is empty and cannot be deduplicated.";
            return false;
        }

        try
        {
            canonicalPath = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(editorPath))
                .Replace(
                    Path.AltDirectorySeparatorChar,
                    Path.DirectorySeparatorChar);
            return true;
        }
        catch (Exception exception) when (IsExpectedPathFailure(exception))
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool IsExpectedProviderFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or JsonException;

    private static bool IsExpectedPathFailure(Exception exception) =>
        exception is ArgumentException
            or NotSupportedException
            or SecurityException
            or IOException;
}
