using System.Globalization;
using System.Windows.Data;
using UProjectHub.Core.Time;

namespace UProjectHub.App.Converters;

public sealed class RelativeTimeConverter : IValueConverter
{
    private const string UnknownText = "—";
    private static readonly CultureInfo DisplayCulture = CultureInfo.InvariantCulture;
    private readonly IClock _clock;
    private readonly TimeZoneInfo _timeZone;

    public RelativeTimeConverter()
        : this(new SystemClock(), TimeZoneInfo.Local)
    {
    }

    public RelativeTimeConverter(IClock clock, TimeZoneInfo timeZone)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
    }

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is not DateTimeOffset timestamp || timestamp == DateTimeOffset.MinValue)
        {
            return UnknownText;
        }

        if (string.Equals(
                parameter as string,
                "CalendarDate",
                StringComparison.Ordinal))
        {
            return TimeZoneInfo
                .ConvertTime(timestamp, _timeZone)
                .ToString("yyyy.MM.dd.", DisplayCulture);
        }

        var nowUtc = _clock.UtcNow;
        var elapsed = nowUtc - timestamp;

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "Just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes} min ago";
        }

        var localNow = TimeZoneInfo.ConvertTime(nowUtc, _timeZone);
        var localTimestamp = TimeZoneInfo.ConvertTime(timestamp, _timeZone);

        if (localTimestamp.Date == localNow.Date)
        {
            return $"Today {localTimestamp.ToString("HH:mm", DisplayCulture)}";
        }

        if (localTimestamp.Date == localNow.Date.AddDays(-1))
        {
            return $"Yesterday {localTimestamp.ToString("HH:mm", DisplayCulture)}";
        }

        return localTimestamp.Year == localNow.Year
            ? localTimestamp.ToString("MMM d", DisplayCulture)
            : localTimestamp.ToString("yyyy-MM-dd", DisplayCulture);
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
