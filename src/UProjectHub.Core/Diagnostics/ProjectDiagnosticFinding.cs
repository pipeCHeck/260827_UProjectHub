namespace UProjectHub.Core.Diagnostics;

public static class ProjectDiagnosticCodes
{
    public const string ProjectMissing = "project.missing";
    public const string ProjectBroken = "project.broken";
    public const string EngineMissing = "engine.missing";
    public const string EngineAmbiguous = "engine.ambiguous";
    public const string EngineUnknown = "engine.unknown";
    public const string SolutionMissing = "solution.missing";
    public const string SolutionMultiple = "solution.multiple";
    public const string SolutionInaccessible = "solution.inaccessible";
    public const string DiagnosticsPartialFailure = "diagnostics.partialFailure";
}

public enum ProjectDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public enum ProjectDiagnosticSuggestedAction
{
    GenerateProjectFiles,
}

public sealed record ProjectDiagnosticFinding(
    string Code,
    ProjectDiagnosticSeverity Severity,
    bool IsBlocking,
    ProjectDiagnosticSuggestedAction? SuggestedAction = null);
