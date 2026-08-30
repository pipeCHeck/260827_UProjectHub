using UProjectHub.App.Services;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ProjectTagIndexTests
{
    [TestMethod]
    public void RebuildDeduplicatesCaseInsensitivelyAndPreservesFirstCasing()
    {
        var index = new ProjectTagIndex();

        index.Rebuild([
            CreateProject("Available", ["Prototype", "게임인재원8기"]),
            CreateProject("Missing", ["prototype", "Archive"], ProjectState.Missing),
        ]);

        CollectionAssert.AreEqual(
            new[] { "Archive", "Prototype", "게임인재원8기" },
            index.KnownTags.ToArray());
    }

    [TestMethod]
    public void SuggestionsPutPrefixMatchesBeforeContainsMatches()
    {
        var index = new ProjectTagIndex();
        index.Rebuild([
            CreateProject("One", ["Academy Alumni", "Game Academy", "Academy 8"]),
        ]);

        var suggestions = index.GetSuggestions("academy");

        CollectionAssert.AreEqual(
            new[] { "Academy 8", "Academy Alumni", "Game Academy" },
            suggestions.ToArray());
    }

    [TestMethod]
    public void EmptyInputDoesNotOpenAnUnboundedSuggestionList()
    {
        var index = new ProjectTagIndex();
        index.Rebuild([CreateProject("One", ["One", "Two"])]);

        Assert.IsEmpty(index.GetSuggestions("  "));
    }

    private static UnrealProject CreateProject(
        string name,
        IReadOnlyList<string> tags,
        ProjectState state = ProjectState.Available) =>
        new(
            name,
            new ProjectPath($@"D:\Projects\{name}\{name}.uproject"),
            "5.8",
            "5.8",
            ProjectType.Cpp,
            DateTimeOffset.UnixEpoch,
            LastLaunched: null,
            IsFavorite: false,
            state,
            EngineResolutionState.Resolved)
        {
            Tags = tags,
        };
}
