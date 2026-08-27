using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Launching;

public interface IUnrealEditorLauncher
{
    LaunchResult Launch(
        UnrealProject project,
        EngineResolution engineResolution);
}
