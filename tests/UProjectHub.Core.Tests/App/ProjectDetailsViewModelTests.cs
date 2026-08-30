using UProjectHub.App.ViewModels;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ProjectDetailsViewModelTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void DetailsKeepOverviewMetadataAndDiagnosticsSeparated()
    {
        var project = CreateProject(EngineResolutionState.Missing);
        var report = new ProjectDiagnosticReport(
            project.ProjectFilePath,
            Now,
            [
                new ProjectDiagnosticFinding(
                    ProjectDiagnosticCodes.EngineMissing,
                    ProjectDiagnosticSeverity.Error,
                    IsBlocking: true),
            ]);

        var details = new ProjectDetailsViewModel(
            new ProjectOverviewViewModel(project, TimeZoneInfo.Utc),
            new ProjectDiagnosticsViewModel(report));

        Assert.AreEqual("Game", details.Name);
        Assert.AreEqual(project.ProjectFilePath.Value, details.Overview.ProjectPath);
        Assert.AreEqual("5.8", details.Overview.EngineAssociation);
        Assert.HasCount(1, details.Diagnostics.Findings);
        Assert.AreEqual(
            "The matching Unreal Engine installation was not found.",
            details.Diagnostics.Findings[0].Message);
        Assert.AreEqual("Error", details.Diagnostics.Findings[0].SeverityLabel);
        Assert.IsFalse(details.Diagnostics.Findings[0].HasSuggestedAction);
    }

    [TestMethod]
    public void HealthyReportKeepsDiagnosticsSectionQuiet()
    {
        var project = CreateProject(EngineResolutionState.Resolved);
        var report = new ProjectDiagnosticReport(
            project.ProjectFilePath,
            Now,
            Array.Empty<ProjectDiagnosticFinding>());

        var diagnostics = new ProjectDiagnosticsViewModel(report);

        Assert.IsFalse(diagnostics.HasFindings);
        Assert.IsEmpty(diagnostics.Findings);
    }

    [TestMethod]
    public void DetailsCanRequestTagsAndNotesAsItsInitialSection()
    {
        var project = CreateProject(EngineResolutionState.Resolved);
        var details = new ProjectDetailsViewModel(
            new ProjectOverviewViewModel(project),
            new ProjectDiagnosticsViewModel(new ProjectDiagnosticReport(
                project.ProjectFilePath,
                Now,
                Array.Empty<ProjectDiagnosticFinding>())),
            initialSection: ProjectDetailsSection.TagsAndNotes);

        Assert.AreEqual(ProjectDetailsSection.TagsAndNotes, details.SelectedSection);
        Assert.AreEqual(2, details.SelectedTabIndex);
    }

    [TestMethod]
    public void SourceControlIsTheFourthProjectDetailsSection()
    {
        var project = CreateProject(EngineResolutionState.Resolved);
        var details = new ProjectDetailsViewModel(
            new ProjectOverviewViewModel(project),
            new ProjectDiagnosticsViewModel(new ProjectDiagnosticReport(
                project.ProjectFilePath,
                Now,
                Array.Empty<ProjectDiagnosticFinding>())),
            initialSection: ProjectDetailsSection.SourceControl);

        Assert.AreEqual(ProjectDetailsSection.SourceControl, details.SelectedSection);
        Assert.AreEqual(3, details.SelectedTabIndex);
    }

    private static UnrealProject CreateProject(
        EngineResolutionState engineState) =>
        new(
            "Game",
            new ProjectPath(@"D:\Projects\Game\Game.uproject"),
            "5.8",
            "5.8.1",
            ProjectType.Cpp,
            Now.AddHours(-1),
            LastLaunched: null,
            IsFavorite: false,
            ProjectState.Available,
            engineState);
}
