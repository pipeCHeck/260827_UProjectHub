using System.Globalization;
using UProjectHub.App.Converters;
using UProjectHub.Core.Tests.Time;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class RelativeTimeConverterTests
{
    private static readonly TimeZoneInfo TestTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "Task22TestTimeZone",
        TimeSpan.FromHours(9),
        "Task 22 Test Time",
        "Task 22 Test Time");

    private static readonly DateTimeOffset NowUtc =
        new(2026, 8, 28, 3, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void UnknownValues_ReturnQuietPlaceholder()
    {
        var converter = CreateConverter();

        Assert.AreEqual("—", Convert(converter, null));
        Assert.AreEqual("—", Convert(converter, DateTimeOffset.MinValue));
    }

    [TestMethod]
    public void FutureAndSubMinuteValues_ReturnJustNow()
    {
        var converter = CreateConverter();

        Assert.AreEqual("Just now", Convert(converter, NowUtc.AddMinutes(1)));
        Assert.AreEqual("Just now", Convert(converter, NowUtc.AddSeconds(-59)));
    }

    [TestMethod]
    public void ValuesUnderOneHour_ReturnWholeMinutesAgo()
    {
        var converter = CreateConverter();

        Assert.AreEqual("1 min ago", Convert(converter, NowUtc.AddMinutes(-1)));
        Assert.AreEqual("18 min ago", Convert(converter, NowUtc.AddMinutes(-18)));
        Assert.AreEqual("59 min ago", Convert(converter, NowUtc.AddMinutes(-59)));
    }

    [TestMethod]
    public void OlderSameDayValue_UsesLocalTodayTime()
    {
        var converter = CreateConverter();
        var localTenFifteen = new DateTimeOffset(2026, 8, 28, 1, 15, 0, TimeSpan.Zero);

        Assert.AreEqual("Today 10:15", Convert(converter, localTenFifteen));
    }

    [TestMethod]
    public void PreviousLocalDate_UsesYesterdayTime()
    {
        var converter = CreateConverter();
        var localYesterdayElevenFortyFive =
            new DateTimeOffset(2026, 8, 27, 14, 45, 0, TimeSpan.Zero);

        Assert.AreEqual("Yesterday 23:45", Convert(converter, localYesterdayElevenFortyFive));
    }

    [TestMethod]
    public void OlderSameYearValue_UsesInvariantEnglishMonthAndDay()
    {
        var converter = CreateConverter();
        var localMarchFourth = new DateTimeOffset(2026, 3, 3, 15, 0, 0, TimeSpan.Zero);

        Assert.AreEqual("Mar 4", Convert(converter, localMarchFourth));
    }

    [TestMethod]
    public void PreviousYearValue_UsesSortableDate()
    {
        var converter = CreateConverter();
        var localPreviousYear = new DateTimeOffset(2025, 12, 30, 15, 0, 0, TimeSpan.Zero);

        Assert.AreEqual("2025-12-31", Convert(converter, localPreviousYear));
    }

    [TestMethod]
    public void CalendarDateMode_UsesLocalDottedYearMonthDayForLastModified()
    {
        var converter = CreateConverter();
        var localAugustTwentySixth =
            new DateTimeOffset(2026, 8, 25, 15, 30, 0, TimeSpan.Zero);

        Assert.AreEqual(
            "2026.08.26.",
            converter.Convert(
                localAugustTwentySixth,
                typeof(string),
                "CalendarDate",
                CultureInfo.InvariantCulture));
        Assert.AreEqual(
            "—",
            converter.Convert(
                DateTimeOffset.MinValue,
                typeof(string),
                "CalendarDate",
                CultureInfo.InvariantCulture));
    }

    private static RelativeTimeConverter CreateConverter()
    {
        return new RelativeTimeConverter(new FakeClock(NowUtc), TestTimeZone);
    }

    private static object Convert(RelativeTimeConverter converter, DateTimeOffset? value)
    {
        return converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);
    }
}
