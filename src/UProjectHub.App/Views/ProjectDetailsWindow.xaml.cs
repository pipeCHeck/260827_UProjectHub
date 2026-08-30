using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UProjectHub.App.ViewModels;

namespace UProjectHub.App.Views;

public partial class ProjectDetailsWindow : Window
{
    public ProjectDetailsWindow(ProjectDetailsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        TagInputComboBox.AddHandler(
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnTagInputPreviewKeyDown),
            handledEventsToo: true);
    }

    private void OnTagInputPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (sender is not ComboBox comboBox
            || DataContext is not ProjectDetailsViewModel { Notes: { } notes })
        {
            return;
        }

        if (eventArgs.Key == Key.Escape && comboBox.IsDropDownOpen)
        {
            notes.IsSuggestionsOpen = false;
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key != Key.Enter)
        {
            return;
        }

        if (comboBox.SelectedItem is string suggestion)
        {
            notes.NewTag = suggestion;
        }

        notes.IsSuggestionsOpen = false;

        if (notes.AddTagCommand.CanExecute(null))
        {
            notes.AddTagCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }
}
