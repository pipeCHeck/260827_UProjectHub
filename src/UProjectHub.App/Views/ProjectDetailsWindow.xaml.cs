using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using UProjectHub.App.ViewModels;

namespace UProjectHub.App.Views;

public partial class ProjectDetailsWindow : Window
{
    private readonly ProjectNotesViewModel? _notes;
    private bool _allowUnsavedNoteClose;

    public ProjectDetailsWindow(ProjectDetailsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        _notes = viewModel.Notes;
        if (_notes is not null)
        {
            _notes.PropertyChanged += OnNotesPropertyChanged;
        }

        TagInputComboBox.AddHandler(
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnTagInputPreviewKeyDown),
            handledEventsToo: true);
    }

    protected override void OnClosing(CancelEventArgs eventArgs)
    {
        if (!_allowUnsavedNoteClose && _notes?.IsNoteDirty == true)
        {
            eventArgs.Cancel = true;
            ShowUnsavedNoteConfirmation();
        }

        base.OnClosing(eventArgs);
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        if (_notes is not null)
        {
            _notes.PropertyChanged -= OnNotesPropertyChanged;
        }

        base.OnClosed(eventArgs);
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

    private void OnWindowKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Escape || eventArgs.Handled)
        {
            return;
        }

        eventArgs.Handled = true;
        Close();
    }

    private void OnCloseRequested(object sender, RoutedEventArgs eventArgs) =>
        Close();

    private void OnContinueEditing(object sender, RoutedEventArgs eventArgs)
    {
        HideUnsavedNoteConfirmation();
        _ = NoteTextBox.Focus();
    }

    private void OnCloseWithoutSaving(object sender, RoutedEventArgs eventArgs)
    {
        _allowUnsavedNoteClose = true;
        Close();
    }

    private void OnNotesPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ProjectNotesViewModel.IsNoteDirty)
            && _notes?.IsNoteDirty == false)
        {
            HideUnsavedNoteConfirmation();
        }
    }

    private void ShowUnsavedNoteConfirmation()
    {
        if (UnsavedNoteCloseConfirmation.Visibility == Visibility.Visible)
        {
            return;
        }

        UnsavedNoteCloseConfirmation.Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () => ContinueEditingButton.Focus());
    }

    private void HideUnsavedNoteConfirmation()
    {
        UnsavedNoteCloseConfirmation.Visibility = Visibility.Collapsed;
    }
}
