using System.Windows;
using System.Windows.Controls;

namespace UProjectHub.App.Behaviors;

public static class EditableComboBoxTextBrushBehavior
{
    private const string TextBrushResourceKey = "Brush.TextPrimary";

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(EditableComboBoxTextBrushBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(
        DependencyObject element,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (element is not ComboBox comboBox)
        {
            return;
        }

        comboBox.Loaded -= OnComboBoxLoaded;
        if (eventArgs.NewValue is true)
        {
            comboBox.Loaded += OnComboBoxLoaded;
            if (comboBox.IsLoaded)
            {
                ApplySemanticTextBrushes(comboBox);
            }
        }
    }

    private static void OnComboBoxLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is ComboBox comboBox)
        {
            ApplySemanticTextBrushes(comboBox);
        }
    }

    private static void ApplySemanticTextBrushes(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        if (comboBox.Template.FindName("PART_EditableTextBox", comboBox)
            is not TextBox editor)
        {
            return;
        }

        editor.SetResourceReference(Control.ForegroundProperty, TextBrushResourceKey);
        editor.SetResourceReference(TextBox.CaretBrushProperty, TextBrushResourceKey);
    }
}
