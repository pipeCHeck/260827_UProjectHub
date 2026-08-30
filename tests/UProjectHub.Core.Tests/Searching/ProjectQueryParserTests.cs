using UProjectHub.Core.Models;
using UProjectHub.Core.Searching;

namespace UProjectHub.Core.Tests.Searching;

[TestClass]
public sealed class ProjectQueryParserTests
{
    private readonly ProjectQueryParser _parser = new();

    [TestMethod]
    public void VersionTokenProducesVersionTerm()
    {
        var query = _parser.Parse("version:5.8");

        Assert.HasCount(1, query.Terms);
        Assert.IsInstanceOfType<VersionTerm>(query.Terms[0]);
        Assert.AreEqual("5.8", ((VersionTerm)query.Terms[0]).Value);
    }

    [TestMethod]
    public void CppTypeTokenProducesCppProjectTypeTerm()
    {
        var query = _parser.Parse("type:cpp");

        Assert.HasCount(1, query.Terms);
        Assert.IsInstanceOfType<ProjectTypeTerm>(query.Terms[0]);
        Assert.AreEqual(ProjectType.Cpp, ((ProjectTypeTerm)query.Terms[0]).ProjectType);
    }

    [TestMethod]
    public void BlueprintTypeTokenProducesBlueprintProjectTypeTerm()
    {
        var query = _parser.Parse("type:bp");

        Assert.HasCount(1, query.Terms);
        Assert.IsInstanceOfType<ProjectTypeTerm>(query.Terms[0]);
        Assert.AreEqual(
            ProjectType.Blueprint,
            ((ProjectTypeTerm)query.Terms[0]).ProjectType);
    }

    [TestMethod]
    public void PathTokenProducesPathTerm()
    {
        var query = _parser.Parse("path:Game");

        Assert.HasCount(1, query.Terms);
        Assert.IsInstanceOfType<PathTerm>(query.Terms[0]);
        Assert.AreEqual("Game", ((PathTerm)query.Terms[0]).Value);
    }

    [TestMethod]
    public void ModifiedTokenPreservesPositiveDayWindow()
    {
        var query = _parser.Parse("modified:7d");

        Assert.HasCount(1, query.Terms);
        Assert.IsInstanceOfType<ModifiedWithinTerm>(query.Terms[0]);
        Assert.AreEqual(7, ((ModifiedWithinTerm)query.Terms[0]).Days);
    }

    [TestMethod]
    public void FavoriteTokenProducesFavoriteTerm()
    {
        var query = _parser.Parse("favorite:true");

        Assert.HasCount(1, query.Terms);
        Assert.IsInstanceOfType<FavoriteTerm>(query.Terms[0]);
        Assert.IsTrue(((FavoriteTerm)query.Terms[0]).IsFavorite);
    }

    [TestMethod]
    public void TagAndNoteTokensProduceMetadataTerms()
    {
        var query = _parser.Parse("tag:\"Client Work\" note:prototype");

        Assert.HasCount(2, query.Terms);
        Assert.AreEqual(new TagTerm("Client Work"), query.Terms[0]);
        Assert.AreEqual(new NoteTerm("prototype"), query.Terms[1]);
    }

    [TestMethod]
    public void QuotedPathWithSpacesProducesOnePathTerm()
    {
        var query = _parser.Parse("path:\"D:\\Game Academy\"");

        Assert.HasCount(1, query.Terms);
        Assert.IsInstanceOfType<PathTerm>(query.Terms[0]);
        Assert.AreEqual(@"D:\Game Academy", ((PathTerm)query.Terms[0]).Value);
    }

    [TestMethod]
    public void UnknownPrefixFallsBackToOriginalPlainText()
    {
        var query = _parser.Parse("foo:bar");

        Assert.HasCount(1, query.Terms);
        Assert.IsInstanceOfType<PlainTextTerm>(query.Terms[0]);
        Assert.AreEqual("foo:bar", ((PlainTextTerm)query.Terms[0]).Text);
    }

    [TestMethod]
    [DataRow("type:java")]
    [DataRow("modified:abc")]
    [DataRow("modified:0d")]
    [DataRow("favorite:maybe")]
    public void InvalidKnownTokenFallsBackToOriginalPlainText(string token)
    {
        var query = _parser.Parse(token);

        Assert.HasCount(1, query.Terms);
        Assert.IsInstanceOfType<PlainTextTerm>(query.Terms[0]);
        Assert.AreEqual(token, ((PlainTextTerm)query.Terms[0]).Text);
    }

    [TestMethod]
    public void InvalidTokenDoesNotStopRemainingTerms()
    {
        var query = _parser.Parse("version:5.8 type:java hello");

        Assert.HasCount(3, query.Terms);
        Assert.AreEqual(new VersionTerm("5.8"), query.Terms[0]);
        Assert.AreEqual(new PlainTextTerm("type:java"), query.Terms[1]);
        Assert.AreEqual(new PlainTextTerm("hello"), query.Terms[2]);
    }

    [TestMethod]
    public void PlainTextWordsProduceIndependentTerms()
    {
        var query = _parser.Parse("hello world");

        Assert.HasCount(2, query.Terms);
        Assert.AreEqual(new PlainTextTerm("hello"), query.Terms[0]);
        Assert.AreEqual(new PlainTextTerm("world"), query.Terms[1]);
    }

    [TestMethod]
    public void StructuredAndPlainTextTermsCanCoexist()
    {
        var query = _parser.Parse("path:Game prototype favorite:true");

        Assert.HasCount(3, query.Terms);
        Assert.AreEqual(new PathTerm("Game"), query.Terms[0]);
        Assert.AreEqual(new PlainTextTerm("prototype"), query.Terms[1]);
        Assert.AreEqual(new FavoriteTerm(true), query.Terms[2]);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   \t  ")]
    public void EmptyOrWhitespaceQueryProducesNoTerms(string text)
    {
        var query = _parser.Parse(text);

        Assert.IsEmpty(query.Terms);
    }
}
