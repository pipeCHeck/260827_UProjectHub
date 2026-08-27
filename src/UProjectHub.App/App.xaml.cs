using System.Windows;
using UProjectHub.App.Composition;

namespace UProjectHub.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var bootstrapper = new AppBootstrapper();
        var viewModel = bootstrapper.Build();
        var window = new MainWindow
        {
            DataContext = viewModel,
        };

        MainWindow = window;
        window.Show();
    }
}
