using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Launching;

public interface IExplorerLauncher
{
    LaunchResult OpenProjectFolder(UnrealProject project);

    LaunchResult RevealProjectFile(UnrealProject project);
}
