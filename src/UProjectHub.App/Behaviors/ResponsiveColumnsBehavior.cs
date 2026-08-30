using System.Windows;
using System.Windows.Controls;
using UProjectHub.Core.Settings;

namespace UProjectHub.App.Behaviors;

public static class ResponsiveColumnsBehavior
{
    public const double WideThreshold = 1000d;
    public const double MediumThreshold = 820d;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ResponsiveColumnsBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ColumnLayoutProperty = DependencyProperty.RegisterAttached(
        "ColumnLayout",
        typeof(IReadOnlyList<ColumnLayoutState>),
        typeof(ResponsiveColumnsBehavior),
        new PropertyMetadata(null, OnColumnLayoutChanged));

    public static bool GetIsEnabled(DependencyObject dependencyObject)
    {
        return (bool)dependencyObject.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject dependencyObject, bool value)
    {
        dependencyObject.SetValue(IsEnabledProperty, value);
    }

    public static IReadOnlyList<ColumnLayoutState>? GetColumnLayout(DependencyObject dependencyObject)
    {
        return (IReadOnlyList<ColumnLayoutState>?)dependencyObject.GetValue(ColumnLayoutProperty);
    }

    public static void SetColumnLayout(
        DependencyObject dependencyObject,
        IReadOnlyList<ColumnLayoutState>? value)
    {
        dependencyObject.SetValue(ColumnLayoutProperty, value);
    }

    public static ResponsiveColumnLayout GetLayout(double actualWidth)
    {
        if (actualWidth >= WideThreshold)
        {
            return new ResponsiveColumnLayout(
                ShowType: true,
                ShowLastLaunched: true,
                ShowGit: true);
        }

        if (actualWidth >= MediumThreshold)
        {
            return new ResponsiveColumnLayout(
                ShowType: true,
                ShowLastLaunched: false,
                ShowGit: true);
        }

        return new ResponsiveColumnLayout(
            ShowType: false,
            ShowLastLaunched: false,
            ShowGit: false);
    }

    private static void OnIsEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not DataGrid dataGrid)
        {
            throw new ArgumentException(
                "ResponsiveColumnsBehavior can only be attached to a DataGrid.",
                nameof(dependencyObject));
        }

        dataGrid.Loaded -= OnDataGridLoaded;
        dataGrid.SizeChanged -= OnDataGridSizeChanged;

        if ((bool)eventArgs.NewValue)
        {
            dataGrid.Loaded += OnDataGridLoaded;
            dataGrid.SizeChanged += OnDataGridSizeChanged;
            Apply(dataGrid);
        }
    }

    private static void OnColumnLayoutChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is DataGrid dataGrid && GetIsEnabled(dataGrid))
        {
            Apply(dataGrid);
        }
    }

    private static void OnDataGridLoaded(object sender, RoutedEventArgs eventArgs)
    {
        Apply((DataGrid)sender);
    }

    private static void OnDataGridSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        Apply((DataGrid)sender);
    }

    private static void Apply(DataGrid dataGrid)
    {
        var responsiveLayout = GetLayout(dataGrid.ActualWidth);
        var persistedLayout = GetColumnLayout(dataGrid);

        foreach (var column in dataGrid.Columns)
        {
            var columnId = column.SortMemberPath;
            if (string.IsNullOrWhiteSpace(columnId))
            {
                continue;
            }

            var persisted = persistedLayout?.FirstOrDefault(state =>
                string.Equals(state.ColumnId, columnId, StringComparison.OrdinalIgnoreCase));
            var isVisible = persisted?.IsVisible ?? true;
            if (string.Equals(columnId, "LastLaunched", StringComparison.Ordinal))
            {
                isVisible &= responsiveLayout.ShowLastLaunched;
            }
            else if (string.Equals(columnId, "ProjectType", StringComparison.Ordinal))
            {
                isVisible &= responsiveLayout.ShowType;
            }
            else if (string.Equals(columnId, "GitState", StringComparison.Ordinal))
            {
                isVisible &= responsiveLayout.ShowGit;
            }

            column.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            if (persisted?.Width is > 0d)
            {
                column.Width = new DataGridLength(persisted.Width.Value);
            }
        }
    }
}

public readonly record struct ResponsiveColumnLayout(
    bool ShowType,
    bool ShowLastLaunched,
    bool ShowGit);
