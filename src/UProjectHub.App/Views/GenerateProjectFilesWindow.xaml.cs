using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UProjectHub.App.ViewModels;

namespace UProjectHub.App.Views;

public partial class GenerateProjectFilesWindow : Window
{
    private bool _followOutputTail = true;
    private bool _scrollToEndPending;

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

    private void OutputDetailsTextBox_OnTextChanged(
        object sender,
        TextChangedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(OutputDetailsTextBox.Text))
        {
            _followOutputTail = true;
            return;
        }

        if (!_followOutputTail || _scrollToEndPending)
        {
            return;
        }

        _scrollToEndPending = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () =>
            {
                _scrollToEndPending = false;
                if (_followOutputTail)
                {
                    OutputDetailsTextBox.ScrollToEnd();
                }
            });
    }

    private void OutputDetailsTextBox_OnScrollChanged(
        object sender,
        ScrollChangedEventArgs eventArgs)
    {
        if (eventArgs.ExtentHeightChange != 0)
        {
            return;
        }

        const double tolerance = 1;
        _followOutputTail = eventArgs.VerticalOffset
            >= eventArgs.ExtentHeight - eventArgs.ViewportHeight - tolerance;
    }
}
