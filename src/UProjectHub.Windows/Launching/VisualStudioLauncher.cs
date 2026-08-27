using System.Security;
using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Launching;

public sealed class VisualStudioLauncher : IVisualStudioLauncher
{
    private readonly IProcessLauncher _processLauncher;

    public VisualStudioLauncher(IProcessLauncher processLauncher)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        _processLauncher = processLauncher;
    }

    public LaunchResult OpenSolution(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.ProjectType != ProjectType.Cpp)
        {
            return LaunchResult.Failed(
                "Open in Visual Studio is available only for C++ projects.");
        }

        if (string.IsNullOrWhiteSpace(project.ProjectDirectory)
            || !Directory.Exists(project.ProjectDirectory))
        {
            return LaunchResult.Failed("The project directory was not found.");
        }

        string[] solutions;
        try
        {
            solutions = Directory
                .EnumerateFiles(
                    project.ProjectDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Where(filePath => string.Equals(
                    Path.GetExtension(filePath),
                    ".sln",
                    StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFullPath)
                .ToArray();
        }
        catch (Exception exception) when (IsExpectedEnumerationFailure(exception))
        {
            return LaunchResult.Failed(exception.Message);
        }

        var namedSolution = solutions.FirstOrDefault(solution =>
            string.Equals(
                Path.GetFileNameWithoutExtension(solution),
                project.Name,
                StringComparison.OrdinalIgnoreCase));
        var selectedSolution = namedSolution
            ?? (solutions.Length == 1 ? solutions[0] : null);

        if (selectedSolution is null)
        {
            return LaunchResult.Failed(
                solutions.Length == 0
                    ? "No existing Visual Studio solution was found."
                    : "More than one Visual Studio solution is available.");
        }

        return _processLauncher.Launch(new ProcessRequest(
            fileName: selectedSolution,
            useShellExecute: true));
    }

    private static bool IsExpectedEnumerationFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException;
}
