using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace UProjectHub.App.Converters;

public sealed class SortDirectionGlyphConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        value switch
        {
            ListSortDirection.Ascending => "▲",
            ListSortDirection.Descending => "▼",
            _ => string.Empty,
        };

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
