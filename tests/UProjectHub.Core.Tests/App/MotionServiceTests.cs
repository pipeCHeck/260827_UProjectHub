using System.Windows;
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
            TimeSpan.FromMilliseconds(140),
            ((Duration)resources[MotionService.NormalDurationResourceKey]).TimeSpan);
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
