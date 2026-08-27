namespace UProjectHub.Windows.Launching;

public interface IProcessLauncher
{
    LaunchResult Launch(ProcessRequest request);
}
