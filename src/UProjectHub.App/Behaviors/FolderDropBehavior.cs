using System.IO;
using System.Windows;
using System.Windows.Input;

namespace UProjectHub.App.Behaviors;

public static class FolderDropBehavior
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached(
            "Command",
            typeof(ICommand),
            typeof(FolderDropBehavior),
            new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject dependencyObject) =>
        (ICommand?)dependencyObject.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject dependencyObject, ICommand? value) =>
        dependencyObject.SetValue(CommandProperty, value);

    public static IReadOnlyList<string> GetDroppedFolders(IDataObject dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);
        if (!dataObject.GetDataPresent(DataFormats.FileDrop)
            || dataObject.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return [];
        }

        return Array.AsReadOnly(paths.Where(Directory.Exists).ToArray());
    }

    private static void OnCommandChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not UIElement element)
        {
            throw new ArgumentException(
                "FolderDropBehavior can only be attached to a UIElement.",
                nameof(dependencyObject));
        }

        element.DragOver -= OnDragOver;
        element.Drop -= OnDrop;
        element.AllowDrop = eventArgs.NewValue is ICommand;
        if (eventArgs.NewValue is ICommand)
        {
            element.DragOver += OnDragOver;
            element.Drop += OnDrop;
        }
    }

    private static void OnDragOver(object sender, DragEventArgs eventArgs)
    {
        var element = (UIElement)sender;
        var folders = GetDroppedFolders(eventArgs.Data);
        var command = GetCommand(element);
        eventArgs.Effects = folders.Count > 0 && command?.CanExecute(folders) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs eventArgs)
    {
        var element = (UIElement)sender;
        var folders = GetDroppedFolders(eventArgs.Data);
        var command = GetCommand(element);
        if (folders.Count > 0 && command?.CanExecute(folders) == true)
        {
            command.Execute(folders);
        }

        eventArgs.Handled = true;
    }
}
