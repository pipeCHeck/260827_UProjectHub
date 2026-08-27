using UProjectHub.Core.Versions;

namespace UProjectHub.Core.Tests.Versions;

[TestClass]
public sealed class EngineVersionTests
{
    [TestMethod]
    public void TryParseAcceptsMajorAndMinor()
    {
        var parsed = EngineVersion.TryParse("5.8", out var version);

        Assert.IsTrue(parsed);
        Assert.AreEqual(5, version.Major);
        Assert.AreEqual(8, version.Minor);
        Assert.IsNull(version.Patch);
    }

    [TestMethod]
    public void TryParseAcceptsMajorMinorAndPatch()
    {
        var parsed = EngineVersion.TryParse("5.8.1", out var version);

        Assert.IsTrue(parsed);
        Assert.AreEqual(5, version.Major);
        Assert.AreEqual(8, version.Minor);
        Assert.AreEqual(1, version.Patch);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("5")]
    [DataRow("5.x")]
    [DataRow("5.8.1.2")]
    [DataRow("5.8-preview")]
    [DataRow(" 5.8 ")]
    public void TryParseRejectsValuesOutsideTheNumericMvpGrammar(string value)
    {
        Assert.IsFalse(EngineVersion.TryParse(value, out _));
    }

    [TestMethod]
    public void TryParseRejectsNull()
    {
        Assert.IsFalse(EngineVersion.TryParse(null, out _));
    }
}
