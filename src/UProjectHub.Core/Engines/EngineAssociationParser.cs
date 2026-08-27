using UProjectHub.Core.Versions;

namespace UProjectHub.Core.Engines;

public static class EngineAssociationParser
{
    public static EngineAssociation Parse(string? rawAssociation)
    {
        var normalized = rawAssociation?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return UnknownEngineAssociation.Instance;
        }

        if (EngineVersion.TryParse(normalized, out var version))
        {
            return new NumericEngineAssociation(version);
        }

        if (Guid.TryParse(normalized, out var identifier))
        {
            return new GuidEngineAssociation(identifier);
        }

        return UnknownEngineAssociation.Instance;
    }
}
