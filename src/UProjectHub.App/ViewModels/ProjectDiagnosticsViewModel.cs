using UProjectHub.App.Services;
using UProjectHub.Core.Diagnostics;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectDiagnosticsViewModel
{
    public ProjectDiagnosticsViewModel(
        ProjectDiagnosticReport report,
        LocalizationService? localization = null)
    {
        Report = report ?? throw new ArgumentNullException(nameof(report));
        Findings = Array.AsReadOnly(report.Findings
            .Select(finding => new ProjectDiagnosticFindingViewModel(
                finding,
                localization))
            .ToArray());
        EmptyMessage = Localize(
            localization,
            "String.DiagnosticsEmpty",
            "No basic diagnostics need attention.");
    }

    public ProjectDiagnosticReport Report { get; }

    public IReadOnlyList<ProjectDiagnosticFindingViewModel> Findings { get; }

    public bool HasFindings => Findings.Count > 0;

    public bool HasNoFindings => !HasFindings;

    public string EmptyMessage { get; }

    private static string Localize(
        LocalizationService? localization,
        string key,
        string fallback) =>
        localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;
}

public sealed class ProjectDiagnosticFindingViewModel
{
    public ProjectDiagnosticFindingViewModel(
        ProjectDiagnosticFinding finding,
        LocalizationService? localization = null)
    {
        Finding = finding ?? throw new ArgumentNullException(nameof(finding));
        Message = ProjectDiagnosticTextService.GetMessage(finding, localization);
        SeverityLabel = finding.Severity switch
        {
            ProjectDiagnosticSeverity.Info =>
                Localize(localization, "String.DiagnosticSeverityInfo", "Info"),
            ProjectDiagnosticSeverity.Warning =>
                Localize(localization, "String.DiagnosticSeverityWarning", "Warning"),
            ProjectDiagnosticSeverity.Error =>
                Localize(localization, "String.DiagnosticSeverityError", "Error"),
            _ => string.Empty,
        };
        SuggestedActionLabel = finding.SuggestedAction switch
        {
            ProjectDiagnosticSuggestedAction.GenerateProjectFiles =>
                Localize(
                    localization,
                    "String.GenerateProjectFiles",
                    "Generate Visual Studio Project Files"),
            _ => string.Empty,
        };
    }

    public ProjectDiagnosticFinding Finding { get; }

    public ProjectDiagnosticSeverity Severity => Finding.Severity;

    public string SeverityLabel { get; }

    public string Message { get; }

    public bool IsBlocking => Finding.IsBlocking;

    public string SuggestedActionLabel { get; }

    public bool HasSuggestedAction =>
        Finding.SuggestedAction is not null;

    private static string Localize(
        LocalizationService? localization,
        string key,
        string fallback) =>
        localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;
}
