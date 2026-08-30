using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Launching;

public interface IProjectFilesGenerator
{
    ProjectFileGenerationPreparation Prepare(
        UnrealProject project,
        InstalledEngine engine);

    Task<ProjectFileGenerationResult> GenerateAsync(
        ProjectFileGenerationRequest request,
        CancellationToken cancellationToken = default);

    Task<ProjectFileGenerationResult> GenerateAsync(
        ProjectFileGenerationRequest request,
        CancellationToken cancellationToken,
        IProgress<ExternalProcessOutput>? outputProgress) =>
        GenerateAsync(request, cancellationToken);
}

public sealed record ProjectFileGenerationPreparation(
    bool CanGenerate,
    ProjectFileGenerationRequest? Request,
    string? UnavailableReason)
{
    public static ProjectFileGenerationPreparation Available(
        ProjectFileGenerationRequest request) =>
        new(true, request, UnavailableReason: null);

    public static ProjectFileGenerationPreparation Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new ProjectFileGenerationPreparation(
            false,
            Request: null,
            reason);
    }
}

public sealed record ProjectFileGenerationRequest(
    UnrealProject Project,
    InstalledEngine Engine,
    ExternalProcessRequest Process,
    string ExpectedSolutionPath);

public enum ProjectFileGenerationStatus
{
    Succeeded,
    NonZeroExit,
    FailedToStart,
    Cancelled,
    AlreadyRunning,
}

public sealed record ProjectFileGenerationResult(
    ProjectFileGenerationStatus Status,
    int? ExitCode,
    string StandardOutputTail,
    string StandardErrorTail,
    string? ErrorMessage,
    VisualStudioSolutionSelection? SolutionSelection)
{
    public bool IsSuccess => Status == ProjectFileGenerationStatus.Succeeded;
}
