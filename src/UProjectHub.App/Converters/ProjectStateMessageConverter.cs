using System.Globalization;
using System.Windows.Data;
using UProjectHub.Core.Models;

namespace UProjectHub.App.Converters;

public sealed class ProjectStateMessageConverter : IValueConverter
{
    public static string GetMessage(ProjectState state)
    {
        return state switch
        {
            ProjectState.Available => string.Empty,
            ProjectState.Missing => "Missing",
            ProjectState.Broken => "Project information unavailable",
            _ => string.Empty,
        };
    }

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return value is ProjectState state ? GetMessage(state) : string.Empty;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
