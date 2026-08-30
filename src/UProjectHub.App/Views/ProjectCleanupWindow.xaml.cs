using System.ComponentModel;
using System.Windows;
using UProjectHub.App.ViewModels;

namespace UProjectHub.App.Views;

public partial class ProjectCleanupWindow : Window
{
    public ProjectCleanupWindow(ProjectCleanupViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs eventArgs)
    {
        if (DataContext is ProjectCleanupViewModel { IsCleaning: true })
        {
            eventArgs.Cancel = true;
        }

        base.OnClosing(eventArgs);
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is ProjectCleanupViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
}
