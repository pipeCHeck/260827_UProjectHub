using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;

namespace UProjectHub.Core.Tests.Engines;

[TestClass]
public sealed class EngineResolverTests
{
    private static readonly Guid MatchingGuid =
        Guid.Parse("a1b2c3d4-e5f6-47a8-9b0c-1d2e3f4a5b6c");

    [TestMethod]
    [DataRow("5.8")]
    [DataRow("5.8.1")]
    public void NumericFamilyWithOneUsableCandidateIsResolved(
        string installedDisplayVersion)
    {
        var engine = CreateEngine(
            displayVersion: installedDisplayVersion,
            association: "not-used-for-numeric-matching");

        var resolution = EngineResolver.Resolve("5.8", [engine]);

        Assert.AreEqual(EngineResolutionState.Resolved, resolution.State);
        Assert.AreSame(engine, resolution.ResolvedCandidate);
        Assert.HasCount(1, resolution.MatchingCandidates);
        Assert.AreSame(engine, resolution.MatchingCandidates[0]);
    }

    [TestMethod]
    public void MultipleUsableCandidatesInNumericFamilyAreAmbiguous()
    {
        var first = CreateEngine("5.8.1");
        var second = CreateEngine("5.8.2");

        var resolution = EngineResolver.Resolve("5.8", [first, second]);

        Assert.AreEqual(EngineResolutionState.Ambiguous, resolution.State);
        Assert.IsNull(resolution.ResolvedCandidate);
        Assert.HasCount(2, resolution.MatchingCandidates);
    }

    [TestMethod]
    public void DifferentNumericVersionsDoNotFallback()
    {
        var resolution = EngineResolver.Resolve(
            "5.8",
            [CreateEngine("5.7"), CreateEngine("5.9"), CreateEngine("5.10")]);

        AssertMissing(resolution);
    }

    [TestMethod]
    public void UnusableCandidateDoesNotMatch()
    {
        var resolution = EngineResolver.Resolve(
            "5.8",
            [CreateEngine("5.8", isUsable: false)]);

        AssertMissing(resolution);
    }

    [TestMethod]
    public void UnusableCandidateDoesNotMakeUsableMatchAmbiguous()
    {
        var usable = CreateEngine("5.8", isUsable: true);
        var unusable = CreateEngine("5.8.1", isUsable: false);

        var resolution = EngineResolver.Resolve("5.8", [usable, unusable]);

        Assert.AreEqual(EngineResolutionState.Resolved, resolution.State);
        Assert.AreSame(usable, resolution.ResolvedCandidate);
        Assert.HasCount(1, resolution.MatchingCandidates);
    }

    [TestMethod]
    public void ProviderSourceDoesNotChooseBetweenMatchingCandidates()
    {
        var launcher = CreateEngine("5.8", source: EngineSource.Launcher);
        var manual = CreateEngine("5.8.2", source: EngineSource.Manual);

        var resolution = EngineResolver.Resolve("5.8", [launcher, manual]);

        Assert.AreEqual(EngineResolutionState.Ambiguous, resolution.State);
        Assert.IsNull(resolution.ResolvedCandidate);
        Assert.HasCount(2, resolution.MatchingCandidates);
    }

    [TestMethod]
    public void ExactGuidMatchIsResolvedAcrossFormattingAndCase()
    {
        var matching = CreateEngine(
            displayVersion: "5.8",
            association: MatchingGuid.ToString("B").ToUpperInvariant(),
            source: EngineSource.SourceBuild);

        var resolution = EngineResolver.Resolve(
            MatchingGuid.ToString("D").ToLowerInvariant(),
            [matching]);

        Assert.AreEqual(EngineResolutionState.Resolved, resolution.State);
        Assert.AreSame(matching, resolution.ResolvedCandidate);
        Assert.HasCount(1, resolution.MatchingCandidates);
    }

    [TestMethod]
    public void DifferentGuidDoesNotMatch()
    {
        var resolution = EngineResolver.Resolve(
            MatchingGuid.ToString("D"),
            [CreateEngine("5.8", Guid.NewGuid().ToString("D"))]);

        AssertMissing(resolution);
    }

    [TestMethod]
    public void MultipleExactGuidMatchesAreAmbiguous()
    {
        var first = CreateEngine(
            "5.8",
            MatchingGuid.ToString("D"),
            EngineSource.SourceBuild);
        var second = CreateEngine(
            "5.8.1",
            MatchingGuid.ToString("B").ToUpperInvariant(),
            EngineSource.Manual);

        var resolution = EngineResolver.Resolve(
            MatchingGuid.ToString("N"),
            [first, second]);

        Assert.AreEqual(EngineResolutionState.Ambiguous, resolution.State);
        Assert.IsNull(resolution.ResolvedCandidate);
        Assert.HasCount(2, resolution.MatchingCandidates);
    }

    [TestMethod]
    public void InvalidAssociationIsUnknownRegardlessOfCandidates()
    {
        string?[] invalidAssociations = [null, string.Empty, "invalid"];

        foreach (var rawAssociation in invalidAssociations)
        {
            var resolution = EngineResolver.Resolve(
                rawAssociation,
                [CreateEngine("5.8")]);

            Assert.AreEqual(EngineResolutionState.Unknown, resolution.State);
            Assert.IsNull(resolution.ResolvedCandidate);
            Assert.HasCount(0, resolution.MatchingCandidates);
        }
    }

    private static InstalledEngine CreateEngine(
        string? displayVersion,
        string? association = null,
        EngineSource source = EngineSource.Launcher,
        bool isUsable = true) =>
        new(
            DisplayName: $"Unreal Engine {displayVersion}",
            Association: association,
            DisplayVersion: displayVersion,
            RootPath: $"C:\\Engines\\{Guid.NewGuid():N}",
            EditorPath: $"C:\\Engines\\{Guid.NewGuid():N}\\UnrealEditor.exe",
            Source: source,
            IsUsable: isUsable);

    private static void AssertMissing(EngineResolution resolution)
    {
        Assert.AreEqual(EngineResolutionState.Missing, resolution.State);
        Assert.IsNull(resolution.ResolvedCandidate);
        Assert.HasCount(0, resolution.MatchingCandidates);
    }
}
