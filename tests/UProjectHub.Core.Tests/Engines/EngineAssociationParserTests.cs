using UProjectHub.Core.Engines;
using UProjectHub.Core.Versions;

namespace UProjectHub.Core.Tests.Engines;

[TestClass]
public sealed class EngineAssociationParserTests
{
    [TestMethod]
    [DataRow("5.8", 5, 8, null)]
    [DataRow("5.10", 5, 10, null)]
    [DataRow("5.8.1", 5, 8, 1)]
    public void ParsesNumericAssociation(
        string rawAssociation,
        int expectedMajor,
        int expectedMinor,
        int? expectedPatch)
    {
        var association = EngineAssociationParser.Parse(rawAssociation);

        var numeric = Assert.IsInstanceOfType<NumericEngineAssociation>(association);
        Assert.AreEqual(
            new EngineVersion(expectedMajor, expectedMinor, expectedPatch),
            numeric.Version);
    }

    [TestMethod]
    public void NormalizesGuidCaseAndCommonFormatting()
    {
        const string bracedUppercase = "{A1B2C3D4-E5F6-47A8-9B0C-1D2E3F4A5B6C}";
        const string plainLowercase = "a1b2c3d4-e5f6-47a8-9b0c-1d2e3f4a5b6c";

        var braced = Assert.IsInstanceOfType<GuidEngineAssociation>(
            EngineAssociationParser.Parse(bracedUppercase));
        var plain = Assert.IsInstanceOfType<GuidEngineAssociation>(
            EngineAssociationParser.Parse(plainLowercase));

        Assert.AreEqual(braced.Identifier, plain.Identifier);
        Assert.AreEqual(
            "a1b2c3d4-e5f6-47a8-9b0c-1d2e3f4a5b6c",
            braced.Identifier.ToString("D"));
    }

    [TestMethod]
    public void NullEmptyAndInvalidAssociationsAreUnknown()
    {
        string?[] invalidAssociations =
        [
            null,
            string.Empty,
            "   ",
            "not-an-engine-association",
            "5",
            "5.x",
        ];

        foreach (var rawAssociation in invalidAssociations)
        {
            Assert.IsInstanceOfType<UnknownEngineAssociation>(
                EngineAssociationParser.Parse(rawAssociation));
        }
    }
}
