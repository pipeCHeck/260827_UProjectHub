namespace UProjectHub.App.Services;

public interface ISystemAnimationPreference
{
    bool AreAnimationsEnabled { get; }

    event EventHandler? PreferenceChanged;
}
