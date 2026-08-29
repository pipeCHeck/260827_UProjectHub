using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Launching;

public sealed class VisualStudioLauncher : IVisualStudioLauncher
{
    private readonly IProcessLauncher _processLauncher;
    private readonly IVisualStudioSolutionLocator _solutionLocator;

    public VisualStudioLauncher(IProcessLauncher processLauncher)
        : this(processLauncher, new VisualStudioSolutionLocator())
    {
    }

    public VisualStudioLauncher(
        IProcessLauncher processLauncher,
        IVisualStudioSolutionLocator solutionLocator)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        ArgumentNullException.ThrowIfNull(solutionLocator);
        _processLauncher = processLauncher;
        _solutionLocator = solutionLocator;
    }

    public bool CanOpenSolution(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.ProjectType == ProjectType.Cpp
            && _solutionLocator.Locate(project).State
            == VisualStudioSolutionState.Available;
    }

    public VisualStudioSolutionSelection LocateSolution(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return _solutionLocator.Locate(project);
    }

    public LaunchResult OpenSolution(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.ProjectType != ProjectType.Cpp)
        {
            return LaunchResult.Failed(
                "Open in Visual Studio is available only for C++ projects.");
        }

        var selection = LocateSolution(project);
        if (selection.State != VisualStudioSolutionState.Available
            || selection.SolutionPath is null)
        {
            return LaunchResult.Failed(GetUnavailableMessage(selection));
        }

        return _processLauncher.Launch(new ProcessRequest(
            fileName: selection.SolutionPath,
            useShellExecute: true));
    }

    private static string GetUnavailableMessage(
        VisualStudioSolutionSelection selection)
    {
        return selection.State switch
        {
            VisualStudioSolutionState.Missing =>
                "No existing Visual Studio solution was found.",
            VisualStudioSolutionState.Multiple =>
                "More than one Visual Studio solution is available.",
            VisualStudioSolutionState.Inaccessible =>
                selection.ErrorMessage
                    ?? "The project directory could not be inspected.",
            _ => "The Visual Studio solution is unavailable.",
        };
    }
}
