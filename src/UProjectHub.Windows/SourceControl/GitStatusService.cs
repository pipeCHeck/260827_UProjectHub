using UProjectHub.Windows.Launching;

namespace UProjectHub.Windows.SourceControl;

public sealed class GitStatusService : IGitStatusService
{
    private static readonly TimeSpan DefaultCommandTimeout =
        TimeSpan.FromSeconds(10);

    private readonly IExternalProcessRunner _runner;
    private readonly TimeSpan _commandTimeout;
    private readonly object _availabilityGate = new();
    private Task<bool>? _availabilityTask;

    public GitStatusService(
        IExternalProcessRunner runner,
        TimeSpan? commandTimeout = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _commandTimeout = commandTimeout ?? DefaultCommandTimeout;
        if (_commandTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(commandTimeout));
        }
    }

    public async Task<GitProjectStatus> GetStatusAsync(
        string projectDirectory,
        bool includeRemotes = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await GetAvailabilityTask().WaitAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return new GitProjectStatus(
                GitProjectState.GitUnavailable,
                ErrorMessage: "Git is not installed or could not be started.");
        }

        var rootResult = await RunGitAsync(
            ["--no-optional-locks", "-C", projectDirectory,
                "rev-parse", "--show-toplevel"],
            cancellationToken).ConfigureAwait(false);
        RememberUnavailable(rootResult);
        if (rootResult.Status != ExternalProcessStatus.Succeeded)
        {
            return ClassifyFailure(rootResult, cancellationToken);
        }

        var repositoryRoot = rootResult.StandardOutputTail.Trim();
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return new GitProjectStatus(
                GitProjectState.Failed,
                ErrorMessage: "Git did not return the repository root.");
        }

        var statusResult = await RunGitAsync(
            ["--no-optional-locks", "-C", projectDirectory,
                "status", "--porcelain=v1", "-z", "--untracked-files=normal"],
            cancellationToken).ConfigureAwait(false);
        RememberUnavailable(statusResult);
        if (statusResult.Status != ExternalProcessStatus.Succeeded)
        {
            var failure = ClassifyFailure(statusResult, cancellationToken);
            return failure with { RepositoryRoot = repositoryRoot };
        }

        var state = string.IsNullOrEmpty(statusResult.StandardOutputTail)
            ? GitProjectState.Clean
            : GitProjectState.Changed;
        if (!includeRemotes)
        {
            return new GitProjectStatus(state, repositoryRoot);
        }

        var remoteResult = await RunGitAsync(
            ["--no-optional-locks", "-C", projectDirectory, "remote", "-v"],
            cancellationToken).ConfigureAwait(false);
        RememberUnavailable(remoteResult);
        if (remoteResult.Status == ExternalProcessStatus.Succeeded)
        {
            return new GitProjectStatus(
                state,
                repositoryRoot,
                ParseRemotes(remoteResult.StandardOutputTail));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new GitProjectStatus(
            state,
            repositoryRoot,
            RemoteErrorMessage: GetError(remoteResult));
    }

    private Task<bool> GetAvailabilityTask()
    {
        lock (_availabilityGate)
        {
            return _availabilityTask ??= ProbeAvailabilityAsync();
        }
    }

    private void RememberUnavailable(ExternalProcessResult result)
    {
        if (result.Status != ExternalProcessStatus.FailedToStart)
        {
            return;
        }

        lock (_availabilityGate)
        {
            _availabilityTask = Task.FromResult(false);
        }
    }

    private async Task<bool> ProbeAvailabilityAsync()
    {
        var result = await RunGitAsync(
            ["--version"],
            CancellationToken.None).ConfigureAwait(false);
        return result.Status == ExternalProcessStatus.Succeeded;
    }

    private async Task<ExternalProcessResult> RunGitAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_commandTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        var result = await _runner.RunAsync(
            new ExternalProcessRequest("git", arguments),
            linked.Token).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static GitProjectStatus ClassifyFailure(
        ExternalProcessResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var error = GetError(result);
        if (result.Status == ExternalProcessStatus.FailedToStart)
        {
            return new GitProjectStatus(
                GitProjectState.GitUnavailable,
                ErrorMessage: error);
        }

        if (result.Status == ExternalProcessStatus.NonZeroExit
            && (error.Contains(
                    "not a git repository",
                    StringComparison.OrdinalIgnoreCase)
                || error.Contains(
                    "not a repository",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return new GitProjectStatus(GitProjectState.NotRepository);
        }

        return new GitProjectStatus(
            GitProjectState.Failed,
            ErrorMessage: result.Status == ExternalProcessStatus.Cancelled
                ? "The Git command timed out."
                : error);
    }

    private static IReadOnlyList<GitRemote> ParseRemotes(string output)
    {
        var remotes = new List<GitRemote>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in output.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries
                         | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOfAny(['\t', ' ']);
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var remainder = line[(separator + 1)..].TrimStart();
            var purpose = remainder.LastIndexOf(" (", StringComparison.Ordinal);
            var url = (purpose > 0 ? remainder[..purpose] : remainder).Trim();
            if (name.Length == 0
                || url.Length == 0
                || !seen.Add(name + "\0" + url))
            {
                continue;
            }

            remotes.Add(new GitRemote(
                name,
                url,
                WebUrlLauncher.NormalizeSafeUrl(url)));
        }

        return Array.AsReadOnly(remotes.ToArray());
    }

    private static string GetError(ExternalProcessResult result) =>
        FirstNonEmpty(
            result.StandardErrorTail,
            result.ErrorMessage,
            result.StandardOutputTail)
        ?? "The Git command failed.";

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
