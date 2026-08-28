using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UProjectHub.App.Behaviors;
using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class MotionServiceTests
{
    [TestMethod]
    public void StatusBarAnimationPreference_NotifiesOnlyWhenItsValueChanges()
    {
        var statusBar = new StatusBarViewModel();
        var changes = 0;
        statusBar.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(StatusBarViewModel.AreAnimationsEnabled))
            {
                changes++;
            }
        };

        statusBar.SetAnimationsEnabled(false);
        statusBar.SetAnimationsEnabled(false);

        Assert.IsFalse(statusBar.AreAnimationsEnabled);
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void EnabledPreference_UsesSemanticMotionDurations()
    {
        var resources = new ResourceDictionary();
        var preference = new FakeSystemAnimationPreference(true);
        using var service = new MotionService(resources, preference);

        Assert.IsTrue(service.AreAnimationsEnabled);
        Assert.AreEqual(TimeSpan.FromMilliseconds(90), service.FastDuration);
        Assert.AreEqual(TimeSpan.FromMilliseconds(140), service.NormalDuration);
        Assert.AreEqual(TimeSpan.FromMilliseconds(180), service.SlowDuration);
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(90),
            ((Duration)resources[MotionService.FastDurationResourceKey]).TimeSpan);
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(140),
            ((Duration)resources[MotionService.NormalDurationResourceKey]).TimeSpan);
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(180),
            ((Duration)resources[MotionService.SlowDurationResourceKey]).TimeSpan);
    }

    [TestMethod]
    public void DisabledPreference_MakesEveryEffectiveDurationImmediate()
    {
        var resources = new ResourceDictionary();
        var preference = new FakeSystemAnimationPreference(false);
        using var service = new MotionService(resources, preference);

        Assert.IsFalse(service.AreAnimationsEnabled);
        Assert.AreEqual(TimeSpan.Zero, service.FastDuration);
        Assert.AreEqual(TimeSpan.Zero, service.NormalDuration);
        Assert.AreEqual(TimeSpan.Zero, service.SlowDuration);
        Assert.AreEqual(Duration.Automatic, (Duration)resources[MotionService.ActivityDurationResourceKey]);
    }

    [TestMethod]
    public void RuntimePreferenceChanges_UpdateDurationsAndNotifyImmediately()
    {
        var resources = new ResourceDictionary();
        var preference = new FakeSystemAnimationPreference(true);
        using var service = new MotionService(resources, preference);
        var changes = 0;
        service.PreferenceChanged += (_, _) => changes++;

        preference.SetAnimationsEnabled(false);

        Assert.IsFalse(service.AreAnimationsEnabled);
        Assert.AreEqual(TimeSpan.Zero, service.NormalDuration);
        Assert.AreEqual(Duration.Automatic, (Duration)resources[MotionService.ActivityDurationResourceKey]);
        Assert.AreEqual(1, changes);

        preference.SetAnimationsEnabled(true);

        Assert.IsTrue(service.AreAnimationsEnabled);
        Assert.AreEqual(TimeSpan.FromMilliseconds(140), service.NormalDuration);
        Assert.AreEqual(TimeSpan.FromSeconds(1), ((Duration)resources[MotionService.ActivityDurationResourceKey]).TimeSpan);
        Assert.AreEqual(2, changes);
    }

    [TestMethod]
    public void PreferenceChanges_DoNotRemoveUnrelatedThemeOrDensityDictionaries()
    {
        var resources = new ResourceDictionary();
        var theme = new ResourceDictionary();
        var density = new ResourceDictionary();
        resources.MergedDictionaries.Add(theme);
        resources.MergedDictionaries.Add(density);
        var preference = new FakeSystemAnimationPreference(true);
        using var service = new MotionService(resources, preference);

        preference.SetAnimationsEnabled(false);

        CollectionAssert.AreEqual(
            new[] { theme, density },
            resources.MergedDictionaries.ToArray());
    }

    [STATestMethod]
    public void ZeroDurationPressFeedback_ChangesScaleImmediatelyAndRestoresIt()
    {
        var button = new Button
        {
            RenderTransform = new ScaleTransform(1, 1),
        };
        MotionFeedbackBehavior.SetDuration(button, new Duration(TimeSpan.Zero));
        MotionFeedbackBehavior.SetPressedScale(button, 0.98);

        MotionFeedbackBehavior.SetIsPressFeedbackActive(button, true);

        var pressedTransform = (ScaleTransform)button.RenderTransform;
        Assert.AreEqual(0.98, pressedTransform.ScaleX, 0.0001);
        Assert.AreEqual(0.98, pressedTransform.ScaleY, 0.0001);

        MotionFeedbackBehavior.SetIsPressFeedbackActive(button, false);

        var releasedTransform = (ScaleTransform)button.RenderTransform;
        Assert.AreEqual(1, releasedTransform.ScaleX, 0.0001);
        Assert.AreEqual(1, releasedTransform.ScaleY, 0.0001);
    }

    [STATestMethod]
    public void ZeroDurationOpacityFeedback_ChangesImmediatelyAndRestoresIt()
    {
        var overlay = new Border { Opacity = 0 };
        MotionFeedbackBehavior.SetDuration(overlay, new Duration(TimeSpan.Zero));
        MotionFeedbackBehavior.SetRestingOpacity(overlay, 0);
        MotionFeedbackBehavior.SetActiveOpacity(overlay, 0.55);

        MotionFeedbackBehavior.SetIsOpacityFeedbackActive(overlay, true);

        Assert.AreEqual(0.55, overlay.Opacity, 0.0001);

        MotionFeedbackBehavior.SetIsOpacityFeedbackActive(overlay, false);

        Assert.AreEqual(0, overlay.Opacity, 0.0001);
    }

    [STATestMethod]
    public void DurationChangingToZero_SnapsActiveFeedbackToItsFinalState()
    {
        var overlay = new Border { Opacity = 0 };
        MotionFeedbackBehavior.SetDuration(overlay, new Duration(TimeSpan.FromSeconds(1)));
        MotionFeedbackBehavior.SetIsOpacityFeedbackEnabled(overlay, true);
        MotionFeedbackBehavior.SetActiveOpacity(overlay, 0.55);
        MotionFeedbackBehavior.SetIsOpacityFeedbackActive(overlay, true);

        MotionFeedbackBehavior.SetDuration(overlay, new Duration(TimeSpan.Zero));

        Assert.AreEqual(0.55, overlay.Opacity, 0.0001);
    }

    private sealed class FakeSystemAnimationPreference(bool areAnimationsEnabled)
        : ISystemAnimationPreference
    {
        public bool AreAnimationsEnabled { get; private set; } = areAnimationsEnabled;

        public event EventHandler? PreferenceChanged;

        public void SetAnimationsEnabled(bool areAnimationsEnabled)
        {
            if (AreAnimationsEnabled == areAnimationsEnabled)
            {
                return;
            }

            AreAnimationsEnabled = areAnimationsEnabled;
            PreferenceChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
