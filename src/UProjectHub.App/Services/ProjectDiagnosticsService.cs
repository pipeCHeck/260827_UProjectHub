using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Models;
using UProjectHub.Windows.Launching;

namespace UProjectHub.App.Services;

public sealed class ProjectDiagnosticsService
{
    private readonly BasicProjectDiagnosticsService _basicDiagnostics;
    private readonly IVisualStudioSolutionLocator _solutionLocator;
    private readonly Func<UnrealProject, bool> _canGenerateProjectFiles;

    public ProjectDiagnosticsService(
        BasicProjectDiagnosticsService basicDiagnostics,
        IVisualStudioSolutionLocator solutionLocator,
        Func<UnrealProject, bool> canGenerateProjectFiles)
    {
        _basicDiagnostics = basicDiagnostics
            ?? throw new ArgumentNullException(nameof(basicDiagnostics));
        _solutionLocator = solutionLocator
            ?? throw new ArgumentNullException(nameof(solutionLocator));
        _canGenerateProjectFiles = canGenerateProjectFiles
            ?? throw new ArgumentNullException(nameof(canGenerateProjectFiles));
    }

    public ProjectDiagnosticReport Diagnose(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var supplementalFindings = new List<ProjectDiagnosticFinding>();
        if (project.ProjectState == ProjectState.Available
            && project.ProjectType == ProjectType.Cpp)
        {
            var solutionFinding = CreateSolutionFinding(project);
            if (solutionFinding is not null)
            {
                supplementalFindings.Add(solutionFinding);
            }
        }

        return _basicDiagnostics.Diagnose(project, supplementalFindings);
    }

    private ProjectDiagnosticFinding? CreateSolutionFinding(
        UnrealProject project)
    {
        try
        {
            var selection = _solutionLocator.Locate(project);
            return selection.State switch
            {
                VisualStudioSolutionState.Missing
                    when _canGenerateProjectFiles(project) =>
                    new ProjectDiagnosticFinding(
                        ProjectDiagnosticCodes.SolutionMissing,
                        ProjectDiagnosticSeverity.Info,
                        IsBlocking: false,
                        ProjectDiagnosticSuggestedAction.GenerateProjectFiles),
                VisualStudioSolutionState.Multiple =>
                    Warning(ProjectDiagnosticCodes.SolutionMultiple),
                VisualStudioSolutionState.Inaccessible =>
                    Warning(ProjectDiagnosticCodes.SolutionInaccessible),
                _ => null,
            };
        }
        catch (Exception)
        {
            return Warning(ProjectDiagnosticCodes.DiagnosticsPartialFailure);
        }
    }

    private static ProjectDiagnosticFinding Warning(string code) =>
        new(
            code,
            ProjectDiagnosticSeverity.Warning,
            IsBlocking: false);
}
