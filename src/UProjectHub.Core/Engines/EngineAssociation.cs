using UProjectHub.Core.Versions;

namespace UProjectHub.Core.Engines;

public abstract record EngineAssociation;

public sealed record NumericEngineAssociation(
    EngineVersion Version) : EngineAssociation;

public sealed record GuidEngineAssociation(
    Guid Identifier) : EngineAssociation;

public sealed record UnknownEngineAssociation : EngineAssociation
{
    public static UnknownEngineAssociation Instance { get; } = new();

    private UnknownEngineAssociation()
    {
    }
}
