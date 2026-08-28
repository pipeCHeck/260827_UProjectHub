using System.Windows;
using UProjectHub.App.ViewModels;

namespace UProjectHub.App.Views;

public partial class SettingsWindow : Window
{
    private bool _isLoaded;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        await ((SettingsViewModel)DataContext).LoadAsync();
    }
}
