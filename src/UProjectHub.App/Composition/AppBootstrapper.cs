using UProjectHub.App.ViewModels;

namespace UProjectHub.App.Composition;

public sealed class AppBootstrapper
{
    public MainViewModel Build()
    {
        var statusBar = new StatusBarViewModel();
        return new MainViewModel(statusBar);
    }
}
