using System.Security;
using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Launching;

public sealed class VisualStudioSolutionLocator : IVisualStudioSolutionLocator
{
    public VisualStudioSolutionSelection Locate(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (string.IsNullOrWhiteSpace(project.ProjectDirectory)
            || !Directory.Exists(project.ProjectDirectory))
        {
            return VisualStudioSolutionSelection.Inaccessible(
                "The project directory was not found.");
        }

        try
        {
            var solutions = Directory
                .EnumerateFiles(
                    project.ProjectDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Where(filePath => string.Equals(
                    Path.GetExtension(filePath),
                    ".sln",
                    StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var namedSolution = solutions.FirstOrDefault(solution =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(solution),
                    project.Name,
                    StringComparison.OrdinalIgnoreCase));
            if (namedSolution is not null)
            {
                return VisualStudioSolutionSelection.Available(
                    namedSolution,
                    Array.AsReadOnly(solutions));
            }

            return solutions.Length switch
            {
                0 => VisualStudioSolutionSelection.Missing(),
                1 => VisualStudioSolutionSelection.Available(
                    solutions[0],
                    Array.AsReadOnly(solutions)),
                _ => VisualStudioSolutionSelection.Multiple(
                    Array.AsReadOnly(solutions)),
            };
        }
        catch (Exception exception) when (IsExpectedEnumerationFailure(exception))
        {
            return VisualStudioSolutionSelection.Inaccessible(exception.Message);
        }
    }

    private static bool IsExpectedEnumerationFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException;
}
