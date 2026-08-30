using UProjectHub.App.Services;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Tests.Time;
using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ProjectDiagnosticsServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void MissingSolutionIsActionableInfoWhenGenerationIsAvailable()
    {
        var locator = new FakeSolutionLocator(
            _ => VisualStudioSolutionSelection.Missing());
        var service = new ProjectDiagnosticsService(
            new BasicProjectDiagnosticsService(new FakeClock(Now)),
            locator,
            _ => true);

        var report = service.Diagnose(CreateProject());

        Assert.HasCount(1, report.Findings);
        var finding = report.Findings[0];
        Assert.AreEqual(ProjectDiagnosticCodes.SolutionMissing, finding.Code);
        Assert.AreEqual(ProjectDiagnosticSeverity.Info, finding.Severity);
        Assert.IsFalse(finding.IsBlocking);
        Assert.AreEqual(
            ProjectDiagnosticSuggestedAction.GenerateProjectFiles,
            finding.SuggestedAction);
    }

    [TestMethod]
    [DataRow(
        VisualStudioSolutionState.Multiple,
        "solution.multiple")]
    [DataRow(
        VisualStudioSolutionState.Inaccessible,
        "solution.inaccessible")]
    public void NonSelectableSolutionStatesProduceWarnings(
        VisualStudioSolutionState solutionState,
        string expectedCode)
    {
        var locator = new FakeSolutionLocator(_ => solutionState switch
        {
            VisualStudioSolutionState.Multiple =>
                VisualStudioSolutionSelection.Multiple(
                [
                    @"D:\Projects\Game\Game.sln",
                    @"D:\Projects\Game\Tools.sln",
                ]),
            VisualStudioSolutionState.Inaccessible =>
                VisualStudioSolutionSelection.Inaccessible("Access denied."),
            _ => throw new ArgumentOutOfRangeException(nameof(solutionState)),
        });
        var service = new ProjectDiagnosticsService(
            new BasicProjectDiagnosticsService(new FakeClock(Now)),
            locator,
            _ => true);

        var report = service.Diagnose(CreateProject());

        Assert.HasCount(1, report.Findings);
        Assert.AreEqual(expectedCode, report.Findings[0].Code);
        Assert.AreEqual(
            ProjectDiagnosticSeverity.Warning,
            report.Findings[0].Severity);
        Assert.IsNull(report.Findings[0].SuggestedAction);
    }

    [TestMethod]
    public void LocatorFailureIsIsolatedAndLaterProjectStillDiagnoses()
    {
        var locator = new FakeSolutionLocator(project =>
        {
            if (project.Name == "First")
            {
                throw new IOException("Access denied.");
            }

            return VisualStudioSolutionSelection.Missing();
        });
        var service = new ProjectDiagnosticsService(
            new BasicProjectDiagnosticsService(new FakeClock(Now)),
            locator,
            _ => true);

        var failedReport = service.Diagnose(CreateProject("First"));
        var laterReport = service.Diagnose(CreateProject("Later"));

        Assert.HasCount(1, failedReport.Findings);
        Assert.AreEqual(
            ProjectDiagnosticCodes.DiagnosticsPartialFailure,
            failedReport.Findings[0].Code);
        Assert.AreEqual(
            ProjectDiagnosticSeverity.Warning,
            failedReport.Findings[0].Severity);
        Assert.HasCount(1, laterReport.Findings);
        Assert.AreEqual(
            ProjectDiagnosticCodes.SolutionMissing,
            laterReport.Findings[0].Code);
    }

    [TestMethod]
    public void SnapshotStoreDoesNotRepeatSolutionLookupForCachedReads()
    {
        var locator = new FakeSolutionLocator(
            _ => VisualStudioSolutionSelection.Missing());
        var diagnostics = new ProjectDiagnosticsService(
            new BasicProjectDiagnosticsService(new FakeClock(Now)),
            locator,
            _ => true);
        var store = new ProjectDiagnosticSnapshotStore(diagnostics);
        var project = CreateProject();

        store.Refresh([project]);
        var first = store.Get(project);
        var second = store.Get(project);

        Assert.AreSame(first, second);
        Assert.AreEqual(1, locator.LocateCount);
    }

    [TestMethod]
    public void EngineProblemOutranksActionableMissingSolution()
    {
        var service = new ProjectDiagnosticsService(
            new BasicProjectDiagnosticsService(new FakeClock(Now)),
            new FakeSolutionLocator(
                _ => VisualStudioSolutionSelection.Missing()),
            _ => true);

        var report = service.Diagnose(CreateProject(
            engineState: EngineResolutionState.Missing));

        Assert.HasCount(2, report.Findings);
        Assert.AreEqual(
            ProjectDiagnosticCodes.EngineMissing,
            report.PrimaryListFinding?.Code);
        Assert.AreEqual(
            ProjectDiagnosticCodes.SolutionMissing,
            report.Findings[1].Code);
    }

    [TestMethod]
    public void BlueprintProjectDoesNotInspectVisualStudioSolutions()
    {
        var locator = new FakeSolutionLocator(
            _ => VisualStudioSolutionSelection.Missing());
        var service = new ProjectDiagnosticsService(
            new BasicProjectDiagnosticsService(new FakeClock(Now)),
            locator,
            _ => true);

        var report = service.Diagnose(CreateProject(
            projectType: ProjectType.Blueprint));

        Assert.IsEmpty(report.Findings);
        Assert.AreEqual(0, locator.LocateCount);
    }

    private static UnrealProject CreateProject(
        string name = "Game",
        ProjectType projectType = ProjectType.Cpp,
        EngineResolutionState engineState = EngineResolutionState.Resolved) =>
        new(
            name,
            new ProjectPath($@"D:\Projects\{name}\{name}.uproject"),
            "5.8",
            "5.8.1",
            projectType,
            Now.AddDays(-1),
            LastLaunched: null,
            IsFavorite: false,
            ProjectState.Available,
            engineState);

    private sealed class FakeSolutionLocator(
        Func<UnrealProject, VisualStudioSolutionSelection> locate)
        : IVisualStudioSolutionLocator
    {
        public int LocateCount { get; private set; }

        public VisualStudioSolutionSelection Locate(UnrealProject project)
        {
            LocateCount++;
            return locate(project);
        }
    }
}
