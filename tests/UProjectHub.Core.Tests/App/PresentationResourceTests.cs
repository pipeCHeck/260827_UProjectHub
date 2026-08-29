using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using UProjectHub.App.Controls;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Settings;
using AppThemeMode = UProjectHub.Core.Settings.ThemeMode;

namespace UProjectHub.Core.Tests.App;

[TestClass]
[DoNotParallelize]
public sealed class PresentationResourceTests
{
    [STATestMethod]
    public void DetailsTextCellsUseTheSharedSemanticCellInset()
    {
        var dictionary = LoadDictionary("Themes/DataGrid.xaml");
        var expected = new Thickness(12, 0, 12, 0);
        var text = new TextBlock
        {
            Style = (Style)dictionary["DataGrid.TextCellContent"],
        };
        text.Resources["Thickness.ListCellPadding"] = expected;

        Assert.AreEqual(expected, text.Margin);
        Assert.AreEqual(VerticalAlignment.Center, text.VerticalAlignment);
    }

    [STATestMethod]
    public void FavoriteButtonUsesTheSemanticActiveBrushOnlyWhenFavorite()
    {
        var dictionary = LoadDictionary("Themes/Buttons.xaml");
        var quiet = new SolidColorBrush(Colors.Gray);
        var active = new SolidColorBrush(Colors.Goldenrod);
        var button = new Button
        {
            Style = (Style)dictionary["Button.Favorite"],
            DataContext = new FavoritePresentationState(true),
        };
        AddButtonResources(button, quiet);
        button.Resources["Brush.FavoriteActive"] = active;
        button.ApplyTemplate();
        button.Measure(new Size(40, 40));
        button.Arrange(new Rect(0, 0, 40, 40));
        button.UpdateLayout();

        Assert.AreSame(active, button.Foreground);

        button.DataContext = new FavoritePresentationState(false);

        Assert.AreSame(quiet, button.Foreground);
    }

    [STATestMethod]
    [DataRow(AppThemeMode.Dark, "Dark")]
    [DataRow(RowDensity.Compact, "Compact")]
    public void ClosedComboBoxDisplaysTheSelectedEnumValue(
        object selectedValue,
        string expectedText)
    {
        var dictionary = LoadDictionary("Themes/Buttons.xaml");
        var comboBox = new ComboBox
        {
            Style = (Style)dictionary[typeof(ComboBox)],
            ItemsSource = selectedValue is AppThemeMode
                ? Enum.GetValues<AppThemeMode>()
                : Enum.GetValues<RowDensity>(),
            SelectedItem = selectedValue,
        };
        AddComboBoxResources(comboBox);

        comboBox.ApplyTemplate();
        comboBox.Measure(new Size(240, 48));
        comboBox.Arrange(new Rect(0, 0, 240, 48));
        comboBox.UpdateLayout();

        Assert.IsTrue(
            Descendants<TextBlock>(comboBox).Any(text => text.Text == expectedText),
            $"The closed ComboBox did not display '{expectedText}'.");
    }

    [STATestMethod]
    public void ClosedComboBoxDisplaysTheSelectedLocalizedLanguageLabel()
    {
        var dictionary = LoadDictionary("Themes/Buttons.xaml");
        var options = new[]
        {
            new SettingOption<AppLanguage>(AppLanguage.English, "English"),
            new SettingOption<AppLanguage>(AppLanguage.Korean, "한국어"),
        };
        var comboBox = new ComboBox
        {
            Style = (Style)dictionary[typeof(ComboBox)],
            ItemsSource = options,
            DisplayMemberPath = nameof(SettingOption<AppLanguage>.Label),
            SelectedValuePath = nameof(SettingOption<AppLanguage>.Value),
            SelectedValue = AppLanguage.Korean,
        };
        AddComboBoxResources(comboBox);

        comboBox.ApplyTemplate();
        comboBox.Measure(new Size(240, 48));
        comboBox.Arrange(new Rect(0, 0, 240, 48));
        comboBox.UpdateLayout();

        Assert.IsTrue(
            Descendants<TextBlock>(comboBox).Any(text => text.Text == "한국어"),
            "The closed Language ComboBox did not display its selected localized label.");
    }

    [STATestMethod]
    [DataRow("Themes/NormalDensity.xaml")]
    [DataRow("Themes/CompactDensity.xaml")]
    public void ProjectFiltersExposeCompactReadableSemanticWidths(string dictionaryPath)
    {
        var dictionary = LoadDictionary(dictionaryPath);

        Assert.IsTrue(dictionary.Contains("Metric.EngineFilterWidth"));
        Assert.IsTrue(dictionary.Contains("Metric.ProjectTypeFilterWidth"));
        var engineWidth = (double)dictionary["Metric.EngineFilterWidth"];
        var typeWidth = (double)dictionary["Metric.ProjectTypeFilterWidth"];
        Assert.IsTrue(engineWidth is >= 120 and <= 136);
        Assert.IsTrue(typeWidth is >= 84 and <= 100);
        Assert.IsGreaterThan(typeWidth, engineWidth);
    }

    [STATestMethod]
    public void ContextMenuChromeUsesSemanticSurfacesWithoutASystemColorGutter()
    {
        var dictionary = LoadDictionary("Themes/Menus.xaml");
        var elevated = new SolidColorBrush(Colors.DarkSlateGray);
        var text = new SolidColorBrush(Colors.WhiteSmoke);
        var border = new SolidColorBrush(Colors.DimGray);
        var menu = new ContextMenu
        {
            Style = (Style)dictionary[typeof(ContextMenu)],
        };
        AddMenuResources(menu, elevated, text, border);
        var enabledItem = new MenuItem
        {
            Header = "Open Project",
            Style = (Style)dictionary[typeof(MenuItem)],
        };
        var disabledItem = new MenuItem
        {
            Header = "Unavailable",
            IsEnabled = false,
            Style = (Style)dictionary[typeof(MenuItem)],
        };
        var separator = new Separator
        {
            Style = (Style)dictionary[typeof(Separator)],
        };
        AddMenuResources(enabledItem, elevated, text, border);
        AddMenuResources(disabledItem, elevated, text, border);
        AddMenuResources(separator, elevated, text, border);
        menu.Items.Add(enabledItem);
        menu.Items.Add(disabledItem);
        menu.Items.Add(separator);

        menu.ApplyTemplate();
        enabledItem.ApplyTemplate();
        disabledItem.ApplyTemplate();
        separator.ApplyTemplate();

        var menuSurface = Assert.IsInstanceOfType<Border>(
            menu.Template.FindName("MenuSurface", menu));
        var itemChrome = Assert.IsInstanceOfType<Grid>(
            enabledItem.Template.FindName("ItemChrome", enabledItem));

        Assert.AreSame(elevated, menu.Background);
        Assert.AreSame(elevated, menuSurface.Background);
        Assert.AreSame(text, menu.Foreground);
        Assert.AreSame(text, enabledItem.Foreground);
        Assert.AreSame(enabledItem.Background, itemChrome.Background);
        Assert.AreEqual(0.48, disabledItem.Opacity);
        Assert.AreSame(border, separator.Background);
    }

    [STATestMethod]
    public void DisabledMenuItemsStillShowTheirExplanatoryTooltips()
    {
        var dictionary = LoadDictionary("Themes/Menus.xaml");
        var item = new MenuItem
        {
            Style = (Style)dictionary[typeof(MenuItem)],
            IsEnabled = false,
            ToolTip = "Unavailable reason",
        };

        Assert.IsTrue(ToolTipService.GetShowOnDisabled(item));
    }

    [STATestMethod]
    [DataRow(Orientation.Vertical)]
    [DataRow(Orientation.Horizontal)]
    public void SemanticScrollBarPreservesTheWpfTrackAndThumbContract(Orientation orientation)
    {
        var dictionary = LoadDictionary("Themes/ScrollBars.xaml");
        var quietThumb = new SolidColorBrush(Colors.Gray);
        var scrollBar = new ScrollBar
        {
            Orientation = orientation,
            Minimum = 0,
            Maximum = 100,
            ViewportSize = 10,
            Style = (Style)dictionary[typeof(ScrollBar)],
        };
        AddScrollBarResources(scrollBar, quietThumb);

        scrollBar.ApplyTemplate();
        scrollBar.Measure(new Size(160, 160));
        scrollBar.Arrange(new Rect(0, 0, 160, 160));
        scrollBar.UpdateLayout();

        var track = Assert.IsInstanceOfType<Track>(
            scrollBar.Template.FindName("PART_Track", scrollBar));
        var thumb = Assert.IsInstanceOfType<Thumb>(track.Thumb);
        var decrease = Assert.IsInstanceOfType<RepeatButton>(track.DecreaseRepeatButton);
        var increase = Assert.IsInstanceOfType<RepeatButton>(track.IncreaseRepeatButton);

        Assert.AreEqual(orientation, track.Orientation);
        Assert.AreSame(scrollBar, decrease.CommandTarget);
        Assert.AreSame(scrollBar, increase.CommandTarget);
        Assert.AreSame(
            orientation == Orientation.Vertical
                ? ScrollBar.PageUpCommand
                : ScrollBar.PageLeftCommand,
            decrease.Command);
        Assert.AreSame(
            orientation == Orientation.Vertical
                ? ScrollBar.PageDownCommand
                : ScrollBar.PageRightCommand,
            increase.Command);
        Assert.AreSame(quietThumb, thumb.Background);

        var thumbStyle = (Style)dictionary["ScrollBar.Thumb"];
        Assert.IsTrue(thumbStyle.Triggers.OfType<Trigger>().Any(trigger =>
            trigger.Property == UIElement.IsMouseOverProperty));
        Assert.IsTrue(thumbStyle.Triggers.OfType<Trigger>().Any(trigger =>
            trigger.Property == Thumb.IsDraggingProperty));
    }

    [STATestMethod]
    public void SearchInputAndPlaceholderShareTheSameSemanticLeftInsetInBothDensities()
    {
        var normal = LoadDictionary("Themes/NormalDensity.xaml");
        var compact = LoadDictionary("Themes/CompactDensity.xaml");

        Assert.IsTrue(normal.Contains("Thickness.SearchTextInset"));
        Assert.IsTrue(compact.Contains("Thickness.SearchTextInset"));

        var normalInset = (Thickness)normal["Thickness.SearchTextInset"];
        var compactInset = (Thickness)compact["Thickness.SearchTextInset"];
        Assert.AreEqual(normalInset, compactInset);
        Assert.IsGreaterThan(0, normalInset.Left);
        Assert.AreEqual(0, normalInset.Top);
        Assert.AreEqual(0, normalInset.Right);
        Assert.AreEqual(0, normalInset.Bottom);

        var application = Application.Current ?? new Application();
        var buttons = LoadDictionary("Themes/Buttons.xaml");
        application.Resources.MergedDictionaries.Add(buttons);
        try
        {
            var searchBox = new SearchBox();
            searchBox.Resources["Thickness.SearchTextInset"] = normalInset;
            var input = Assert.IsInstanceOfType<TextBox>(searchBox.FindName("SearchInput"));
            var placeholder = Assert.IsInstanceOfType<TextBlock>(searchBox.FindName("SearchPlaceholder"));

            Assert.AreEqual(normalInset, input.Padding);
            Assert.AreEqual(normalInset, placeholder.Margin);
        }
        finally
        {
            application.Resources.MergedDictionaries.Remove(buttons);
        }
    }

    private static ResourceDictionary LoadDictionary(string relativePath) =>
        (ResourceDictionary)Application.LoadComponent(
            new Uri(
                $"/UProjectHub.App;component/{relativePath}",
                UriKind.Relative));

    private static void AddButtonResources(Button button, Brush foreground)
    {
        button.Resources["Brush.Surface"] = Brushes.Transparent;
        button.Resources["Brush.TextPrimary"] = foreground;
        button.Resources["Brush.BorderSubtle"] = Brushes.Transparent;
        button.Resources["Brush.Transparent"] = Brushes.Transparent;
        button.Resources["Brush.HoverSurface"] = Brushes.Transparent;
        button.Resources["Brush.Focus"] = Brushes.Transparent;
        button.Resources["Thickness.ControlPadding"] = new Thickness(8, 4, 8, 4);
        button.Resources["CornerRadius.Control"] = new CornerRadius(8);
        button.Resources["Motion.FastDuration"] = new Duration(TimeSpan.Zero);
        button.Resources["Motion.EaseOut"] = null;
    }

    private static void AddComboBoxResources(ComboBox comboBox)
    {
        comboBox.Resources["Brush.Surface"] = Brushes.White;
        comboBox.Resources["Brush.TextPrimary"] = Brushes.Black;
        comboBox.Resources["Brush.TextSecondary"] = Brushes.Gray;
        comboBox.Resources["Brush.BorderSubtle"] = Brushes.Gray;
        comboBox.Resources["Brush.HoverSurface"] = Brushes.LightGray;
        comboBox.Resources["Brush.Focus"] = Brushes.Blue;
        comboBox.Resources["CornerRadius.Small"] = new CornerRadius(6);
    }

    private static void AddMenuResources(
        FrameworkElement element,
        Brush elevated,
        Brush text,
        Brush border)
    {
        element.Resources["Brush.ElevatedSurface"] = elevated;
        element.Resources["Brush.HoverSurface"] = Brushes.SlateGray;
        element.Resources["Brush.SelectedSurface"] = Brushes.DarkSlateBlue;
        element.Resources["Brush.BorderSubtle"] = border;
        element.Resources["Brush.TextPrimary"] = text;
        element.Resources["Brush.TextSecondary"] = Brushes.LightGray;
        element.Resources["Brush.Transparent"] = Brushes.Transparent;
        element.Resources["Thickness.MenuItemPadding"] = new Thickness(8, 6, 10, 6);
        element.Resources["Thickness.MenuSeparatorMargin"] = new Thickness(8, 3, 8, 3);
        element.Resources["CornerRadius.Small"] = new CornerRadius(6);
    }

    private static void AddScrollBarResources(ScrollBar scrollBar, Brush quietThumb)
    {
        scrollBar.Resources["Brush.Transparent"] = Brushes.Transparent;
        scrollBar.Resources["Brush.BorderSubtle"] = quietThumb;
        scrollBar.Resources["Brush.TextSecondary"] = Brushes.LightGray;
        scrollBar.Resources["Brush.Accent"] = Brushes.CornflowerBlue;
        scrollBar.Resources["Metric.ScrollBarThickness"] = 14d;
        scrollBar.Resources["Metric.ScrollBarThumbMinimumLength"] = 24d;
        scrollBar.Resources["CornerRadius.Small"] = new CornerRadius(6);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    public sealed record FavoritePresentationState(bool IsFavorite);
}
