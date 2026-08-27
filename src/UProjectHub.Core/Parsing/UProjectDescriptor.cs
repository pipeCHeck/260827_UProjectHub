namespace UProjectHub.Core.Parsing;

public sealed record UProjectDescriptor(
    int? FileVersion,
    string? EngineAssociation,
    IReadOnlyList<UProjectModule>? Modules);
