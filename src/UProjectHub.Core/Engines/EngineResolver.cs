using UProjectHub.Core.Models;
using UProjectHub.Core.Versions;

namespace UProjectHub.Core.Engines;

public static class EngineResolver
{
    public static EngineResolution Resolve(
        string? rawAssociation,
        IReadOnlyCollection<InstalledEngine> installedEngines)
    {
        ArgumentNullException.ThrowIfNull(installedEngines);

        return EngineAssociationParser.Parse(rawAssociation) switch
        {
            NumericEngineAssociation numeric => EngineResolution.FromMatches(
                installedEngines.Where(engine =>
                    IsNumericMatch(engine, numeric.Version))),
            GuidEngineAssociation registeredBuild => EngineResolution.FromMatches(
                installedEngines.Where(engine =>
                    IsGuidMatch(engine, registeredBuild.Identifier))),
            _ => EngineResolution.Unknown(),
        };
    }

    private static bool IsNumericMatch(
        InstalledEngine engine,
        EngineVersion projectVersion)
    {
        if (!engine.IsUsable
            || !EngineVersion.TryParse(
                engine.DisplayVersion?.Trim(),
                out var installedVersion))
        {
            return false;
        }

        return installedVersion.Major == projectVersion.Major
               && installedVersion.Minor == projectVersion.Minor;
    }

    private static bool IsGuidMatch(
        InstalledEngine engine,
        Guid projectIdentifier) =>
        engine.IsUsable
        && EngineAssociationParser.Parse(engine.Association)
            is GuidEngineAssociation registeredBuild
        && registeredBuild.Identifier == projectIdentifier;
}
