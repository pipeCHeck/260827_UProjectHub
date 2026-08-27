using System.Windows;
using UProjectHub.App.ViewModels;

namespace UProjectHub.App.Views;

public partial class ProjectInformationWindow : Window
{
    public ProjectInformationWindow(ProjectInformationViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }
}
