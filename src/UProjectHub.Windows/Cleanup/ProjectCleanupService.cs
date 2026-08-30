using System.Security;
using UProjectHub.Core.Models;
using UProjectHub.Windows.Launching;

namespace UProjectHub.Windows.Cleanup;

public sealed class ProjectCleanupService : IProjectCleanupService
{
    private static readonly IReadOnlyList<ProjectCleanupTargetKind> TargetOrder =
    [
        ProjectCleanupTargetKind.Intermediate,
        ProjectCleanupTargetKind.DerivedDataCache,
        ProjectCleanupTargetKind.VisualStudioWorkspace,
        ProjectCleanupTargetKind.Binaries,
        ProjectCleanupTargetKind.Solution,
    ];

    private readonly IVisualStudioSolutionLocator _solutionLocator;
    private readonly Func<string, FileAttributes> _attributeReader;

    public ProjectCleanupService(IVisualStudioSolutionLocator solutionLocator)
        : this(solutionLocator, File.GetAttributes)
    {
    }

    internal ProjectCleanupService(
        IVisualStudioSolutionLocator solutionLocator,
        Func<string, FileAttributes> attributeReader)
    {
        _solutionLocator = solutionLocator
            ?? throw new ArgumentNullException(nameof(solutionLocator));
        _attributeReader = attributeReader
            ?? throw new ArgumentNullException(nameof(attributeReader));
    }

    public Task<ProjectCleanupInspection> InspectAsync(
        UnrealProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Task.Run(() => Inspect(project, cancellationToken), cancellationToken);
    }

    public Task<ProjectCleanupResult> CleanupAsync(
        ProjectCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);
        ArgumentNullException.ThrowIfNull(request.Targets);
        return Task.Run(() => Cleanup(request, cancellationToken), cancellationToken);
    }

    private ProjectCleanupInspection Inspect(
        UnrealProject project,
        CancellationToken cancellationToken)
    {
        var root = GetValidatedProjectRoot(project);
        var items = TargetOrder
            .Select(kind => InspectItem(project, root, kind, cancellationToken))
            .ToArray();
        return new ProjectCleanupInspection(project, Array.AsReadOnly(items));
    }

    private ProjectCleanupItemInspection InspectItem(
        UnrealProject project,
        string root,
        ProjectCleanupTargetKind kind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (kind == ProjectCleanupTargetKind.Solution)
            {
                return InspectSolution(project, root, cancellationToken);
            }

            var path = GetDirectoryTarget(root, kind);
            if (!Directory.Exists(path))
            {
                return Inspection(kind, path, exists: false, canDelete: false);
            }

            var size = CalculateDirectorySize(root, path, cancellationToken);
            return Inspection(kind, path, exists: true, canDelete: true, size);
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
            return Inspection(
                kind,
                TryGetDisplayPath(project, root, kind),
                exists: PathExists(TryGetDisplayPath(project, root, kind)),
                canDelete: false,
                errorMessage: exception.Message);
        }
    }

    private ProjectCleanupItemInspection InspectSolution(
        UnrealProject project,
        string root,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (project.ProjectType != ProjectType.Cpp)
        {
            return Inspection(
                ProjectCleanupTargetKind.Solution,
                Path.Combine(root, $"{project.Name}.sln"),
                exists: false,
                canDelete: false,
                errorMessage: ".sln cleanup is available only for C++ projects.");
        }

        var selection = _solutionLocator.Locate(project);
        var candidates = selection.CandidatePaths
            .Select(Path.GetFullPath)
            .ToArray();
        if (selection.State == VisualStudioSolutionState.Available
            && selection.SolutionPath is { } solutionPath)
        {
            var validated = ValidateSolutionPath(root, solutionPath);
            if (!File.Exists(validated))
            {
                return Inspection(
                    ProjectCleanupTargetKind.Solution,
                    validated,
                    exists: false,
                    canDelete: false,
                    candidatePaths: candidates);
            }

            RejectReparsePoint(validated);
            return Inspection(
                ProjectCleanupTargetKind.Solution,
                validated,
                exists: true,
                canDelete: true,
                sizeBytes: new FileInfo(validated).Length,
                candidatePaths: candidates);
        }

        return selection.State switch
        {
            VisualStudioSolutionState.Missing => Inspection(
                ProjectCleanupTargetKind.Solution,
                Path.Combine(root, $"{project.Name}.sln"),
                exists: false,
                canDelete: false),
            VisualStudioSolutionState.Multiple => Inspection(
                ProjectCleanupTargetKind.Solution,
                path: null,
                exists: candidates.Length > 0,
                canDelete: false,
                errorMessage: "Multiple .sln files were found; no file was selected.",
                candidatePaths: candidates),
            _ => Inspection(
                ProjectCleanupTargetKind.Solution,
                path: null,
                exists: false,
                canDelete: false,
                errorMessage: selection.ErrorMessage
                    ?? "The project folder could not be inspected for .sln files.",
                candidatePaths: candidates),
        };
    }

    private ProjectCleanupResult Cleanup(
        ProjectCleanupRequest request,
        CancellationToken cancellationToken)
    {
        var root = GetValidatedProjectRoot(request.Project);
        var requested = request.Targets.Distinct().ToHashSet();
        var results = new List<ProjectCleanupItemResult>(requested.Count);

        foreach (var kind in TargetOrder.Where(requested.Contains))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(CleanupItem(request.Project, root, kind, cancellationToken));
        }

        return new ProjectCleanupResult(results.AsReadOnly());
    }

    private ProjectCleanupItemResult CleanupItem(
        UnrealProject project,
        string root,
        ProjectCleanupTargetKind kind,
        CancellationToken cancellationToken)
    {
        string? path = null;
        long freedBytes = 0;
        try
        {
            if (kind == ProjectCleanupTargetKind.Solution)
            {
                if (project.ProjectType != ProjectType.Cpp)
                {
                    return Result(
                        kind,
                        null,
                        ProjectCleanupItemStatus.Unavailable,
                        errorMessage: ".sln cleanup is available only for C++ projects.");
                }

                var selection = _solutionLocator.Locate(project);
                if (selection.State == VisualStudioSolutionState.Missing)
                {
                    return Result(kind, null, ProjectCleanupItemStatus.NotFound);
                }

                if (selection.State != VisualStudioSolutionState.Available
                    || selection.SolutionPath is null)
                {
                    return Result(
                        kind,
                        null,
                        ProjectCleanupItemStatus.Unavailable,
                        errorMessage: selection.State == VisualStudioSolutionState.Multiple
                            ? "Multiple .sln files were found; no file was deleted."
                            : selection.ErrorMessage
                                ?? "No uniquely identified .sln file is available.");
                }

                path = ValidateSolutionPath(root, selection.SolutionPath);
                if (!File.Exists(path))
                {
                    return Result(kind, path, ProjectCleanupItemStatus.NotFound);
                }

                RejectReparsePoint(path);
                var length = new FileInfo(path).Length;
                File.Delete(path);
                freedBytes = length;
                return Result(kind, path, ProjectCleanupItemStatus.Deleted, freedBytes);
            }

            path = GetDirectoryTarget(root, kind);
            ValidateDirectoryTarget(root, path, kind);
            if (!Directory.Exists(path))
            {
                return Result(kind, path, ProjectCleanupItemStatus.NotFound);
            }

            _ = CalculateDirectorySize(root, path, cancellationToken);
            DeleteDirectoryTree(root, path, cancellationToken, ref freedBytes);
            return Result(kind, path, ProjectCleanupItemStatus.Deleted, freedBytes);
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
            return Result(
                kind,
                path,
                ProjectCleanupItemStatus.Failed,
                freedBytes,
                exception.Message);
        }
    }

    private long CalculateDirectorySize(
        string root,
        string directoryPath,
        CancellationToken cancellationToken)
    {
        ValidateContainedPath(root, directoryPath);
        RejectReparsePoint(directoryPath);
        long total = 0;

        foreach (var entry in new DirectoryInfo(directoryPath).EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateContainedPath(root, entry.FullName);
            RejectReparsePoint(entry.FullName);
            if (entry is DirectoryInfo)
            {
                total = checked(total + CalculateDirectorySize(
                    root,
                    entry.FullName,
                    cancellationToken));
            }
            else
            {
                total = checked(total + ((FileInfo)entry).Length);
            }
        }

        return total;
    }

    private void DeleteDirectoryTree(
        string root,
        string directoryPath,
        CancellationToken cancellationToken,
        ref long freedBytes)
    {
        ValidateContainedPath(root, directoryPath);
        RejectReparsePoint(directoryPath);

        foreach (var entry in new DirectoryInfo(directoryPath).EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateContainedPath(root, entry.FullName);
            RejectReparsePoint(entry.FullName);
            if (entry is DirectoryInfo)
            {
                DeleteDirectoryTree(root, entry.FullName, cancellationToken, ref freedBytes);
            }
            else
            {
                var length = ((FileInfo)entry).Length;
                File.Delete(entry.FullName);
                freedBytes = checked(freedBytes + length);
            }
        }

        Directory.Delete(directoryPath, recursive: false);
    }

    private string GetValidatedProjectRoot(UnrealProject project)
    {
        if (project.ProjectState != ProjectState.Available)
        {
            throw new InvalidOperationException("The project is not available for cleanup.");
        }

        var root = Path.GetFullPath(project.ProjectDirectory);
        var descriptor = Path.GetFullPath(project.ProjectFilePath.Value);
        if (!Directory.Exists(root)
            || !File.Exists(descriptor)
            || !string.Equals(
                Path.GetDirectoryName(descriptor),
                root,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetExtension(descriptor),
                ".uproject",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The project root and descriptor could not be validated.");
        }

        RejectReparsePoint(root);
        return root;
    }

    private static string GetDirectoryTarget(
        string root,
        ProjectCleanupTargetKind kind)
    {
        var name = kind switch
        {
            ProjectCleanupTargetKind.Intermediate => "Intermediate",
            ProjectCleanupTargetKind.DerivedDataCache => "DerivedDataCache",
            ProjectCleanupTargetKind.VisualStudioWorkspace => ".vs",
            ProjectCleanupTargetKind.Binaries => "Binaries",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return Path.GetFullPath(Path.Combine(root, name));
    }

    private static void ValidateDirectoryTarget(
        string root,
        string path,
        ProjectCleanupTargetKind kind)
    {
        var expected = GetDirectoryTarget(root, kind);
        if (!string.Equals(expected, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetDirectoryName(expected),
                root,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The cleanup path is not an allowed project-root target.");
        }
    }

    private static string ValidateSolutionPath(string root, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".sln", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetDirectoryName(fullPath),
                root,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected .sln is not a top-level file in this project.");
        }

        ValidateContainedPath(root, fullPath);
        return fullPath;
    }

    private static void ValidateContainedPath(string root, string path)
    {
        var canonicalRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var canonicalPath = Path.GetFullPath(path);
        var prefix = canonicalRoot + Path.DirectorySeparatorChar;
        if (!canonicalPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The cleanup path is outside the project root.");
        }
    }

    private void RejectReparsePoint(string path)
    {
        if ((_attributeReader(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Cleanup was blocked because a reparse point was found: {path}");
        }
    }

    private static ProjectCleanupItemInspection Inspection(
        ProjectCleanupTargetKind kind,
        string? path,
        bool exists,
        bool canDelete,
        long sizeBytes = 0,
        string? errorMessage = null,
        IReadOnlyList<string>? candidatePaths = null) =>
        new(
            kind,
            path,
            exists,
            canDelete,
            sizeBytes,
            errorMessage,
            candidatePaths ?? Array.Empty<string>());

    private static ProjectCleanupItemResult Result(
        ProjectCleanupTargetKind kind,
        string? path,
        ProjectCleanupItemStatus status,
        long freedBytes = 0,
        string? errorMessage = null) =>
        new(kind, path, status, freedBytes, errorMessage);

    private static string? TryGetDisplayPath(
        UnrealProject project,
        string root,
        ProjectCleanupTargetKind kind)
    {
        try
        {
            return kind == ProjectCleanupTargetKind.Solution
                ? Path.Combine(root, $"{project.Name}.sln")
                : GetDirectoryTarget(root, kind);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool PathExists(string? path) =>
        path is not null && (Directory.Exists(path) || File.Exists(path));

    private static bool IsExpectedFileSystemFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException
            or InvalidOperationException
            or OverflowException;
}
