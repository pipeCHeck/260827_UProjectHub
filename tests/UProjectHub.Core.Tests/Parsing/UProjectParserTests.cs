using UProjectHub.Core.Parsing;

namespace UProjectHub.Core.Tests.Parsing;

[TestClass]
public sealed class UProjectParserTests
{
    [TestMethod]
    public async Task ValidDescriptorParsesRequiredFieldsAsync()
    {
        IUProjectParser parser = new UProjectParser();

        var result = await parser.ParseAsync(FixturePath("Cpp", "Cpp.uproject"));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Descriptor);
        Assert.AreEqual(3, result.Descriptor.FileVersion);
        Assert.AreEqual("5.8", result.Descriptor.EngineAssociation);
        Assert.IsNotNull(result.Descriptor.Modules);
        Assert.HasCount(1, result.Descriptor.Modules);
        Assert.AreEqual("Cpp", result.Descriptor.Modules[0].Name);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public async Task MalformedJsonReturnsFailureWithoutThrowingAsync()
    {
        IUProjectParser parser = new UProjectParser();

        var result = await parser.ParseAsync(
            FixturePath("Malformed", "Malformed.uproject"));

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Descriptor);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    private static string FixturePath(params string[] segments)
    {
        var fixtureRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Projects"));

        return Path.Combine([fixtureRoot, .. segments]);
    }
}
