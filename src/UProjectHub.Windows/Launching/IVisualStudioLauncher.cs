using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Launching;

public interface IVisualStudioLauncher
{
    LaunchResult OpenSolution(UnrealProject project);
}
