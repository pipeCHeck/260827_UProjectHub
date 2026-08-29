using System.ComponentModel;
using System.Globalization;
using UProjectHub.App.Converters;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class SortDirectionGlyphConverterTests
{
    [TestMethod]
    [DataRow(ListSortDirection.Ascending, "▲")]
    [DataRow(ListSortDirection.Descending, "▼")]
    public void SortDirectionMapsToAVisibleArrow(
        ListSortDirection direction,
        string expected)
    {
        var converter = new SortDirectionGlyphConverter();

        var actual = converter.Convert(
            direction,
            typeof(string),
            null,
            CultureInfo.InvariantCulture);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void UnsortedColumnHasNoArrow()
    {
        var converter = new SortDirectionGlyphConverter();

        var actual = converter.Convert(
            null,
            typeof(string),
            null,
            CultureInfo.InvariantCulture);

        Assert.AreEqual(string.Empty, actual);
    }
}
