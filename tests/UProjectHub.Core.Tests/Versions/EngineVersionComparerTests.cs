using UProjectHub.Core.Versions;

namespace UProjectHub.Core.Tests.Versions;

[TestClass]
public sealed class EngineVersionComparerTests
{
    private static readonly EngineVersionComparer Comparer = EngineVersionComparer.Instance;

    [TestMethod]
    public void FiveNineSortsBeforeFiveTen()
    {
        Assert.IsLessThan(0, Comparer.Compare("5.9", "5.10"));
    }

    [TestMethod]
    public void VersionWithoutPatchSortsBeforeHigherPatch()
    {
        Assert.IsLessThan(0, Comparer.Compare("5.8", "5.8.1"));
    }

    [TestMethod]
    public void NumericVersionsUseComponentWiseComparison()
    {
        Assert.IsLessThan(0, Comparer.Compare("4.27.2", "5.0"));
        Assert.IsLessThan(0, Comparer.Compare("5.8.9", "5.10.0"));
    }

    [TestMethod]
    public void NumericValueSortsBeforeNonNumericValue()
    {
        Assert.IsLessThan(0, Comparer.Compare("5.10", "custom-build"));
        Assert.IsGreaterThan(0, Comparer.Compare("custom-build", "5.10"));
    }

    [TestMethod]
    public void NullSortsLast()
    {
        Assert.IsLessThan(0, Comparer.Compare("custom-build", null));
        Assert.IsGreaterThan(0, Comparer.Compare(null, "custom-build"));
        Assert.AreEqual(0, Comparer.Compare(null, null));
    }
}
