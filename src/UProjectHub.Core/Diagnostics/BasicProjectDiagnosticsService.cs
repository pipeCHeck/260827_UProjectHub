using UProjectHub.Core.Models;
using UProjectHub.Core.Time;

namespace UProjectHub.Core.Diagnostics;

public sealed class BasicProjectDiagnosticsService(IClock clock)
{
    private readonly IClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));

    public ProjectDiagnosticReport Diagnose(
        UnrealProject project,
        IEnumerable<ProjectDiagnosticFinding>? supplementalFindings = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var findings = new List<ProjectDiagnosticFinding>();
        var finding = CreateStateFinding(project);
        if (finding is not null)
        {
            findings.Add(finding);
        }

        if (supplementalFindings is not null)
        {
            findings.AddRange(supplementalFindings);
        }

        var orderedFindings = findings
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => GetPriority(finding.Code))
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ToArray();

        return new ProjectDiagnosticReport(
            project.ProjectFilePath,
            _clock.UtcNow,
            Array.AsReadOnly(orderedFindings));
    }

    private static ProjectDiagnosticFinding? CreateStateFinding(
        UnrealProject project)
    {
        if (project.ProjectState == ProjectState.Missing)
        {
            return Blocking(
                ProjectDiagnosticCodes.ProjectMissing,
                ProjectDiagnosticSeverity.Error);
        }

        if (project.ProjectState == ProjectState.Broken)
        {
            return Blocking(
                ProjectDiagnosticCodes.ProjectBroken,
                ProjectDiagnosticSeverity.Error);
        }

        return project.EngineState switch
        {
            EngineResolutionState.Missing => Blocking(
                ProjectDiagnosticCodes.EngineMissing,
                ProjectDiagnosticSeverity.Error),
            EngineResolutionState.Ambiguous => Blocking(
                ProjectDiagnosticCodes.EngineAmbiguous,
                ProjectDiagnosticSeverity.Warning),
            EngineResolutionState.Unknown => Blocking(
                ProjectDiagnosticCodes.EngineUnknown,
                ProjectDiagnosticSeverity.Warning),
            _ => null,
        };
    }

    private static ProjectDiagnosticFinding Blocking(
        string code,
        ProjectDiagnosticSeverity severity) =>
        new(code, severity, IsBlocking: true);

    private static int GetPriority(string code) =>
        code switch
        {
            ProjectDiagnosticCodes.ProjectMissing => 10,
            ProjectDiagnosticCodes.ProjectBroken => 20,
            ProjectDiagnosticCodes.EngineMissing => 30,
            ProjectDiagnosticCodes.EngineAmbiguous => 40,
            ProjectDiagnosticCodes.EngineUnknown => 50,
            ProjectDiagnosticCodes.SolutionInaccessible => 60,
            ProjectDiagnosticCodes.SolutionMultiple => 70,
            ProjectDiagnosticCodes.DiagnosticsPartialFailure => 80,
            ProjectDiagnosticCodes.SolutionMissing => 90,
            _ => 100,
        };
}
