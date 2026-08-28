using System.Windows;
using System.Windows.Input;
using UProjectHub.App.Controls;
using UProjectHub.App.ViewModels;

namespace UProjectHub.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnProjectSortRequested(
        object sender,
        ProjectSortRequestedEventArgs eventArgs)
    {
        if (DataContext is MainViewModel { SearchFilter: { } searchFilter })
        {
            searchFilter.RequestSort(eventArgs.Column);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.F5
            && DataContext is MainViewModel mainViewModel
            && mainViewModel.RefreshCommand.CanExecute(null))
        {
            mainViewModel.RefreshCommand.Execute(null);
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.F
            && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ProjectSearchBox.FocusSearch();
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.Escape
            && DataContext is MainViewModel { SearchFilter: { } searchFilter }
            && !string.IsNullOrEmpty(searchFilter.SearchText))
        {
            searchFilter.ClearSearchCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }
}
