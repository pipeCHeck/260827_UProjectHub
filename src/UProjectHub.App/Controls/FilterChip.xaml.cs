using System.Windows;
using System.Windows.Controls;

namespace UProjectHub.App.Controls;

public partial class FilterChip : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(FilterChip),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ChipContentProperty = DependencyProperty.Register(
        nameof(ChipContent),
        typeof(object),
        typeof(FilterChip));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(FilterChip),
        new PropertyMetadata(false));

    public FilterChip()
    {
        InitializeComponent();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public object? ChipContent
    {
        get => GetValue(ChipContentProperty);
        set => SetValue(ChipContentProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }
}
