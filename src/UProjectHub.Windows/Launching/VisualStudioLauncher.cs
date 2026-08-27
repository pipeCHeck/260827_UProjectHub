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

    public bool CanOpenSolution(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return SelectSolution(project).SolutionPath is not null;
    }

    public LaunchResult OpenSolution(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var selection = SelectSolution(project);
        if (selection.SolutionPath is null)
        {
            return LaunchResult.Failed(selection.ErrorMessage!);
        }

        return _processLauncher.Launch(new ProcessRequest(
            fileName: selection.SolutionPath,
            useShellExecute: true));
    }

    private static SolutionSelection SelectSolution(UnrealProject project)
    {
        if (project.ProjectType != ProjectType.Cpp)
        {
            return SolutionSelection.Unavailable(
                "Open in Visual Studio is available only for C++ projects.");
        }

        if (string.IsNullOrWhiteSpace(project.ProjectDirectory)
            || !Directory.Exists(project.ProjectDirectory))
        {
            return SolutionSelection.Unavailable(
                "The project directory was not found.");
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
            return SolutionSelection.Unavailable(exception.Message);
        }

        var namedSolution = solutions.FirstOrDefault(solution =>
            string.Equals(
                Path.GetFileNameWithoutExtension(solution),
                project.Name,
                StringComparison.OrdinalIgnoreCase));
        if (namedSolution is not null)
        {
            return SolutionSelection.Available(namedSolution);
        }

        return solutions.Length switch
        {
            1 => SolutionSelection.Available(solutions[0]),
            0 => SolutionSelection.Unavailable(
                "No existing Visual Studio solution was found."),
            _ => SolutionSelection.Unavailable(
                "More than one Visual Studio solution is available."),
        };
    }

    private static bool IsExpectedEnumerationFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException;

    private sealed record SolutionSelection(
        string? SolutionPath,
        string? ErrorMessage)
    {
        public static SolutionSelection Available(string solutionPath) =>
            new(solutionPath, null);

        public static SolutionSelection Unavailable(string errorMessage) =>
            new(null, errorMessage);
    }
}
