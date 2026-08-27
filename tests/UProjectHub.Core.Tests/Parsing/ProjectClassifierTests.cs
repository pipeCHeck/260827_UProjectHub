using UProjectHub.Core.Models;
using UProjectHub.Core.Parsing;

namespace UProjectHub.Core.Tests.Parsing;

[TestClass]
public sealed class ProjectClassifierTests
{
    [TestMethod]
    public async Task NonEmptyModulesClassifiesAsCppAsync()
    {
        var descriptor = await ParseDescriptorAsync("Cpp", "Cpp.uproject");

        var projectType = ProjectClassifier.Classify(descriptor);

        Assert.AreEqual(ProjectType.Cpp, projectType);
    }

    [TestMethod]
    public async Task EmptyModulesClassifiesAsBlueprintAsync()
    {
        var descriptor = await ParseDescriptorAsync(
            "BlueprintEmptyModules",
            "BlueprintEmptyModules.uproject");

        var projectType = ProjectClassifier.Classify(descriptor);

        Assert.AreEqual(ProjectType.Blueprint, projectType);
    }

    [TestMethod]
    public async Task MissingModulesClassifiesAsBlueprintAsync()
    {
        var descriptor = await ParseDescriptorAsync(
            "BlueprintMissingModules",
            "BlueprintMissingModules.uproject");

        var projectType = ProjectClassifier.Classify(descriptor);

        Assert.AreEqual(ProjectType.Blueprint, projectType);
    }

    [TestMethod]
    public async Task SourceFolderDoesNotChangeMissingModulesClassificationAsync()
    {
        var projectDirectory = FixturePath("BlueprintSourceOnly");
        Assert.IsTrue(Directory.Exists(Path.Combine(projectDirectory, "Source")));

        var descriptor = await ParseDescriptorAsync(
            "BlueprintSourceOnly",
            "BlueprintSourceOnly.uproject");

        var projectType = ProjectClassifier.Classify(descriptor);

        Assert.AreEqual(ProjectType.Blueprint, projectType);
    }

    private static async Task<UProjectDescriptor> ParseDescriptorAsync(params string[] segments)
    {
        IUProjectParser parser = new UProjectParser();
        var result = await parser.ParseAsync(FixturePath(segments));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Descriptor);

        return result.Descriptor;
    }

    private static string FixturePath(params string[] segments)
    {
        var fixtureRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Projects"));

        return Path.Combine([fixtureRoot, .. segments]);
    }
}
