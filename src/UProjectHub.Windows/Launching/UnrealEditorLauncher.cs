using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;
using UProjectHub.Core.Time;

namespace UProjectHub.Windows.Launching;

public sealed class UnrealEditorLauncher : IUnrealEditorLauncher
{
    private readonly IProcessLauncher _processLauncher;
    private readonly IClock _clock;

    public UnrealEditorLauncher(
        IProcessLauncher processLauncher,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        ArgumentNullException.ThrowIfNull(clock);

        _processLauncher = processLauncher;
        _clock = clock;
    }

    public LaunchResult Launch(
        UnrealProject project,
        EngineResolution engineResolution)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(engineResolution);

        if (engineResolution.State != EngineResolutionState.Resolved)
        {
            return LaunchResult.Failed(
                "The project's Unreal Engine is not uniquely resolved.");
        }

        var engine = engineResolution.ResolvedCandidate;
        if (engine is null || !engine.IsUsable)
        {
            return LaunchResult.Failed(
                "The resolved Unreal Engine is not usable.");
        }

        if (string.IsNullOrWhiteSpace(engine.EditorPath)
            || !File.Exists(engine.EditorPath))
        {
            return LaunchResult.Failed(
                "The resolved Unreal Editor executable was not found.");
        }

        var processResult = _processLauncher.Launch(new ProcessRequest(
            fileName: engine.EditorPath,
            argumentList: [project.ProjectFilePath.Value]));
        return processResult.IsSuccess
            ? LaunchResult.Succeeded(_clock.UtcNow)
            : processResult;
    }
}
