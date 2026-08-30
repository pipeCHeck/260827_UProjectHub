using UProjectHub.App.Converters;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Models;
using UProjectHub.Windows.SourceControl;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectRowViewModel : ObservableObject
{
    private ProjectDiagnosticReport? _diagnosticReport;
    private GitProjectStatus? _gitStatus;

    public ProjectRowViewModel(
        UnrealProject project,
        ProjectContextActionsViewModel? contextActions = null,
        ProjectDiagnosticReport? diagnosticReport = null,
        LocalizationService? localization = null,
        GitProjectStatus? gitStatus = null)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        ContextActions = contextActions;
        _diagnosticReport = diagnosticReport;
        _gitStatus = gitStatus;
        _localization = localization;
    }

    private readonly LocalizationService? _localization;

    public UnrealProject Project { get; }

    public ProjectContextActionsViewModel? ContextActions { get; }

    public ProjectDiagnosticReport? DiagnosticReport => _diagnosticReport;

    public GitProjectStatus? GitStatus => _gitStatus;

    public GitProjectState? GitState => GitStatus?.State;

    public string GitStatusDisplay => GitState switch
    {
        GitProjectState.NotRepository => Localize(
            "String.GitNotRepository",
            "Not Repository"),
        GitProjectState.Clean => Localize("String.GitClean", "Clean"),
        GitProjectState.Changed => Localize("String.GitChanged", "Changed"),
        GitProjectState.Failed => Localize("String.GitFailed", "Failed"),
        GitProjectState.GitUnavailable => Localize(
            "String.GitUnavailable",
            "Git Unavailable"),
        _ => string.Empty,
    };

    public bool IsGitChanged => GitState == GitProjectState.Changed;

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

    public IReadOnlyList<string> VisibleTags => Project.Tags.Take(3).ToArray();

    public int AdditionalTagCount => Math.Max(0, Project.Tags.Count - 3);

    public bool HasAdditionalTags => AdditionalTagCount > 0;

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

    public void UpdateDiagnosticReport(ProjectDiagnosticReport? report)
    {
        if (!SetProperty(
                ref _diagnosticReport,
                report,
                nameof(DiagnosticReport)))
        {
            return;
        }

        RefreshDiagnosticPresentation();
    }

    public void UpdateGitStatus(GitProjectStatus? status)
    {
        if (!SetProperty(ref _gitStatus, status, nameof(GitStatus)))
        {
            return;
        }

        RefreshGitPresentation();
    }

    public void RefreshGitPresentation()
    {
        OnPropertyChanged(nameof(GitState));
        OnPropertyChanged(nameof(GitStatusDisplay));
        OnPropertyChanged(nameof(IsGitChanged));
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

    private string Localize(string key, string fallback) =>
        _localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;
}
