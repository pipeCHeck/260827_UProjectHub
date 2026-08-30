using UProjectHub.App.Converters;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Models;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectRowViewModel : ObservableObject
{
    private ProjectDiagnosticReport? _diagnosticReport;

    public ProjectRowViewModel(
        UnrealProject project,
        ProjectContextActionsViewModel? contextActions = null,
        ProjectDiagnosticReport? diagnosticReport = null,
        LocalizationService? localization = null)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        ContextActions = contextActions;
        _diagnosticReport = diagnosticReport;
        _localization = localization;
    }

    private readonly LocalizationService? _localization;

    public UnrealProject Project { get; }

    public ProjectContextActionsViewModel? ContextActions { get; }

    public ProjectDiagnosticReport? DiagnosticReport => _diagnosticReport;

    public bool IsFavorite => Project.IsFavorite;

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    public string Name => Project.Name;

    public string ProjectPath => Project.ProjectFilePath.Value;

    public string ProjectDirectory => Project.ProjectDirectory;

    public string EngineDisplay => FirstNonEmpty(
        Project.EngineDisplayVersion,
        Project.EngineAssociation) ?? "—";

    public string TypeDisplay => Project.ProjectState == ProjectState.Broken
        ? "—"
        : Project.ProjectType switch
        {
            ProjectType.Cpp => "C++",
            ProjectType.Blueprint => "Blueprint",
            _ => "—",
        };

    public DateTimeOffset LastModified => Project.LastModified;

    public DateTimeOffset? LastLaunched => Project.LastLaunched;

    public ProjectState ProjectState => Project.ProjectState;

    public EngineResolutionState EngineState => Project.EngineState;

    public ProjectDiagnosticFinding? PrimaryDiagnostic =>
        DiagnosticReport?.PrimaryListFinding;

    public ProjectDiagnosticSeverity? DiagnosticSeverity =>
        PrimaryDiagnostic?.Severity;

    public string DiagnosticMessage => PrimaryDiagnostic is { } finding
        ? ProjectDiagnosticTextService.GetMessage(finding, _localization)
        : ProjectStateMessageConverter.GetMessage(ProjectState, _localization);

    public string StateMessage => DiagnosticMessage;

    public void UpdateDiagnosticReport(ProjectDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!SetProperty(
                ref _diagnosticReport,
                report,
                nameof(DiagnosticReport)))
        {
            return;
        }

        RefreshDiagnosticPresentation();
    }

    public void RefreshDiagnosticPresentation()
    {
        OnPropertyChanged(nameof(PrimaryDiagnostic));
        OnPropertyChanged(nameof(DiagnosticSeverity));
        OnPropertyChanged(nameof(DiagnosticMessage));
        OnPropertyChanged(nameof(StateMessage));
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
