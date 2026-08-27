using UProjectHub.App.ViewModels;
using UProjectHub.Core.Filtering;
using UProjectHub.Core.Searching;
using UProjectHub.Core.Sorting;
using UProjectHub.Core.Time;

namespace UProjectHub.App.Composition;

public sealed class AppBootstrapper
{
    public MainViewModel Build()
    {
        var statusBar = new StatusBarViewModel();
        var projectList = new ProjectListViewModel();
        var clock = new SystemClock();
        var searchService = new ProjectSearchService(clock);
        var searchFilter = new SearchFilterViewModel(
            projectList,
            new ProjectQueryParser(),
            new ProjectFilterService(searchService),
            new ProjectSortService());

        return new MainViewModel(
            statusBar,
            projectList: projectList,
            searchFilter: searchFilter);
    }
}
