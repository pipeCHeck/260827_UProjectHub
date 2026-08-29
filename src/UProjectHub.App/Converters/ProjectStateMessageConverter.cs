using System.Globalization;
using System.Windows;
using System.Windows.Data;
using UProjectHub.Core.Models;
using UProjectHub.App.Services;

namespace UProjectHub.App.Converters;

public sealed class ProjectStateMessageConverter : IValueConverter
{
    public static string GetMessage(
        ProjectState state,
        LocalizationService? localization = null)
    {
        return state switch
        {
            ProjectState.Available => string.Empty,
            ProjectState.Missing => Localize(
                localization,
                "String.StateMissing",
                "Missing"),
            ProjectState.Broken => Localize(
                localization,
                "String.StateBroken",
                "Project information unavailable"),
            _ => string.Empty,
        };
    }

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var localization = Application.Current?.TryFindResource(
            "Service.Localization") as LocalizationService;
        return value is ProjectState state
            ? GetMessage(state, localization)
            : string.Empty;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static string Localize(
        LocalizationService? localization,
        string key,
        string fallback) =>
        localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;
}
