using System.Security;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Versions;

namespace UProjectHub.Windows.Projects;

public sealed class UnrealKnownProjectRootProvider : IUnrealKnownProjectRootProvider
{
    private static readonly EnumerationOptions VersionDirectoryOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static readonly string[] ConfigPlatformDirectories =
    [
        "WindowsEditor",
        "Windows",
    ];

    private readonly string _localApplicationDataDirectory;
    private readonly UnrealEditorSettingsParser _settingsParser;

    public UnrealKnownProjectRootProvider()
        : this(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            new UnrealEditorSettingsParser())
    {
    }

    public UnrealKnownProjectRootProvider(
        string localApplicationDataDirectory,
        UnrealEditorSettingsParser settingsParser)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);
        ArgumentNullException.ThrowIfNull(settingsParser);
        _localApplicationDataDirectory = Path.GetFullPath(
            localApplicationDataDirectory);
        _settingsParser = settingsParser;
    }

    public async Task<UnrealKnownProjectRootsResult> GetKnownRootsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var roots = new List<ProjectPath>();
        var rootIdentities = new HashSet<ProjectPath>();
        var issues = new List<UnrealKnownProjectRootIssue>();
        var unrealEngineDirectory = Path.Combine(
            _localApplicationDataDirectory,
            "UnrealEngine");

        if (!Directory.Exists(unrealEngineDirectory))
        {
            return CreateResult(roots, issues);
        }

        IReadOnlyList<string> versionDirectories;
        try
        {
            versionDirectories = Directory
                .EnumerateDirectories(
                    unrealEngineDirectory,
                    "*",
                    VersionDirectoryOptions)
                .Where(IsVersionDirectory)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            issues.Add(new UnrealKnownProjectRootIssue(
                unrealEngineDirectory,
                exception.Message));
            return CreateResult(roots, issues);
        }

        foreach (var versionDirectory in versionDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var platformDirectory in ConfigPlatformDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var settingsFilePath = Path.Combine(
                    versionDirectory,
                    "Saved",
                    "Config",
                    platformDirectory,
                    "EditorSettings.ini");

                if (!File.Exists(settingsFilePath))
                {
                    continue;
                }

                await ReadSettingsAsync(
                    settingsFilePath,
                    roots,
                    rootIdentities,
                    issues,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return CreateResult(roots, issues);
    }

    private async Task ReadSettingsAsync(
        string settingsFilePath,
        ICollection<ProjectPath> roots,
        ISet<ProjectPath> rootIdentities,
        ICollection<UnrealKnownProjectRootIssue> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            var contents = await File.ReadAllTextAsync(
                settingsFilePath,
                cancellationToken).ConfigureAwait(false);
            foreach (var rawRoot in _settingsParser.ParseCreatedProjectPaths(contents))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddRoot(rawRoot, settingsFilePath, roots, rootIdentities, issues);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            issues.Add(new UnrealKnownProjectRootIssue(
                settingsFilePath,
                exception.Message));
        }
    }

    private static void AddRoot(
        string rawRoot,
        string settingsFilePath,
        ICollection<ProjectPath> roots,
        ISet<ProjectPath> rootIdentities,
        ICollection<UnrealKnownProjectRootIssue> issues)
    {
        try
        {
            if (!Path.IsPathFullyQualified(rawRoot))
            {
                throw new ArgumentException(
                    "CreatedProjectPaths must contain an absolute path.",
                    nameof(rawRoot));
            }

            var root = new ProjectPath(
                Path.TrimEndingDirectorySeparator(rawRoot));
            if (rootIdentities.Add(root))
            {
                roots.Add(root);
            }
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or SecurityException)
        {
            issues.Add(new UnrealKnownProjectRootIssue(
                settingsFilePath,
                exception.Message));
        }
    }

    private static bool IsVersionDirectory(string path) =>
        EngineVersion.TryParse(Path.GetFileName(path), out _);

    private static UnrealKnownProjectRootsResult CreateResult(
        List<ProjectPath> roots,
        List<UnrealKnownProjectRootIssue> issues) =>
        new(
            Array.AsReadOnly(roots.ToArray()),
            Array.AsReadOnly(issues.ToArray()));

    private static bool IsExpectedReadFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException;
}
