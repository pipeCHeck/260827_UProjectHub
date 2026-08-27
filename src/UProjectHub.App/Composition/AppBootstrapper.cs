using UProjectHub.App.ViewModels;

namespace UProjectHub.App.Composition;

public sealed class AppBootstrapper
{
    public MainViewModel Build()
    {
        var statusBar = new StatusBarViewModel();
        var projectList = new ProjectListViewModel();
        return new MainViewModel(statusBar, projectList: projectList);
    }
}
