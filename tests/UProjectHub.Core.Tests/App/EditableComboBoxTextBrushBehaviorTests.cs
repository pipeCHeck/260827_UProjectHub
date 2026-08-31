using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using UProjectHub.App.Behaviors;

namespace UProjectHub.Core.Tests.App;

[TestClass]
[DoNotParallelize]
public sealed class EditableComboBoxTextBrushBehaviorTests
{
    [STATestMethod]
    public void LoadedEditorUsesSemanticForegroundAndCaretBrush()
    {
        var darkSemanticText = new SolidColorBrush(Colors.WhiteSmoke);
        var lightSemanticText = new SolidColorBrush(Colors.DarkSlateGray);
        var selectionBrush = new SolidColorBrush(Colors.Goldenrod);
        var comboBox = new ComboBox
        {
            Foreground = Brushes.Transparent,
            Template = CreateEditableTemplate(),
        };
        EditableComboBoxTextBrushBehavior.SetIsEnabled(comboBox, true);
        var window = new Window
        {
            Content = comboBox,
            ShowActivated = false,
        };
        window.Resources["Brush.TextPrimary"] = darkSemanticText;
        window.Resources["Test.SelectionBrush"] = selectionBrush;

        try
        {
            window.Show();
            window.UpdateLayout();
            var editor = Assert.IsInstanceOfType<TextBox>(
                comboBox.Template.FindName("PART_EditableTextBox", comboBox));

            Assert.AreSame(darkSemanticText, editor.Foreground);
            Assert.AreSame(darkSemanticText, editor.CaretBrush);
            Assert.AreSame(selectionBrush, editor.SelectionBrush);

            window.Resources["Brush.TextPrimary"] = lightSemanticText;
            window.UpdateLayout();

            Assert.AreSame(lightSemanticText, editor.Foreground);
            Assert.AreSame(lightSemanticText, editor.CaretBrush);
            Assert.AreSame(selectionBrush, editor.SelectionBrush);
        }
        finally
        {
            window.Close();
        }
    }

    private static ControlTemplate CreateEditableTemplate() =>
        (ControlTemplate)XamlReader.Parse(
            """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="ComboBox">
                <Grid>
                    <TextBox x:Name="PART_EditableTextBox"
                             Foreground="Transparent"
                             CaretBrush="Transparent"
                             SelectionBrush="{DynamicResource Test.SelectionBrush}" />
                </Grid>
            </ControlTemplate>
            """);
}
