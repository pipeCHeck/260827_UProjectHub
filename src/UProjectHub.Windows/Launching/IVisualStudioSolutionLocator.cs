using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Launching;

public interface IVisualStudioSolutionLocator
{
    VisualStudioSolutionSelection Locate(UnrealProject project);
}

public enum VisualStudioSolutionState
{
    Available,
    Missing,
    Multiple,
    Inaccessible,
}

public sealed record VisualStudioSolutionSelection(
    VisualStudioSolutionState State,
    string? SolutionPath,
    IReadOnlyList<string> CandidatePaths,
    string? ErrorMessage)
{
    public static VisualStudioSolutionSelection Available(
        string solutionPath,
        IReadOnlyList<string> candidatePaths) =>
        new(
            VisualStudioSolutionState.Available,
            solutionPath,
            candidatePaths,
            ErrorMessage: null);

    public static VisualStudioSolutionSelection Missing() =>
        new(
            VisualStudioSolutionState.Missing,
            SolutionPath: null,
            CandidatePaths: [],
            ErrorMessage: null);

    public static VisualStudioSolutionSelection Multiple(
        IReadOnlyList<string> candidatePaths) =>
        new(
            VisualStudioSolutionState.Multiple,
            SolutionPath: null,
            candidatePaths,
            ErrorMessage: null);

    public static VisualStudioSolutionSelection Inaccessible(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new VisualStudioSolutionSelection(
            VisualStudioSolutionState.Inaccessible,
            SolutionPath: null,
            CandidatePaths: [],
            errorMessage);
    }
}
