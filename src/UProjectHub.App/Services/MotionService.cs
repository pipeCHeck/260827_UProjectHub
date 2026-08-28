using System.Windows;

namespace UProjectHub.App.Services;

public sealed class MotionService : IDisposable
{
    public const string FastDurationResourceKey = "Motion.FastDuration";
    public const string NormalDurationResourceKey = "Motion.NormalDuration";
    public const string SlowDurationResourceKey = "Motion.SlowDuration";
    public const string ActivityDurationResourceKey = "Motion.ActivityDuration";
    public const string AnimationsEnabledResourceKey = "Motion.AnimationsEnabled";

    private static readonly TimeSpan EnabledFastDuration = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan EnabledNormalDuration = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan EnabledSlowDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan EnabledActivityDuration = TimeSpan.FromSeconds(1);

    private readonly ResourceDictionary _resources;
    private readonly ISystemAnimationPreference _preference;
    private bool _isDisposed;

    public MotionService(
        ResourceDictionary resources,
        ISystemAnimationPreference preference)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _preference = preference ?? throw new ArgumentNullException(nameof(preference));
        _preference.PreferenceChanged += OnPreferenceChanged;
        UpdateEffectiveValues();
    }

    public bool AreAnimationsEnabled { get; private set; }

    public TimeSpan FastDuration { get; private set; }

    public TimeSpan NormalDuration { get; private set; }

    public TimeSpan SlowDuration { get; private set; }

    public event EventHandler? PreferenceChanged;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _preference.PreferenceChanged -= OnPreferenceChanged;
        _isDisposed = true;
    }

    private void OnPreferenceChanged(object? sender, EventArgs eventArgs)
    {
        UpdateEffectiveValues();
        PreferenceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateEffectiveValues()
    {
        AreAnimationsEnabled = _preference.AreAnimationsEnabled;
        FastDuration = AreAnimationsEnabled ? EnabledFastDuration : TimeSpan.Zero;
        NormalDuration = AreAnimationsEnabled ? EnabledNormalDuration : TimeSpan.Zero;
        SlowDuration = AreAnimationsEnabled ? EnabledSlowDuration : TimeSpan.Zero;

        _resources[FastDurationResourceKey] = new Duration(FastDuration);
        _resources[NormalDurationResourceKey] = new Duration(NormalDuration);
        _resources[SlowDurationResourceKey] = new Duration(SlowDuration);
        _resources[ActivityDurationResourceKey] = AreAnimationsEnabled
            ? new Duration(EnabledActivityDuration)
            : Duration.Automatic;
        _resources[AnimationsEnabledResourceKey] = AreAnimationsEnabled;
    }
}
