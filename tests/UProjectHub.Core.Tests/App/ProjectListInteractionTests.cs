using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UProjectHub.App.Controls;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ProjectListInteractionTests
{
    [TestMethod]
    public void ExternalForegroundChangeClearsSelectionWithoutRestoringItOnReturn()
    {
        var activation = new FakeApplicationActivationSource();
        var isSelected = true;
        var clearCount = 0;
        using var lifecycle = new ApplicationSelectionLifecycle(
            activation,
            () =>
            {
                isSelected = false;
                clearCount++;
            });

        lifecycle.Attach();
        activation.RaiseActivationChanged(isApplicationActive: false);

        Assert.IsFalse(isSelected);
        Assert.AreEqual(1, clearCount);

        activation.RaiseActivationChanged(isApplicationActive: true);

        Assert.IsFalse(isSelected);
        Assert.AreEqual(1, clearCount);
    }

    [TestMethod]
    public void InternalWindowOrPopupForegroundChangePreservesSelection()
    {
        var activation = new FakeApplicationActivationSource();
        var isSelected = true;
        var clearCount = 0;
        using var lifecycle = new ApplicationSelectionLifecycle(
            activation,
            () =>
            {
                isSelected = false;
                clearCount++;
            });
        lifecycle.Attach();

        activation.RaiseActivationChanged(isApplicationActive: true);

        Assert.IsTrue(isSelected);
        Assert.AreEqual(0, clearCount);
    }

    [TestMethod]
    public void DetachedLifecycleDoesNotClearSelection()
    {
        var activation = new FakeApplicationActivationSource();
        var clearCount = 0;
        var lifecycle = new ApplicationSelectionLifecycle(
            activation,
            () => clearCount++);
        lifecycle.Attach();
        lifecycle.Dispose();

        activation.RaiseActivationChanged(isApplicationActive: false);

        Assert.AreEqual(0, clearCount);
    }

    [TestMethod]
    public void SelectionClearsOnlyForTheEmptyDataGridSurface()
    {
        Assert.IsTrue(ProjectList.ShouldClearSelection(
            isWithinRow: false,
            isWithinHeader: false,
            isWithinScrollBar: false));

        Assert.IsFalse(ProjectList.ShouldClearSelection(
            isWithinRow: true,
            isWithinHeader: false,
            isWithinScrollBar: false));
        Assert.IsFalse(ProjectList.ShouldClearSelection(
            isWithinRow: false,
            isWithinHeader: true,
            isWithinScrollBar: false));
        Assert.IsFalse(ProjectList.ShouldClearSelection(
            isWithinRow: false,
            isWithinHeader: false,
            isWithinScrollBar: true));
    }

    [STATestMethod]
    public void SelectedCellsUseTheSemanticSelectedSurfaceRegardlessOfFocus()
    {
        var dictionary = (ResourceDictionary)Application.LoadComponent(
            new Uri(
                "/UProjectHub.App;component/Themes/DataGrid.xaml",
                UriKind.Relative));
        var selectedSurface = new SolidColorBrush(Colors.Magenta);
        var textPrimary = new SolidColorBrush(Colors.White);
        var cell = new DataGridCell
        {
            Style = (Style)dictionary[typeof(DataGridCell)],
        };
        cell.Resources["Brush.SelectedSurface"] = selectedSurface;
        cell.Resources["Brush.TextPrimary"] = textPrimary;

        cell.IsSelected = true;

        Assert.AreSame(selectedSurface, cell.Background);
        Assert.AreSame(textPrimary, cell.Foreground);
    }

    private sealed class FakeApplicationActivationSource : IApplicationActivationSource
    {
        public event EventHandler<ApplicationActivationChangedEventArgs>?
            ActivationChanged;

        public void RaiseActivationChanged(bool isApplicationActive) =>
            ActivationChanged?.Invoke(
                this,
                new ApplicationActivationChangedEventArgs(isApplicationActive));
    }
}
