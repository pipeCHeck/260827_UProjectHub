using UProjectHub.App.ViewModels;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Filtering;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Searching;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;
using UProjectHub.Core.Tests.Time;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class SearchFilterViewModelTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 3, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void EmptySnapshot_UsesDefaultLastModifiedDescendingSort()
    {
        var fixture = CreateFixture();

        fixture.ViewModel.SetSnapshot(CreateSnapshot());

        Assert.IsEmpty(fixture.ProjectList.Rows);
        Assert.AreEqual(new ProjectSortDefinition(), fixture.ViewModel.ActiveSort);
        Assert.IsEmpty(fixture.ViewModel.EngineOptions);
        Assert.IsFalse(fixture.ViewModel.HasActiveSearchOrFilters);
    }

    [TestMethod]
    public void Snapshot_IsImmediatelySortedByLastModifiedDescending()
    {
        var fixture = CreateFixture();
        var older = CreateProject("Older", @"D:\Projects\Older\Older.uproject", lastModified: Now.AddDays(-2));
        var newer = CreateProject("Newer", @"D:\Projects\Newer\Newer.uproject", lastModified: Now.AddHours(-1));
        var snapshot = CreateSnapshot(older, newer);

        fixture.ViewModel.SetSnapshot(snapshot);

        CollectionAssert.AreEqual(new[] { "Newer", "Older" }, VisibleNames(fixture));
        CollectionAssert.AreEqual(new[] { "Older", "Newer" }, snapshot.Projects.Select(project => project.Name).ToArray());
    }

    [TestMethod]
    [DataRow("Alpha", "Alpha")]
    [DataRow(@"D:\Game Academy", "Alpha")]
    [DataRow("5.10", "Beta")]
    [DataRow("c++", "Alpha")]
    [DataRow("version:5.8", "Alpha")]
    [DataRow("type:cpp", "Alpha")]
    [DataRow("type:bp", "Beta")]
    [DataRow("path:\"D:\\Game Academy\"", "Alpha")]
    [DataRow("modified:7d", "Alpha")]
    [DataRow("favorite:true", "Alpha")]
    [DataRow("version:5.8 Alpha", "Alpha")]
    public void SearchText_UsesCorePlainAndStructuredQuerySemantics(string query, string expectedName)
    {
        var fixture = CreateFixture();
        fixture.ViewModel.SetSnapshot(CreateSearchSnapshot());

        fixture.ViewModel.SearchText = query;

        CollectionAssert.AreEqual(new[] { expectedName }, VisibleNames(fixture));
    }

    [TestMethod]
    public void UnknownAndMalformedStructuredTokens_UseCorePlainTextFallback()
    {
        var fixture = CreateFixture();
        var unknown = CreateProject("foo:bar", @"D:\Projects\Unknown\Unknown.uproject");
        var malformed = CreateProject("type:java", @"D:\Projects\Malformed\Malformed.uproject");
        fixture.ViewModel.SetSnapshot(CreateSnapshot(unknown, malformed));

        fixture.ViewModel.SearchText = "foo:bar";
        CollectionAssert.AreEqual(new[] { "foo:bar" }, VisibleNames(fixture));

        fixture.ViewModel.SearchText = "type:java";
        CollectionAssert.AreEqual(new[] { "type:java" }, VisibleNames(fixture));
    }

    [TestMethod]
    public void VisibleFiltersAndQuery_CombineWithAndSemanticsAndPreserveTotal()
    {
        var fixture = CreateFixture();
        var cppFavorite = CreateProject(
            "CppFavorite",
            @"D:\Projects\CppFavorite\CppFavorite.uproject",
            engineDisplayVersion: "5.8",
            projectType: ProjectType.Cpp,
            isFavorite: true);
        var cppOtherEngine = CreateProject(
            "CppOther",
            @"D:\Projects\CppOther\CppOther.uproject",
            engineDisplayVersion: "5.10",
            projectType: ProjectType.Cpp,
            isFavorite: true);
        var blueprintFavorite = CreateProject(
            "BlueprintFavorite",
            @"D:\Projects\BlueprintFavorite\BlueprintFavorite.uproject",
            engineDisplayVersion: "5.8",
            isFavorite: true);
        var snapshot = CreateSnapshot(cppFavorite, cppOtherEngine, blueprintFavorite);
        fixture.ViewModel.SetSnapshot(snapshot);

        fixture.ViewModel.SearchText = "Favorite";
        fixture.ViewModel.SelectedEngine = "5.8";
        fixture.ViewModel.SelectedProjectType = ProjectType.Cpp;
        fixture.ViewModel.FavoritesOnly = true;

        CollectionAssert.AreEqual(new[] { "CppFavorite" }, VisibleNames(fixture));
        Assert.AreEqual(3, fixture.ProjectList.TotalCount);
        Assert.AreEqual(1, fixture.ProjectList.VisibleCount);
        Assert.AreEqual("Showing 1 of 3", fixture.ProjectList.ShowingCountText);
        Assert.IsTrue(fixture.ViewModel.HasActiveSearchOrFilters);
    }

    [TestMethod]
    public void EachVisibleFilter_IndependentlyNarrowsTheRawSnapshot()
    {
        var fixture = CreateFixture();
        fixture.ViewModel.SetSnapshot(CreateSnapshot(
            CreateProject(
                "CppFavorite",
                @"D:\Projects\CppFavorite\CppFavorite.uproject",
                engineDisplayVersion: "5.8",
                projectType: ProjectType.Cpp,
                isFavorite: true),
            CreateProject(
                "Blueprint",
                @"D:\Projects\Blueprint\Blueprint.uproject",
                engineDisplayVersion: "5.10")));

        fixture.ViewModel.SelectedEngine = "5.8";
        CollectionAssert.AreEqual(new[] { "CppFavorite" }, VisibleNames(fixture));

        fixture.ViewModel.SelectedEngine = null;
        fixture.ViewModel.SelectedProjectType = ProjectType.Blueprint;
        CollectionAssert.AreEqual(new[] { "Blueprint" }, VisibleNames(fixture));

        fixture.ViewModel.SelectedProjectType = null;
        fixture.ViewModel.FavoritesOnly = true;
        CollectionAssert.AreEqual(new[] { "CppFavorite" }, VisibleNames(fixture));
    }

    [TestMethod]
    public void EngineOptions_UseRawSnapshotMetadataWithSemanticOrderingAndCaseInsensitiveDedupe()
    {
        var fixture = CreateFixture();
        fixture.ViewModel.SetSnapshot(CreateSnapshot(
            CreateProject("Nine", @"D:\Projects\Nine\Nine.uproject", engineDisplayVersion: "5.9"),
            CreateProject("Ten", @"D:\Projects\Ten\Ten.uproject", engineDisplayVersion: "5.10"),
            CreateProject("Duplicate", @"D:\Projects\Duplicate\Duplicate.uproject", engineDisplayVersion: "5.9"),
            CreateProject("Guid", @"D:\Projects\Guid\Guid.uproject", engineAssociation: "{ABCDEFAB-1234-5678-90AB-ABCDEFABCDEF}"),
            CreateProject("Unknown", @"D:\Projects\Unknown\Unknown.uproject")));

        CollectionAssert.AreEqual(
            new[] { "5.9", "5.10", "{ABCDEFAB-1234-5678-90AB-ABCDEFABCDEF}" },
            fixture.ViewModel.EngineOptions.ToArray());

        fixture.ViewModel.SearchText = "Nine";

        CollectionAssert.AreEqual(
            new[] { "5.9", "5.10", "{ABCDEFAB-1234-5678-90AB-ABCDEFABCDEF}" },
            fixture.ViewModel.EngineOptions.ToArray());
    }

    [TestMethod]
    public void PersistedState_IsAppliedAndValidEngineFilterSurvivesFirstSnapshot()
    {
        var fixture = CreateFixture();
        var settings = new AppSettings
        {
            ActiveSort = new ProjectSortDefinition(ProjectSortColumn.Name, SortDirection.Descending),
            VisibleFilters = new VisibleFilterState("5.8", ProjectType.Cpp, true),
        };

        fixture.ViewModel.ApplySettings(settings);

        Assert.AreEqual("5.8", fixture.ViewModel.SelectedEngine);
        Assert.AreEqual(ProjectType.Cpp, fixture.ViewModel.SelectedProjectType);
        Assert.IsTrue(fixture.ViewModel.FavoritesOnly);
        Assert.AreEqual(settings.ActiveSort, fixture.ViewModel.ActiveSort);

        fixture.ViewModel.SetSnapshot(CreateSnapshot(
            CreateProject(
                "Zulu",
                @"D:\Projects\Zulu\Zulu.uproject",
                engineDisplayVersion: "5.8",
                projectType: ProjectType.Cpp,
                isFavorite: true),
            CreateProject(
                "Alpha",
                @"D:\Projects\Alpha\Alpha.uproject",
                engineDisplayVersion: "5.8",
                projectType: ProjectType.Cpp,
                isFavorite: true)));

        Assert.AreEqual("5.8", fixture.ViewModel.SelectedEngine);
        CollectionAssert.AreEqual(new[] { "Zulu", "Alpha" }, VisibleNames(fixture));
    }

    [TestMethod]
    public void PersistedEngineFilter_IsClearedOnlyAfterSnapshotProvesItIsStale()
    {
        var fixture = CreateFixture();
        fixture.ViewModel.ApplySettings(new AppSettings
        {
            VisibleFilters = new VisibleFilterState(Engine: "5.7"),
        });

        Assert.AreEqual("5.7", fixture.ViewModel.SelectedEngine);

        fixture.ViewModel.SetSnapshot(CreateSnapshot(
            CreateProject("Current", @"D:\Projects\Current\Current.uproject", engineDisplayVersion: "5.8")));

        Assert.IsNull(fixture.ViewModel.SelectedEngine);
        Assert.AreEqual(1, fixture.ProjectList.VisibleCount);
    }

    [TestMethod]
    public void ApplySettings_NotifiesActiveCriteriaAfterAtomicStateApplication()
    {
        var fixture = CreateFixture();
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        fixture.ViewModel.ApplySettings(new AppSettings
        {
            VisibleFilters = new VisibleFilterState(FavoritesOnly: true),
        });

        CollectionAssert.Contains(
            changedProperties,
            nameof(SearchFilterViewModel.HasActiveSearchOrFilters));
        Assert.IsTrue(fixture.ViewModel.HasActiveSearchOrFilters);
    }

    [TestMethod]
    public void Reset_ClearsSearchAndFiltersButPreservesSort()
    {
        var fixture = CreateFixture();
        fixture.ViewModel.SetSnapshot(CreateSearchSnapshot());
        fixture.ViewModel.RequestSort(ProjectSortColumn.Name);
        var expectedSort = fixture.ViewModel.ActiveSort;
        fixture.ViewModel.SearchText = "Alpha";
        fixture.ViewModel.SelectedEngine = "5.8";
        fixture.ViewModel.SelectedProjectType = ProjectType.Cpp;
        fixture.ViewModel.FavoritesOnly = true;

        fixture.ViewModel.ResetCommand.Execute(null);

        Assert.AreEqual(string.Empty, fixture.ViewModel.SearchText);
        Assert.IsNull(fixture.ViewModel.SelectedEngine);
        Assert.IsNull(fixture.ViewModel.SelectedProjectType);
        Assert.IsFalse(fixture.ViewModel.FavoritesOnly);
        Assert.IsFalse(fixture.ViewModel.HasActiveSearchOrFilters);
        Assert.AreEqual(expectedSort, fixture.ViewModel.ActiveSort);
        Assert.AreEqual(fixture.ProjectList.TotalCount, fixture.ProjectList.VisibleCount);
    }

    [TestMethod]
    public void ClearSearchCommand_ClearsOnlySearchText()
    {
        var fixture = CreateFixture();
        fixture.ViewModel.SetSnapshot(CreateSearchSnapshot());
        fixture.ViewModel.SearchText = "Alpha";
        fixture.ViewModel.FavoritesOnly = true;

        fixture.ViewModel.ClearSearchCommand.Execute(null);

        Assert.AreEqual(string.Empty, fixture.ViewModel.SearchText);
        Assert.IsTrue(fixture.ViewModel.FavoritesOnly);
    }

    [TestMethod]
    public void RequestSort_TogglesSameColumnAndUsesColumnSpecificInitialDirections()
    {
        var fixture = CreateFixture();

        fixture.ViewModel.RequestSort(ProjectSortColumn.LastModified);
        Assert.AreEqual(
            new ProjectSortDefinition(ProjectSortColumn.LastModified, SortDirection.Ascending),
            fixture.ViewModel.ActiveSort);

        fixture.ViewModel.RequestSort(ProjectSortColumn.Name);
        Assert.AreEqual(
            new ProjectSortDefinition(ProjectSortColumn.Name, SortDirection.Ascending),
            fixture.ViewModel.ActiveSort);

        fixture.ViewModel.RequestSort(ProjectSortColumn.Name);
        Assert.AreEqual(
            new ProjectSortDefinition(ProjectSortColumn.Name, SortDirection.Descending),
            fixture.ViewModel.ActiveSort);

        fixture.ViewModel.RequestSort(ProjectSortColumn.LastLaunched);
        Assert.AreEqual(
            new ProjectSortDefinition(ProjectSortColumn.LastLaunched, SortDirection.Descending),
            fixture.ViewModel.ActiveSort);
    }

    [TestMethod]
    public void EngineSort_UsesSemanticVersionOrderingInBothDirections()
    {
        var fixture = CreateFixture();
        fixture.ViewModel.SetSnapshot(CreateSnapshot(
            CreateProject("Nine", @"D:\Projects\Nine\Nine.uproject", engineDisplayVersion: "5.9"),
            CreateProject("Ten", @"D:\Projects\Ten\Ten.uproject", engineDisplayVersion: "5.10")));

        fixture.ViewModel.RequestSort(ProjectSortColumn.EngineVersion);
        CollectionAssert.AreEqual(new[] { "Nine", "Ten" }, VisibleNames(fixture));

        fixture.ViewModel.RequestSort(ProjectSortColumn.EngineVersion);
        CollectionAssert.AreEqual(new[] { "Ten", "Nine" }, VisibleNames(fixture));
    }

    [TestMethod]
    public void NameTypeAndDateSorts_ApplyBothDirectionsThroughCoreSortService()
    {
        var fixture = CreateFixture();
        fixture.ViewModel.SetSnapshot(CreateSnapshot(
            CreateProject(
                "Alpha",
                @"D:\Projects\Alpha\Alpha.uproject",
                projectType: ProjectType.Blueprint,
                lastModified: Now.AddDays(-2),
                lastLaunched: Now.AddHours(-1)),
            CreateProject(
                "Zulu",
                @"D:\Projects\Zulu\Zulu.uproject",
                projectType: ProjectType.Cpp,
                lastModified: Now.AddDays(-1),
                lastLaunched: Now.AddHours(-2))));

        fixture.ViewModel.RequestSort(ProjectSortColumn.Name);
        CollectionAssert.AreEqual(new[] { "Alpha", "Zulu" }, VisibleNames(fixture));
        fixture.ViewModel.RequestSort(ProjectSortColumn.Name);
        CollectionAssert.AreEqual(new[] { "Zulu", "Alpha" }, VisibleNames(fixture));

        fixture.ViewModel.RequestSort(ProjectSortColumn.ProjectType);
        CollectionAssert.AreEqual(new[] { "Zulu", "Alpha" }, VisibleNames(fixture));
        fixture.ViewModel.RequestSort(ProjectSortColumn.ProjectType);
        CollectionAssert.AreEqual(new[] { "Alpha", "Zulu" }, VisibleNames(fixture));

        fixture.ViewModel.RequestSort(ProjectSortColumn.LastModified);
        CollectionAssert.AreEqual(new[] { "Zulu", "Alpha" }, VisibleNames(fixture));
        fixture.ViewModel.RequestSort(ProjectSortColumn.LastModified);
        CollectionAssert.AreEqual(new[] { "Alpha", "Zulu" }, VisibleNames(fixture));

        fixture.ViewModel.RequestSort(ProjectSortColumn.LastLaunched);
        CollectionAssert.AreEqual(new[] { "Alpha", "Zulu" }, VisibleNames(fixture));
        fixture.ViewModel.RequestSort(ProjectSortColumn.LastLaunched);
        CollectionAssert.AreEqual(new[] { "Zulu", "Alpha" }, VisibleNames(fixture));
    }

    [TestMethod]
    public void SortMaintainsNameAscendingSecondaryOrderAndDoesNotMutateSnapshot()
    {
        var fixture = CreateFixture();
        var beta = CreateProject("Beta", @"D:\Projects\Beta\Beta.uproject", lastModified: Now);
        var alpha = CreateProject("Alpha", @"D:\Projects\Alpha\Alpha.uproject", lastModified: Now);
        var snapshot = CreateSnapshot(beta, alpha);
        var originalOrder = snapshot.Projects.Select(project => project.Name).ToArray();
        fixture.ViewModel.SetSnapshot(snapshot);

        fixture.ViewModel.SearchText = "a";

        CollectionAssert.AreEqual(new[] { "Alpha", "Beta" }, VisibleNames(fixture));
        CollectionAssert.AreEqual(originalOrder, snapshot.Projects.Select(project => project.Name).ToArray());
    }

    [TestMethod]
    public void MainViewModelSetProjects_UsesOneRawSnapshotForHeaderOptionsAndVisibleRows()
    {
        var fixture = CreateFixture();
        var main = new MainViewModel(
            new StatusBarViewModel(),
            projectList: fixture.ProjectList,
            searchFilter: fixture.ViewModel);
        var snapshot = CreateSearchSnapshot();
        fixture.ViewModel.SearchText = "Alpha";

        main.SetProjects(snapshot);

        Assert.AreEqual(snapshot.Projects.Count, main.ProjectCount);
        Assert.AreEqual(snapshot.Projects.Count, main.ProjectList.TotalCount);
        Assert.AreEqual(1, main.ProjectList.VisibleCount);
        CollectionAssert.Contains(main.SearchFilter!.EngineOptions.ToArray(), "5.8");
        CollectionAssert.Contains(main.SearchFilter.EngineOptions.ToArray(), "5.10");
    }

    private static SearchFixture CreateFixture()
    {
        var projectList = new ProjectListViewModel();
        var searchService = new ProjectSearchService(new FakeClock(Now));
        var viewModel = new SearchFilterViewModel(
            projectList,
            new ProjectQueryParser(),
            new ProjectFilterService(searchService),
            new ProjectSortService());

        return new SearchFixture(viewModel, projectList);
    }

    private static ProjectCatalogSnapshot CreateSearchSnapshot()
    {
        return CreateSnapshot(
            CreateProject(
                "Alpha",
                @"D:\Game Academy\Alpha\Alpha.uproject",
                engineDisplayVersion: "5.8",
                projectType: ProjectType.Cpp,
                lastModified: Now.AddDays(-1),
                isFavorite: true),
            CreateProject(
                "Beta",
                @"D:\Study\Beta\Beta.uproject",
                engineDisplayVersion: "5.10",
                lastModified: Now.AddDays(-10)));
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
        bool isFavorite = false)
    {
        return new UnrealProject(
            name,
            new ProjectPath(path),
            engineAssociation,
            engineDisplayVersion,
            projectType,
            lastModified ?? Now,
            lastLaunched,
            isFavorite,
            ProjectState.Available,
            EngineResolutionState.Unknown);
    }

    private static string[] VisibleNames(SearchFixture fixture)
    {
        return fixture.ProjectList.Rows.Select(row => row.Name).ToArray();
    }

    private sealed record SearchFixture(
        SearchFilterViewModel ViewModel,
        ProjectListViewModel ProjectList);
}
