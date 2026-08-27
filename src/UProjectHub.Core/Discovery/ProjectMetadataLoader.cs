using UProjectHub.Core.Activity;
using UProjectHub.Core.Models;
using UProjectHub.Core.Parsing;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Versions;

namespace UProjectHub.Core.Discovery;

public sealed class ProjectMetadataLoader
{
    private static readonly DateTimeOffset UnknownLastModified =
        DateTimeOffset.MinValue;

    private readonly IUProjectParser _parser;
    private readonly ProjectActivityDetector _activityDetector;

    public ProjectMetadataLoader(
        IUProjectParser parser,
        ProjectActivityDetector activityDetector)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(activityDetector);
        _parser = parser;
        _activityDetector = activityDetector;
    }

    public async Task<ProjectMetadataLoadResult> LoadAsync(
        ProjectCandidate candidate,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(settings);

        cancellationToken.ThrowIfCancellationRequested();
        var projectName = Path.GetFileNameWithoutExtension(
            candidate.ProjectFilePath.Value);
        var userState = settings.ProjectUserStates.FirstOrDefault(state =>
            state.ProjectPath.Equals(candidate.ProjectFilePath));

        try
        {
            var parseResult = await _parser.ParseAsync(
                candidate.ProjectFilePath.Value,
                cancellationToken).ConfigureAwait(false);
            var lastModified = await _activityDetector.GetLastModifiedUtcAsync(
                candidate.ProjectFilePath.Value,
                cancellationToken).ConfigureAwait(false) ?? UnknownLastModified;

            if (!parseResult.IsSuccess || parseResult.Descriptor is null)
            {
                return CreateBrokenResult(
                    candidate,
                    projectName,
                    lastModified,
                    userState,
                    parseResult.ErrorMessage ?? "The project descriptor could not be parsed.");
            }

            var descriptor = parseResult.Descriptor;
            var displayVersion = EngineVersion.TryParse(
                descriptor.EngineAssociation,
                out _)
                ? descriptor.EngineAssociation
                : null;
            var project = new UnrealProject(
                projectName,
                candidate.ProjectFilePath,
                descriptor.EngineAssociation,
                displayVersion,
                ProjectClassifier.Classify(descriptor),
                lastModified,
                userState?.LastLaunched,
                userState?.IsFavorite ?? false,
                ProjectState.Available,
                EngineResolutionState.Unknown);

            return new ProjectMetadataLoadResult(project, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return CreateBrokenResult(
                candidate,
                projectName,
                UnknownLastModified,
                userState,
                exception.Message);
        }
    }

    private static ProjectMetadataLoadResult CreateBrokenResult(
        ProjectCandidate candidate,
        string projectName,
        DateTimeOffset lastModified,
        ProjectUserState? userState,
        string message)
    {
        var project = new UnrealProject(
            projectName,
            candidate.ProjectFilePath,
            EngineAssociation: null,
            EngineDisplayVersion: null,
            ProjectType.Blueprint,
            lastModified,
            userState?.LastLaunched,
            userState?.IsFavorite ?? false,
            ProjectState.Broken,
            EngineResolutionState.Unknown);
        var issue = new ProjectDiscoveryIssue(
            candidate.ProjectFilePath.Value,
            ProjectDiscoveryIssueKind.MetadataLoad,
            message);

        return new ProjectMetadataLoadResult(project, issue);
    }
}
