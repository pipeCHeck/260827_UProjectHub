using System.ComponentModel;
using System.Windows;
using UProjectHub.App.ViewModels;

namespace UProjectHub.App.Views;

public partial class GenerateProjectFilesWindow : Window
{
    public GenerateProjectFilesWindow(GenerateProjectFilesViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs eventArgs)
    {
        if (DataContext is GenerateProjectFilesViewModel { IsRunning: true } viewModel)
        {
            viewModel.CancelCommand.Execute(null);
            eventArgs.Cancel = true;
        }

        base.OnClosing(eventArgs);
    }
}
