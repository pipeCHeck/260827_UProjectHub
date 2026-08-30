using UProjectHub.Core.Diagnostics;

namespace UProjectHub.App.Services;

public static class ProjectDiagnosticTextService
{
    public static string GetMessage(
        ProjectDiagnosticFinding finding,
        LocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(finding);

        var (key, fallback) = finding.Code switch
        {
            ProjectDiagnosticCodes.ProjectMissing =>
                ("String.StateMissing", "Missing"),
            ProjectDiagnosticCodes.ProjectBroken =>
                ("String.StateBroken", "Project information unavailable"),
            ProjectDiagnosticCodes.EngineMissing =>
                ("String.DiagnosticEngineMissing",
                    "The matching Unreal Engine installation was not found."),
            ProjectDiagnosticCodes.EngineAmbiguous =>
                ("String.DiagnosticEngineAmbiguous",
                    "Multiple Unreal Engine installations match this project."),
            ProjectDiagnosticCodes.EngineUnknown =>
                ("String.DiagnosticEngineUnknown",
                    "The project's Unreal Engine could not be determined."),
            ProjectDiagnosticCodes.SolutionMissing =>
                ("String.OpenVisualStudioSolutionMissing",
                    "No existing .sln file was found. Generate Visual Studio project files to create one."),
            ProjectDiagnosticCodes.SolutionMultiple =>
                ("String.OpenVisualStudioSolutionMultiple",
                    "Multiple .sln files were found, so no unique solution could be selected."),
            ProjectDiagnosticCodes.SolutionInaccessible =>
                ("String.OpenVisualStudioSolutionInaccessible",
                    "The project folder could not be inspected for .sln files."),
            ProjectDiagnosticCodes.DiagnosticsPartialFailure =>
                ("String.DiagnosticPartialFailure",
                    "Some basic diagnostics could not be completed."),
            _ => (string.Empty, finding.Code),
        };

        return string.IsNullOrEmpty(key)
            ? fallback
            : Localize(localization, key, fallback);
    }

    private static string Localize(
        LocalizationService? localization,
        string key,
        string fallback) =>
        localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;
}
