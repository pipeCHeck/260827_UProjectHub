using System.Collections.Concurrent;
using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Launching;

public sealed class UnrealProjectFilesGenerator : IProjectFilesGenerator
{
    private readonly IExternalProcessRunner _processRunner;
    private readonly IVisualStudioSolutionLocator _solutionLocator;
    private readonly ConcurrentDictionary<string, byte> _runningProjects =
        new(StringComparer.OrdinalIgnoreCase);

    public UnrealProjectFilesGenerator(
        IExternalProcessRunner processRunner,
        IVisualStudioSolutionLocator solutionLocator)
    {
        _processRunner = processRunner
            ?? throw new ArgumentNullException(nameof(processRunner));
        _solutionLocator = solutionLocator
            ?? throw new ArgumentNullException(nameof(solutionLocator));
    }

    public ProjectFileGenerationPreparation Prepare(
        UnrealProject project,
        InstalledEngine engine)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(engine);

        var engineRoot = Path.GetFullPath(engine.RootPath);
        var ubtPath = Path.Combine(
            engineRoot,
            "Engine",
            "Binaries",
            "DotNET",
            "UnrealBuildTool",
            "UnrealBuildTool.exe");
        if (!File.Exists(ubtPath))
        {
            return ProjectFileGenerationPreparation.Unavailable(
                "The resolved Unreal Engine installation has no runnable UnrealBuildTool executable.");
        }

        var arguments = new List<string>
        {
            "-ProjectFiles",
            $"-Project={project.ProjectFilePath.Value}",
            "-Game",
            "-Progress",
        };
        if (engine.Source == EngineSource.Launcher
            && File.Exists(Path.Combine(
            engineRoot,
            "Engine",
            "Build",
            "InstalledBuild.txt")))
        {
            arguments.Add("-Rocket");
        }

        var request = new ProjectFileGenerationRequest(
            project,
            engine,
            new ExternalProcessRequest(
                ubtPath,
                arguments,
                workingDirectory: engineRoot),
            Path.Combine(project.ProjectDirectory, $"{project.Name}.sln"));
        return ProjectFileGenerationPreparation.Available(request);
    }

    public Task<ProjectFileGenerationResult> GenerateAsync(
        ProjectFileGenerationRequest request,
        CancellationToken cancellationToken = default) =>
        GenerateAsync(request, cancellationToken, outputProgress: null);

    public async Task<ProjectFileGenerationResult> GenerateAsync(
        ProjectFileGenerationRequest request,
        CancellationToken cancellationToken,
        IProgress<ExternalProcessOutput>? outputProgress)
    {
        ArgumentNullException.ThrowIfNull(request);

        var projectKey = request.Project.ProjectFilePath.Value;
        if (!_runningProjects.TryAdd(projectKey, 0))
        {
            return new ProjectFileGenerationResult(
                ProjectFileGenerationStatus.AlreadyRunning,
                ExitCode: null,
                StandardOutputTail: string.Empty,
                StandardErrorTail: string.Empty,
                ErrorMessage: "Project-file generation is already running for this project.",
                SolutionSelection: null);
        }

        try
        {
            var processResult = await _processRunner.RunAsync(
                request.Process,
                cancellationToken,
                outputProgress).ConfigureAwait(false);
            var status = processResult.Status switch
            {
                ExternalProcessStatus.Succeeded =>
                    ProjectFileGenerationStatus.Succeeded,
                ExternalProcessStatus.NonZeroExit =>
                    ProjectFileGenerationStatus.NonZeroExit,
                ExternalProcessStatus.FailedToStart =>
                    ProjectFileGenerationStatus.FailedToStart,
                ExternalProcessStatus.Cancelled =>
                    ProjectFileGenerationStatus.Cancelled,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(processResult.Status),
                    processResult.Status,
                    null),
            };
            var selection = processResult.IsSuccess
                ? _solutionLocator.Locate(request.Project)
                : null;
            return new ProjectFileGenerationResult(
                status,
                processResult.ExitCode,
                processResult.StandardOutputTail,
                processResult.StandardErrorTail,
                processResult.ErrorMessage,
                selection);
        }
        finally
        {
            _runningProjects.TryRemove(projectKey, out _);
        }
    }
}
