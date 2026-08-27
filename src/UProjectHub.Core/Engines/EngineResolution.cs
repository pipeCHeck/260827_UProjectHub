using System.Collections.ObjectModel;
using UProjectHub.Core.Models;

namespace UProjectHub.Core.Engines;

public sealed record EngineResolution
{
    private EngineResolution(
        EngineResolutionState state,
        InstalledEngine? resolvedCandidate,
        IReadOnlyList<InstalledEngine> matchingCandidates)
    {
        State = state;
        ResolvedCandidate = resolvedCandidate;
        MatchingCandidates = matchingCandidates;
    }

    public EngineResolutionState State { get; }

    public InstalledEngine? ResolvedCandidate { get; }

    public IReadOnlyList<InstalledEngine> MatchingCandidates { get; }

    internal static EngineResolution Unknown() =>
        new(
            EngineResolutionState.Unknown,
            resolvedCandidate: null,
            Array.Empty<InstalledEngine>());

    internal static EngineResolution FromMatches(
        IEnumerable<InstalledEngine> matchingCandidates)
    {
        var candidates = matchingCandidates.ToArray();
        var readOnlyCandidates = new ReadOnlyCollection<InstalledEngine>(candidates);

        return candidates.Length switch
        {
            0 => new EngineResolution(
                EngineResolutionState.Missing,
                resolvedCandidate: null,
                readOnlyCandidates),
            1 => new EngineResolution(
                EngineResolutionState.Resolved,
                candidates[0],
                readOnlyCandidates),
            _ => new EngineResolution(
                EngineResolutionState.Ambiguous,
                resolvedCandidate: null,
                readOnlyCandidates),
        };
    }
}
