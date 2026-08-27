using System.ComponentModel;
using System.Diagnostics;
using System.Security;

namespace UProjectHub.Windows.Launching;

public sealed class ProcessLauncher : IProcessLauncher
{
    public LaunchResult Launch(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = request.UseShellExecute,
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.ArgumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            return process is null
                ? LaunchResult.Failed("The process could not be started.")
                : LaunchResult.Succeeded();
        }
        catch (Exception exception) when (IsExpectedLaunchFailure(exception))
        {
            return LaunchResult.Failed(exception.Message);
        }
    }

    private static bool IsExpectedLaunchFailure(Exception exception) =>
        exception is Win32Exception
            or InvalidOperationException
            or ArgumentException
            or IOException
            or SecurityException;
}
