using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Launching;

public sealed class ExplorerLauncher : IExplorerLauncher
{
    private const string ExplorerExecutable = "explorer.exe";

    private readonly IProcessLauncher _processLauncher;

    public ExplorerLauncher(IProcessLauncher processLauncher)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        _processLauncher = processLauncher;
    }

    public LaunchResult OpenProjectFolder(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (string.IsNullOrWhiteSpace(project.ProjectDirectory)
            || !Directory.Exists(project.ProjectDirectory))
        {
            return LaunchResult.Failed("The project directory was not found.");
        }

        return _processLauncher.Launch(new ProcessRequest(
            fileName: ExplorerExecutable,
            argumentList: [project.ProjectDirectory]));
    }

}
