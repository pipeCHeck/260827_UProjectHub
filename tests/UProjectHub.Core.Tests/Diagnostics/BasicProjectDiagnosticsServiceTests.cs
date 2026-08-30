using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Tests.Time;

namespace UProjectHub.Core.Tests.Diagnostics;

[TestClass]
public sealed class BasicProjectDiagnosticsServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AvailableProjectWithResolvedEngineHasNoFindings()
    {
        var service = new BasicProjectDiagnosticsService(new FakeClock(Now));

        var report = service.Diagnose(CreateProject());

        Assert.IsEmpty(report.Findings);
        Assert.IsNull(report.PrimaryListFinding);
    }

    [TestMethod]
    [DataRow(
        ProjectState.Missing,
        EngineResolutionState.Unknown,
        "project.missing",
        ProjectDiagnosticSeverity.Error)]
    [DataRow(
        ProjectState.Broken,
        EngineResolutionState.Unknown,
        "project.broken",
        ProjectDiagnosticSeverity.Error)]
    [DataRow(
        ProjectState.Available,
        EngineResolutionState.Missing,
        "engine.missing",
        ProjectDiagnosticSeverity.Error)]
    [DataRow(
        ProjectState.Available,
        EngineResolutionState.Ambiguous,
        "engine.ambiguous",
        ProjectDiagnosticSeverity.Warning)]
    [DataRow(
        ProjectState.Available,
        EngineResolutionState.Unknown,
        "engine.unknown",
        ProjectDiagnosticSeverity.Warning)]
    public void ExistingProblemStatesProduceOneBlockingFinding(
        ProjectState projectState,
        EngineResolutionState engineState,
        string expectedCode,
        ProjectDiagnosticSeverity expectedSeverity)
    {
        var service = new BasicProjectDiagnosticsService(new FakeClock(Now));

        var report = service.Diagnose(CreateProject(projectState, engineState));

        Assert.HasCount(1, report.Findings);
        Assert.AreEqual(expectedCode, report.Findings[0].Code);
        Assert.AreEqual(expectedSeverity, report.Findings[0].Severity);
        Assert.IsTrue(report.Findings[0].IsBlocking);
        Assert.AreSame(report.Findings[0], report.PrimaryListFinding);
    }

    [TestMethod]
    public void ActionableSolutionInfoIsShownWhenNoProblemFindingExists()
    {
        var service = new BasicProjectDiagnosticsService(new FakeClock(Now));
        var solutionInfo = new ProjectDiagnosticFinding(
            "solution.missing",
            ProjectDiagnosticSeverity.Info,
            IsBlocking: false,
            ProjectDiagnosticSuggestedAction.GenerateProjectFiles);

        var report = service.Diagnose(CreateProject(), [solutionInfo]);

        Assert.HasCount(1, report.Findings);
        var primary = report.PrimaryListFinding;
        Assert.IsNotNull(primary);
        Assert.AreSame(solutionInfo, primary);
        Assert.AreEqual(
            ProjectDiagnosticSuggestedAction.GenerateProjectFiles,
            primary.SuggestedAction);
    }

    [TestMethod]
    public void WarningPriorityIsStableRegardlessOfSupplementalInputOrder()
    {
        var service = new BasicProjectDiagnosticsService(new FakeClock(Now));
        var partialFailure = new ProjectDiagnosticFinding(
            "diagnostics.partialFailure",
            ProjectDiagnosticSeverity.Warning,
            IsBlocking: false);
        var multipleSolutions = new ProjectDiagnosticFinding(
            "solution.multiple",
            ProjectDiagnosticSeverity.Warning,
            IsBlocking: false);

        var report = service.Diagnose(
            CreateProject(),
            [partialFailure, multipleSolutions]);

        Assert.AreEqual("solution.multiple", report.Findings[0].Code);
        Assert.AreSame(multipleSolutions, report.PrimaryListFinding);
    }

    private static UnrealProject CreateProject(
        ProjectState projectState = ProjectState.Available,
        EngineResolutionState engineState = EngineResolutionState.Resolved) =>
        new(
            "Game",
            new ProjectPath(@"D:\Projects\Game\Game.uproject"),
            "5.8",
            "5.8.1",
            ProjectType.Cpp,
            Now.AddDays(-1),
            LastLaunched: null,
            IsFavorite: false,
            projectState,
            engineState);
}
