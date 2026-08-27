using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UProjectHub.Core.Sorting;

namespace UProjectHub.App.Controls;

public partial class ProjectList : UserControl
{
    public static readonly DependencyProperty ResetCommandProperty = DependencyProperty.Register(
        nameof(ResetCommand),
        typeof(ICommand),
        typeof(ProjectList));

    public static readonly DependencyProperty ActiveSortProperty = DependencyProperty.Register(
        nameof(ActiveSort),
        typeof(ProjectSortDefinition),
        typeof(ProjectList),
        new PropertyMetadata(null, OnActiveSortChanged));

    public ProjectList()
    {
        InitializeComponent();
    }

    public event EventHandler<ProjectSortRequestedEventArgs>? SortRequested;

    public ICommand? ResetCommand
    {
        get => (ICommand?)GetValue(ResetCommandProperty);
        set => SetValue(ResetCommandProperty, value);
    }

    public ProjectSortDefinition? ActiveSort
    {
        get => (ProjectSortDefinition?)GetValue(ActiveSortProperty);
        set => SetValue(ActiveSortProperty, value);
    }

    private static void OnActiveSortChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var control = (ProjectList)dependencyObject;
        control.UpdateSortIndicators(eventArgs.NewValue as ProjectSortDefinition);
    }

    private void OnSorting(object sender, DataGridSortingEventArgs eventArgs)
    {
        eventArgs.Handled = true;

        if (Enum.TryParse<ProjectSortColumn>(
                eventArgs.Column.SortMemberPath,
                out var column))
        {
            SortRequested?.Invoke(this, new ProjectSortRequestedEventArgs(column));
        }
    }

    private void UpdateSortIndicators(ProjectSortDefinition? sort)
    {
        foreach (var column in ProjectDataGrid.Columns)
        {
            column.SortDirection = null;
        }

        if (sort is null)
        {
            return;
        }

        var columnToMark = ProjectDataGrid.Columns.FirstOrDefault(column =>
            string.Equals(
                column.SortMemberPath,
                sort.Column.ToString(),
                StringComparison.Ordinal));

        if (columnToMark is not null)
        {
            columnToMark.SortDirection = sort.Direction == SortDirection.Ascending
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;
        }
    }
}

public sealed class ProjectSortRequestedEventArgs(ProjectSortColumn column) : EventArgs
{
    public ProjectSortColumn Column { get; } = column;
}
