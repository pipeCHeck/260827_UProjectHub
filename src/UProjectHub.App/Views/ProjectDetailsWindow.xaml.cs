using System.Windows;
using UProjectHub.App.ViewModels;

namespace UProjectHub.App.Views;

public partial class ProjectDetailsWindow : Window
{
    public ProjectDetailsWindow(ProjectDetailsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }
}
