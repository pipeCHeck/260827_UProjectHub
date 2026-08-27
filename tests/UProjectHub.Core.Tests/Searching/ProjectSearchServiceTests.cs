using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Searching;
using UProjectHub.Core.Tests.Time;

namespace UProjectHub.Core.Tests.Searching;

[TestClass]
public sealed class ProjectSearchServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly ProjectQueryParser _parser = new();
    private readonly ProjectSearchService _service = new(new FakeClock(Now));

    [TestMethod]
    public void PlainTextMatchesProjectNameCaseInsensitively()
    {
        var project = CreateProject(name: "My Academy Game");

        Assert.IsTrue(_service.Matches(project, _parser.Parse("ACADEMY")));
        Assert.IsFalse(_service.Matches(project, _parser.Parse("prototype")));
    }

    [TestMethod]
    public void PlainTextMatchesProjectPathCaseInsensitively()
    {
        var project = CreateProject(
            projectPath: @"D:\Game Academy\Sample\Sample.uproject");

        Assert.IsTrue(_service.Matches(project, _parser.Parse("game")));
        Assert.IsTrue(_service.Matches(project, _parser.Parse("SAMPLE.UPROJECT")));
    }

    [TestMethod]
    public void PlainTextMatchesEngineDisplayVersionCaseInsensitively()
    {
        var project = CreateProject(engineDisplayVersion: "UE 5.8.2 Custom");

        Assert.IsTrue(_service.Matches(project, _parser.Parse("custom")));
        Assert.IsTrue(_service.Matches(project, _parser.Parse("5.8.2")));
    }

    [TestMethod]
    public void PlainTextMatchesCommonCppTypeNames()
    {
        var project = CreateProject(projectType: ProjectType.Cpp);

        Assert.IsTrue(_service.Matches(project, _parser.Parse("cpp")));
        Assert.IsTrue(_service.Matches(project, _parser.Parse("C++")));
        Assert.IsFalse(_service.Matches(project, _parser.Parse("blueprint")));
    }

    [TestMethod]
    public void PlainTextMatchesCommonBlueprintTypeNames()
    {
        var project = CreateProject(projectType: ProjectType.Blueprint);

        Assert.IsTrue(_service.Matches(project, _parser.Parse("blueprint")));
        Assert.IsTrue(_service.Matches(project, _parser.Parse("BP")));
        Assert.IsFalse(_service.Matches(project, _parser.Parse("cpp")));
    }

    [TestMethod]
    public void VersionTermMatchesProjectEngineMetadata()
    {
        var project = CreateProject(
            engineAssociation: "5.8",
            engineDisplayVersion: "5.8.2");

        Assert.IsTrue(_service.Matches(project, _parser.Parse("version:5.8")));
        Assert.IsTrue(_service.Matches(project, _parser.Parse("version:5.8.2")));
        Assert.IsFalse(_service.Matches(project, _parser.Parse("version:5.9")));
    }

    [TestMethod]
    public void ProjectTypeTermMatchesExactProjectType()
    {
        var cppProject = CreateProject(projectType: ProjectType.Cpp);
        var blueprintProject = CreateProject(projectType: ProjectType.Blueprint);
        var query = _parser.Parse("type:cpp");

        Assert.IsTrue(_service.Matches(cppProject, query));
        Assert.IsFalse(_service.Matches(blueprintProject, query));
    }

    [TestMethod]
    public void PathTermMatchesProjectPathCaseInsensitively()
    {
        var project = CreateProject(
            projectPath: @"D:\Game Academy\Sample\Sample.uproject");

        Assert.IsTrue(_service.Matches(project, _parser.Parse("path:academy")));
        Assert.IsFalse(_service.Matches(project, _parser.Parse("path:Archive")));
    }

    [TestMethod]
    public void FavoriteTermMatchesFavoriteState()
    {
        var favorite = CreateProject(isFavorite: true);
        var notFavorite = CreateProject(isFavorite: false);
        var query = _parser.Parse("favorite:true");

        Assert.IsTrue(_service.Matches(favorite, query));
        Assert.IsFalse(_service.Matches(notFavorite, query));
    }

    [TestMethod]
    public void MultipleStructuredTermsUseAndSemantics()
    {
        var matching = CreateProject(
            engineDisplayVersion: "5.8.2",
            projectType: ProjectType.Cpp);
        var wrongType = matching with { ProjectType = ProjectType.Blueprint };
        var query = _parser.Parse("version:5.8 type:cpp");

        Assert.IsTrue(_service.Matches(matching, query));
        Assert.IsFalse(_service.Matches(wrongType, query));
    }

    [TestMethod]
    public void StructuredAndPlainTextTermsUseAndSemantics()
    {
        var matching = CreateProject(
            name: "Academy Prototype",
            engineDisplayVersion: "5.8.2");
        var wrongName = matching with { Name = "Production Game" };
        var query = _parser.Parse("version:5.8 academy");

        Assert.IsTrue(_service.Matches(matching, query));
        Assert.IsFalse(_service.Matches(wrongName, query));
    }

    [TestMethod]
    public void FallbackTermsUseThePlainTextMatcher()
    {
        var matching = CreateProject(name: "foo:bar type:java sample");
        var notMatching = CreateProject(name: "Ordinary Sample");
        var query = _parser.Parse("foo:bar type:java");

        Assert.IsTrue(_service.Matches(matching, query));
        Assert.IsFalse(_service.Matches(notMatching, query));
    }

    [TestMethod]
    public void QuotedPathFromParserMatchesActualProjectPath()
    {
        var matching = CreateProject(
            projectPath: @"D:\Game Academy\Sample\Sample.uproject");
        var notMatching = CreateProject(
            projectPath: @"D:\Other\Sample\Sample.uproject");
        var query = _parser.Parse("path:\"D:\\Game Academy\"");

        Assert.IsTrue(_service.Matches(matching, query));
        Assert.IsFalse(_service.Matches(notMatching, query));
    }

    [TestMethod]
    public void ModifiedWindowIncludesTheExactRollingBoundary()
    {
        var boundary = CreateProject(lastModified: Now.AddHours(-168));
        var inside = CreateProject(lastModified: Now.AddHours(-168).AddTicks(1));
        var outside = CreateProject(lastModified: Now.AddHours(-168).AddTicks(-1));
        var query = _parser.Parse("modified:7d");

        Assert.IsTrue(_service.Matches(boundary, query));
        Assert.IsTrue(_service.Matches(inside, query));
        Assert.IsFalse(_service.Matches(outside, query));
    }

    [TestMethod]
    public void EmptyQueryMatchesEveryProject()
    {
        Assert.IsTrue(_service.Matches(CreateProject(), _parser.Parse(string.Empty)));
    }

    [TestMethod]
    public void NullableEngineMetadataDoesNotThrowOrMatchVersionText()
    {
        var project = CreateProject(
            engineAssociation: null,
            engineDisplayVersion: null);

        Assert.IsFalse(_service.Matches(project, _parser.Parse("version:5.8")));
        Assert.IsFalse(_service.Matches(project, _parser.Parse("5.8")));
    }

    private static UnrealProject CreateProject(
        string name = "Sample",
        string projectPath = @"D:\Workspace\Sample\Sample.uproject",
        string? engineAssociation = "5.8",
        string? engineDisplayVersion = "5.8.2",
        ProjectType projectType = ProjectType.Cpp,
        DateTimeOffset? lastModified = null,
        bool isFavorite = false)
    {
        return new UnrealProject(
            Name: name,
            ProjectFilePath: new ProjectPath(projectPath),
            EngineAssociation: engineAssociation,
            EngineDisplayVersion: engineDisplayVersion,
            ProjectType: projectType,
            LastModified: lastModified ?? Now.AddDays(-1),
            LastLaunched: null,
            IsFavorite: isFavorite,
            ProjectState: ProjectState.Available,
            EngineState: EngineResolutionState.Resolved);
    }
}
