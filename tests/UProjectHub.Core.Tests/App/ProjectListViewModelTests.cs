using System.Globalization;
using UProjectHub.App.Converters;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ProjectListViewModelTests
{
    [TestMethod]
    public void EmptySnapshot_ShowsNoProjectsState()
    {
        var viewModel = new ProjectListViewModel();

        viewModel.SetSnapshot(CreateSnapshot());

        Assert.IsEmpty(viewModel.Rows);
        Assert.AreEqual(0, viewModel.TotalCount);
        Assert.AreEqual(0, viewModel.VisibleCount);
        Assert.AreEqual("Showing 0 of 0", viewModel.ShowingCountText);
        Assert.IsTrue(viewModel.IsNoProjectsState);
        Assert.IsFalse(viewModel.IsNoResultsState);
        Assert.IsFalse(viewModel.HasVisibleRows);
    }

    [TestMethod]
    public void Snapshot_MapsProjectMetadataToReadOnlyRows()
    {
        var project = CreateProject(
            name: "Game Academy",
            path: @"C:\Projects\Game Academy\GameAcademy.uproject",
            engineAssociation: "5.8",
            engineDisplayVersion: "5.8.2",
            projectType: ProjectType.Cpp,
            lastModified: new DateTimeOffset(2026, 8, 27, 3, 0, 0, TimeSpan.Zero),
            lastLaunched: new DateTimeOffset(2026, 8, 26, 2, 0, 0, TimeSpan.Zero),
            isFavorite: true);
        var viewModel = new ProjectListViewModel();

        viewModel.SetSnapshot(CreateSnapshot(project));

        Assert.HasCount(1, viewModel.Rows);
        var row = viewModel.Rows[0];
        Assert.AreSame(project, row.Project);
        Assert.IsTrue(row.IsFavorite);
        Assert.AreEqual("★", row.FavoriteGlyph);
        Assert.AreEqual("Game Academy", row.Name);
        Assert.AreEqual(project.ProjectFilePath.Value, row.ProjectPath);
        Assert.AreEqual(project.ProjectDirectory, row.ProjectDirectory);
        Assert.AreEqual("5.8.2", row.EngineDisplay);
        Assert.AreEqual("C++", row.TypeDisplay);
        Assert.AreEqual(project.LastModified, row.LastModified);
        Assert.AreEqual(project.LastLaunched, row.LastLaunched);
        Assert.AreEqual(ProjectState.Available, row.ProjectState);
        Assert.AreEqual(EngineResolutionState.Unknown, row.EngineState);
        Assert.AreEqual(string.Empty, row.StateMessage);
    }

    [TestMethod]
    public void EngineDisplay_UsesAssociationThenQuietUnknownWhenDisplayVersionIsMissing()
    {
        var associationOnly = CreateProject(
            "AssociationOnly",
            @"C:\Projects\AssociationOnly\AssociationOnly.uproject",
            engineAssociation: "{01234567-89AB-CDEF-0123-456789ABCDEF}");
        var unknown = CreateProject(
            "Unknown",
            @"C:\Projects\Unknown\Unknown.uproject");
        var viewModel = new ProjectListViewModel();

        viewModel.SetSnapshot(CreateSnapshot(associationOnly, unknown));

        Assert.AreEqual("{01234567-89AB-CDEF-0123-456789ABCDEF}", viewModel.Rows[0].EngineDisplay);
        Assert.AreEqual("—", viewModel.Rows[1].EngineDisplay);
    }

    [TestMethod]
    public void MissingAndBrokenProjects_RemainVisibleWithQuietProblemMessages()
    {
        var missing = CreateProject(
            "MissingGame",
            @"C:\Projects\MissingGame\MissingGame.uproject",
            projectState: ProjectState.Missing);
        var broken = CreateProject(
            "BrokenGame",
            @"C:\Projects\BrokenGame\BrokenGame.uproject",
            projectType: ProjectType.Blueprint,
            projectState: ProjectState.Broken,
            lastModified: DateTimeOffset.MinValue);
        var viewModel = new ProjectListViewModel();

        viewModel.SetSnapshot(CreateSnapshot(missing, broken));

        Assert.HasCount(2, viewModel.Rows);
        Assert.AreEqual("Missing", viewModel.Rows[0].StateMessage);
        Assert.AreEqual("Blueprint", viewModel.Rows[0].TypeDisplay);
        Assert.AreEqual("Project information unavailable", viewModel.Rows[1].StateMessage);
        Assert.AreEqual("—", viewModel.Rows[1].TypeDisplay);
        Assert.AreEqual(DateTimeOffset.MinValue, viewModel.Rows[1].LastModified);
    }

    [TestMethod]
    public void StateMessageConverter_HidesHealthyStateAndShowsOnlyProblems()
    {
        var converter = new ProjectStateMessageConverter();

        Assert.AreEqual(string.Empty, ConvertState(converter, ProjectState.Available));
        Assert.AreEqual("Missing", ConvertState(converter, ProjectState.Missing));
        Assert.AreEqual("Project information unavailable", ConvertState(converter, ProjectState.Broken));
    }

    [TestMethod]
    public void SetVisibleProjects_ChangesOnlyVisibleRowsAndPreservesSnapshotTotal()
    {
        var projects = Enumerable.Range(1, 28)
            .Select(index => CreateProject(
                $"Game{index}",
                $@"C:\Projects\Game{index}\Game{index}.uproject"))
            .ToArray();
        var snapshot = CreateSnapshot(projects);
        var viewModel = new ProjectListViewModel();
        viewModel.SetSnapshot(snapshot);

        viewModel.SetVisibleProjects([]);

        Assert.IsEmpty(viewModel.Rows);
        Assert.AreEqual(28, viewModel.TotalCount);
        Assert.AreEqual(0, viewModel.VisibleCount);
        Assert.AreEqual("Showing 0 of 28", viewModel.ShowingCountText);
        Assert.IsFalse(viewModel.IsNoProjectsState);
        Assert.IsTrue(viewModel.IsNoResultsState);
        Assert.HasCount(28, snapshot.Projects);
    }

    [TestMethod]
    public void SetVisibleProjects_MapsOnlyProvidedRowsWithoutChangingTotal()
    {
        var projects = Enumerable.Range(1, 5)
            .Select(index => CreateProject(
                $"Game{index}",
                $@"C:\Projects\Game{index}\Game{index}.uproject"))
            .ToArray();
        var viewModel = new ProjectListViewModel();
        viewModel.SetSnapshot(CreateSnapshot(projects));

        viewModel.SetVisibleProjects(projects.Take(2));

        Assert.HasCount(2, viewModel.Rows);
        Assert.AreEqual(5, viewModel.TotalCount);
        Assert.AreEqual(2, viewModel.VisibleCount);
        Assert.AreEqual("Showing 2 of 5", viewModel.ShowingCountText);
    }

    [TestMethod]
    public void SetProjects_KeepsMainHeaderAndProjectListCountsConsistent()
    {
        var list = new ProjectListViewModel();
        var main = new MainViewModel(new StatusBarViewModel(), projectList: list);
        var snapshot = CreateSnapshot(
            CreateProject("One", @"C:\Projects\One\One.uproject"),
            CreateProject("Two", @"C:\Projects\Two\Two.uproject"));

        main.SetProjects(snapshot);

        Assert.AreEqual(2, main.ProjectCount);
        Assert.AreEqual("2 projects", main.ProjectCountText);
        Assert.AreSame(list, main.ProjectList);
        Assert.AreEqual(2, main.ProjectList.TotalCount);
        Assert.AreEqual(2, main.ProjectList.VisibleCount);
    }

    [TestMethod]
    public void ThousandProjectSnapshot_MapsAllRowsInMemory()
    {
        var projects = Enumerable.Range(1, 1_000)
            .Select(index => CreateProject(
                $"Game{index}",
                $@"C:\Projects\Game{index}\Game{index}.uproject"))
            .ToArray();
        var viewModel = new ProjectListViewModel();

        viewModel.SetSnapshot(CreateSnapshot(projects));

        Assert.HasCount(1_000, viewModel.Rows);
        Assert.AreEqual(1_000, viewModel.TotalCount);
        Assert.AreEqual(1_000, viewModel.VisibleCount);
        Assert.AreEqual("Showing 1000 of 1000", viewModel.ShowingCountText);
    }

    private static object ConvertState(ProjectStateMessageConverter converter, ProjectState state)
    {
        return converter.Convert(state, typeof(string), null, CultureInfo.InvariantCulture);
    }

    private static ProjectCatalogSnapshot CreateSnapshot(params UnrealProject[] projects)
    {
        var catalog = new ProjectCatalog();
        foreach (var project in projects)
        {
            catalog.Upsert(project);
        }

        return catalog.GetSnapshot();
    }

    private static UnrealProject CreateProject(
        string name,
        string path,
        string? engineAssociation = null,
        string? engineDisplayVersion = null,
        ProjectType projectType = ProjectType.Blueprint,
        DateTimeOffset? lastModified = null,
        DateTimeOffset? lastLaunched = null,
        bool isFavorite = false,
        ProjectState projectState = ProjectState.Available,
        EngineResolutionState engineState = EngineResolutionState.Unknown)
    {
        return new UnrealProject(
            name,
            new ProjectPath(path),
            engineAssociation,
            engineDisplayVersion,
            projectType,
            lastModified ?? new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero),
            lastLaunched,
            isFavorite,
            projectState,
            engineState);
    }
}
