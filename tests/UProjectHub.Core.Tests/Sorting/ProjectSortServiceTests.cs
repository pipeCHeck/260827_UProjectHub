using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Sorting;

namespace UProjectHub.Core.Tests.Sorting;

[TestClass]
public sealed class ProjectSortServiceTests
{
    private static readonly DateTimeOffset ReferenceTime =
        new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly ProjectSortService _service = new();

    [TestMethod]
    public void NameSortSupportsAscendingAndDescending()
    {
        var projects = new[]
        {
            CreateProject("Charlie"),
            CreateProject("alpha"),
            CreateProject("Bravo"),
        };

        AssertNames(
            ["alpha", "Bravo", "Charlie"],
            Sort(projects, ProjectSortColumn.Name, SortDirection.Ascending));
        AssertNames(
            ["Charlie", "Bravo", "alpha"],
            Sort(projects, ProjectSortColumn.Name, SortDirection.Descending));
    }

    [TestMethod]
    public void EngineVersionSortUsesSemanticComparerInBothDirections()
    {
        var projects = new[]
        {
            CreateProject("Ten", engineDisplayVersion: "5.10"),
            CreateProject("Unknown", engineDisplayVersion: null),
            CreateProject("Custom", engineDisplayVersion: "custom-build"),
            CreateProject("Nine", engineDisplayVersion: "5.9"),
        };

        AssertNames(
            ["Nine", "Ten", "Custom", "Unknown"],
            Sort(projects, ProjectSortColumn.EngineVersion, SortDirection.Ascending));
        AssertNames(
            ["Unknown", "Custom", "Ten", "Nine"],
            Sort(projects, ProjectSortColumn.EngineVersion, SortDirection.Descending));
    }

    [TestMethod]
    public void EngineVersionSortDoesNotFallBackToAssociation()
    {
        var projects = new[]
        {
            CreateProject(
                "AssociationOnly",
                engineDisplayVersion: null,
                engineAssociation: "5.0"),
            CreateProject(
                "Displayed",
                engineDisplayVersion: "5.9",
                engineAssociation: "99.0"),
        };

        AssertNames(
            ["Displayed", "AssociationOnly"],
            Sort(projects, ProjectSortColumn.EngineVersion, SortDirection.Ascending));
    }

    [TestMethod]
    public void ProjectTypeSortSupportsBothDirections()
    {
        var projects = new[]
        {
            CreateProject("Bravo", projectType: ProjectType.Blueprint),
            CreateProject("Charlie", projectType: ProjectType.Cpp),
            CreateProject("Alpha", projectType: ProjectType.Blueprint),
            CreateProject("Beta", projectType: ProjectType.Cpp),
        };

        AssertNames(
            ["Beta", "Charlie", "Alpha", "Bravo"],
            Sort(projects, ProjectSortColumn.ProjectType, SortDirection.Ascending));
        AssertNames(
            ["Alpha", "Bravo", "Beta", "Charlie"],
            Sort(projects, ProjectSortColumn.ProjectType, SortDirection.Descending));
    }

    [TestMethod]
    public void LastModifiedSortSupportsBothDirections()
    {
        var projects = new[]
        {
            CreateProject("Middle", lastModified: ReferenceTime.AddDays(-2)),
            CreateProject("Newest", lastModified: ReferenceTime.AddDays(-1)),
            CreateProject("Oldest", lastModified: ReferenceTime.AddDays(-3)),
        };

        AssertNames(
            ["Oldest", "Middle", "Newest"],
            Sort(projects, ProjectSortColumn.LastModified, SortDirection.Ascending));
        AssertNames(
            ["Newest", "Middle", "Oldest"],
            Sort(projects, ProjectSortColumn.LastModified, SortDirection.Descending));
    }

    [TestMethod]
    public void LastLaunchedSortSupportsBothDirectionsWithNullLast()
    {
        var projects = new[]
        {
            CreateProject("Never", lastLaunched: null),
            CreateProject("Newest", lastLaunched: ReferenceTime.AddHours(-1)),
            CreateProject("Oldest", lastLaunched: ReferenceTime.AddHours(-3)),
        };

        AssertNames(
            ["Oldest", "Newest", "Never"],
            Sort(projects, ProjectSortColumn.LastLaunched, SortDirection.Ascending));
        AssertNames(
            ["Newest", "Oldest", "Never"],
            Sort(projects, ProjectSortColumn.LastLaunched, SortDirection.Descending));
    }

    [TestMethod]
    public void EqualPrimaryValuesUseNameAscendingForBothDirections()
    {
        var projects = new[]
        {
            CreateProject("Charlie", lastModified: ReferenceTime),
            CreateProject("alpha", lastModified: ReferenceTime),
            CreateProject("Bravo", lastModified: ReferenceTime),
        };

        AssertNames(
            ["alpha", "Bravo", "Charlie"],
            Sort(projects, ProjectSortColumn.LastModified, SortDirection.Ascending));
        AssertNames(
            ["alpha", "Bravo", "Charlie"],
            Sort(projects, ProjectSortColumn.LastModified, SortDirection.Descending));
    }

    [TestMethod]
    public void DefaultDefinitionSortsLastModifiedDescending()
    {
        var definition = new ProjectSortDefinition();
        var projects = new[]
        {
            CreateProject("Older", lastModified: ReferenceTime.AddDays(-2)),
            CreateProject("Newer", lastModified: ReferenceTime.AddDays(-1)),
        };

        Assert.AreEqual(ProjectSortColumn.LastModified, definition.Column);
        Assert.AreEqual(SortDirection.Descending, definition.Direction);
        AssertNames(["Newer", "Older"], _service.Sort(projects, definition));
    }

    [TestMethod]
    public void SortReturnsANewResultWithoutChangingInputOrder()
    {
        var projects = new[]
        {
            CreateProject("Charlie"),
            CreateProject("Alpha"),
        };

        var result = Sort(
            projects,
            ProjectSortColumn.Name,
            SortDirection.Ascending);

        AssertNames(["Charlie", "Alpha"], projects);
        AssertNames(["Alpha", "Charlie"], result);
    }

    private IReadOnlyList<UnrealProject> Sort(
        IEnumerable<UnrealProject> projects,
        ProjectSortColumn column,
        SortDirection direction)
    {
        return _service.Sort(
            projects,
            new ProjectSortDefinition(column, direction));
    }

    private static void AssertNames(
        string[] expected,
        IEnumerable<UnrealProject> actual)
    {
        CollectionAssert.AreEqual(
            expected,
            actual.Select(project => project.Name).ToArray());
    }

    private static UnrealProject CreateProject(
        string name,
        string? engineDisplayVersion = "5.8",
        string? engineAssociation = "5.8",
        ProjectType projectType = ProjectType.Cpp,
        DateTimeOffset? lastModified = null,
        DateTimeOffset? lastLaunched = null)
    {
        return new UnrealProject(
            Name: name,
            ProjectFilePath: new ProjectPath(
                $@"D:\Projects\{name}\{name}.uproject"),
            EngineAssociation: engineAssociation,
            EngineDisplayVersion: engineDisplayVersion,
            ProjectType: projectType,
            LastModified: lastModified ?? ReferenceTime,
            LastLaunched: lastLaunched,
            IsFavorite: false,
            ProjectState: ProjectState.Available,
            EngineState: EngineResolutionState.Resolved);
    }
}
