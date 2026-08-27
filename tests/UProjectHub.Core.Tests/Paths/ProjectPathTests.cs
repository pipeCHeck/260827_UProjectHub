using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Tests.Paths;

[TestClass]
public sealed class ProjectPathTests
{
    [TestMethod]
    public void SlashRepresentationsHaveTheSameIdentity()
    {
        var backslashPath = new ProjectPath(@"C:\Projects\Sample\Sample.uproject");
        var forwardSlashPath = new ProjectPath("C:/Projects/Sample/Sample.uproject");

        Assert.AreEqual(backslashPath, forwardSlashPath);
        Assert.AreEqual(backslashPath.GetHashCode(), forwardSlashPath.GetHashCode());
    }

    [TestMethod]
    public void CaseDifferencesHaveTheSameIdentity()
    {
        var mixedCasePath = new ProjectPath(@"C:\Projects\Sample\Sample.uproject");
        var lowerCasePath = new ProjectPath(@"c:\projects\sample\sample.uproject");

        Assert.AreEqual(mixedCasePath, lowerCasePath);
        Assert.AreEqual(mixedCasePath.GetHashCode(), lowerCasePath.GetHashCode());
    }

    [TestMethod]
    public void RelativeAndNormalizableSegmentsHaveTheSameIdentity()
    {
        var absolutePath = Path.Combine(
            Environment.CurrentDirectory,
            "Projects",
            "Sample",
            "Sample.uproject");
        var relativePath = Path.Combine(
            ".",
            "Projects",
            "IntermediateFolder",
            "..",
            "Sample",
            ".",
            "Sample.uproject");

        var canonical = new ProjectPath(absolutePath);
        var relative = new ProjectPath(relativePath);

        Assert.AreEqual(canonical, relative);
        Assert.AreEqual(canonical.GetHashCode(), relative.GetHashCode());
    }
}
