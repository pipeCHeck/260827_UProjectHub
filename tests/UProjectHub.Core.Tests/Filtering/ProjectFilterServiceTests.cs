using UProjectHub.Core.Filtering;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Searching;
using UProjectHub.Core.Tests.Time;

namespace UProjectHub.Core.Tests.Filtering;

[TestClass]
public sealed class ProjectFilterServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly ProjectQueryParser _parser = new();
    private readonly ProjectFilterService _service = new(
        new ProjectSearchService(new FakeClock(Now)));

    [TestMethod]
    public void InactiveVisibleFiltersMatchEveryProject()
    {
        Assert.IsTrue(_service.Matches(CreateProject(), new ProjectFilter()));
    }

    [TestMethod]
    public void EngineFilterUsesProjectEngineMetadataCaseInsensitively()
    {
        var project = CreateProject(
            engineAssociation: "5.8",
            engineDisplayVersion: "UE 5.8.2");

        Assert.IsTrue(
            _service.Matches(project, new ProjectFilter(Engine: "ue 5.8.2")));
        Assert.IsTrue(
            _service.Matches(project, new ProjectFilter(Engine: "5.8")));
        Assert.IsFalse(
            _service.Matches(project, new ProjectFilter(Engine: "5.9")));
    }

    [TestMethod]
    public void ProjectTypeFilterMatchesExactProjectType()
    {
        var cppProject = CreateProject(projectType: ProjectType.Cpp);
        var blueprintProject = CreateProject(projectType: ProjectType.Blueprint);
        var filter = new ProjectFilter(ProjectType: ProjectType.Cpp);

        Assert.IsTrue(_service.Matches(cppProject, filter));
        Assert.IsFalse(_service.Matches(blueprintProject, filter));
    }

    [TestMethod]
    public void FavoritesOnlyFilterRequiresFavoriteProject()
    {
        var filter = new ProjectFilter(FavoritesOnly: true);

        Assert.IsTrue(_service.Matches(CreateProject(isFavorite: true), filter));
        Assert.IsFalse(_service.Matches(CreateProject(isFavorite: false), filter));
    }

    [TestMethod]
    public void TagFilterUsesCaseInsensitiveExactTagIdentity()
    {
        var project = CreateProject() with
        {
            Tags = ["Team Project", "Prototype"],
        };

        Assert.IsTrue(
            _service.Matches(project, new ProjectFilter(Tag: "team project")));
        Assert.IsFalse(
            _service.Matches(project, new ProjectFilter(Tag: "Team")));
    }

    [TestMethod]
    public void ActiveVisibleFiltersUseAndSemantics()
    {
        var matching = CreateProject(
            engineDisplayVersion: "5.8.2",
            projectType: ProjectType.Cpp,
            isFavorite: true);
        var filter = new ProjectFilter(
            Engine: "5.8.2",
            ProjectType: ProjectType.Cpp,
            FavoritesOnly: true);

        Assert.IsTrue(_service.Matches(matching, filter));
        Assert.IsFalse(_service.Matches(matching with { IsFavorite = false }, filter));
        Assert.IsFalse(
            _service.Matches(matching with { ProjectType = ProjectType.Blueprint }, filter));
        Assert.IsFalse(
            _service.Matches(matching with { EngineDisplayVersion = "5.9" }, filter));
    }

    [TestMethod]
    public void QueryAndVisibleFiltersUseAndSemantics()
    {
        var matching = CreateProject(name: "Academy Game", isFavorite: true);
        var filter = new ProjectFilter(FavoritesOnly: true);

        Assert.IsTrue(
            _service.Matches(matching, _parser.Parse("academy"), filter));
        Assert.IsFalse(
            _service.Matches(matching, _parser.Parse("prototype"), filter));
        Assert.IsFalse(
            _service.Matches(
                matching with { IsFavorite = false },
                _parser.Parse("academy"),
                filter));
    }

    private static UnrealProject CreateProject(
        string name = "Sample",
        string? engineAssociation = "5.8",
        string? engineDisplayVersion = "5.8.2",
        ProjectType projectType = ProjectType.Cpp,
        bool isFavorite = false)
    {
        return new UnrealProject(
            Name: name,
            ProjectFilePath: new ProjectPath(
                @"D:\Workspace\Sample\Sample.uproject"),
            EngineAssociation: engineAssociation,
            EngineDisplayVersion: engineDisplayVersion,
            ProjectType: projectType,
            LastModified: Now.AddDays(-1),
            LastLaunched: null,
            IsFavorite: isFavorite,
            ProjectState: ProjectState.Available,
            EngineState: EngineResolutionState.Resolved);
    }
}
