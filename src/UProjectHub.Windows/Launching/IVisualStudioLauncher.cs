using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Launching;

public interface IVisualStudioLauncher
{
    bool CanOpenSolution(UnrealProject project);

    VisualStudioSolutionSelection LocateSolution(UnrealProject project);

    LaunchResult OpenSolution(UnrealProject project);
}
