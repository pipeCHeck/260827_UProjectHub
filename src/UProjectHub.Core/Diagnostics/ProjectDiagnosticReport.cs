using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Diagnostics;

public sealed record ProjectDiagnosticReport(
    ProjectPath ProjectPath,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<ProjectDiagnosticFinding> Findings)
{
    public ProjectDiagnosticFinding? PrimaryListFinding =>
        Findings.FirstOrDefault(finding =>
            finding.Severity is ProjectDiagnosticSeverity.Error
                or ProjectDiagnosticSeverity.Warning)
        ?? Findings.FirstOrDefault(finding =>
            finding.SuggestedAction is not null);
}
