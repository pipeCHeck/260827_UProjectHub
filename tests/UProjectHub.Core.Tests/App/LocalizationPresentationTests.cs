using System.Windows;
using UProjectHub.App.Converters;
using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Filtering;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Searching;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;
using UProjectHub.Core.Time;

namespace UProjectHub.Core.Tests.App;

[TestClass]
[DoNotParallelize]
public sealed class LocalizationPresentationTests
{
    [STATestMethod]
    public void RuntimeLanguageSwitchUpdatesPresentationCountsAndKeepsProjectDataUntouched()
    {
        var localization = CreateLocalization();
        localization.ApplyLanguage(AppLanguage.English);
        var list = new ProjectListViewModel(localization: localization);
        var main = new MainViewModel(
            new StatusBarViewModel(localization),
            projectList: list,
            localization: localization);
        var project = CreateProject();
        var snapshot = Snapshot(project);

        main.SetProjects(snapshot);
        Assert.AreEqual("UProject Hub", main.Title);
        Assert.AreEqual("1 projects", main.ProjectCountText);
        Assert.AreEqual("Showing 1 of 1", list.ShowingCountText);

        localization.ApplyLanguage(AppLanguage.Korean);

        Assert.AreEqual("UProject Hub", main.Title);
        Assert.AreEqual("프로젝트 1개", main.ProjectCountText);
        Assert.AreEqual("전체 1개 중 1개 표시", list.ShowingCountText);
        Assert.AreEqual("Game", list.Rows[0].Name);
        Assert.AreEqual(@"C:\Projects\Game", list.Rows[0].ProjectDirectory);
        Assert.AreEqual("5.8", list.Rows[0].EngineDisplay);
    }

    [STATestMethod]
    public void RuntimeLanguageSwitchRelabelsAllWithoutChangingFilterValueOrSearchGrammar()
    {
        var localization = CreateLocalization();
        localization.ApplyLanguage(AppLanguage.English);
        var list = new ProjectListViewModel(localization: localization);
        var search = new SearchFilterViewModel(
            list,
            new ProjectQueryParser(),
            new ProjectFilterService(new ProjectSearchService(new FixedClock())),
            new ProjectSortService(),
            localization);
        search.SetSnapshot(Snapshot(CreateProject()));
        search.SearchText = "version:5.8";

        Assert.AreEqual("All", search.SelectedEngineFilterOption.Label);
        Assert.IsNull(search.SelectedEngineFilterOption.Value);

        localization.ApplyLanguage(AppLanguage.Korean);

        Assert.AreEqual("전체", search.SelectedEngineFilterOption.Label);
        Assert.IsNull(search.SelectedEngineFilterOption.Value);
        Assert.AreEqual("version:5.8", search.SearchText);
        CollectionAssert.Contains(search.EngineOptions.ToArray(), "5.8");
        Assert.AreEqual("Game", list.Rows[0].Name);
    }

    [STATestMethod]
    public void ProblemStateMessagesUseTheCurrentLanguage()
    {
        var localization = CreateLocalization();
        localization.ApplyLanguage(AppLanguage.English);

        Assert.AreEqual(
            "Missing",
            ProjectStateMessageConverter.GetMessage(ProjectState.Missing, localization));

        localization.ApplyLanguage(AppLanguage.Korean);

        Assert.AreEqual(
            "찾을 수 없음",
            ProjectStateMessageConverter.GetMessage(ProjectState.Missing, localization));
        Assert.AreEqual(
            "프로젝트 정보를 읽을 수 없음",
            ProjectStateMessageConverter.GetMessage(ProjectState.Broken, localization));
    }

    private static LocalizationService CreateLocalization() =>
        new(
            new ResourceDictionary(),
            source => (ResourceDictionary)Application.LoadComponent(
                new Uri(
                    source.OriginalString.EndsWith("Strings.ko.xaml", StringComparison.OrdinalIgnoreCase)
                        ? "/UProjectHub.App;component/Localization/Strings.ko.xaml"
                        : "/UProjectHub.App;component/Localization/Strings.en.xaml",
                    UriKind.Relative)));

    private static UnrealProject CreateProject() => new(
        "Game",
        new ProjectPath(@"C:\Projects\Game\Game.uproject"),
        "5.8",
        "5.8",
        ProjectType.Cpp,
        new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero),
        null,
        false,
        ProjectState.Available,
        EngineResolutionState.Resolved);

    private static ProjectCatalogSnapshot Snapshot(params UnrealProject[] projects)
    {
        var catalog = new ProjectCatalog();
        foreach (var project in projects)
        {
            catalog.Upsert(project);
        }

        return catalog.GetSnapshot();
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow =>
            new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
    }
}
