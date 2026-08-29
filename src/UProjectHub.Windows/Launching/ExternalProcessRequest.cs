namespace UProjectHub.Windows.Launching;

public sealed class ExternalProcessRequest
{
    public ExternalProcessRequest(
        string fileName,
        IEnumerable<string>? argumentList = null,
        string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        FileName = fileName;
        ArgumentList = Array.AsReadOnly(argumentList?.ToArray() ?? []);
        WorkingDirectory = workingDirectory;
    }

    public string FileName { get; }

    public IReadOnlyList<string> ArgumentList { get; }

    public string? WorkingDirectory { get; }
}

public enum ExternalProcessStatus
{
    Succeeded,
    NonZeroExit,
    FailedToStart,
    Cancelled,
}

public sealed record ExternalProcessResult(
    ExternalProcessStatus Status,
    int? ExitCode,
    string StandardOutputTail,
    string StandardErrorTail,
    string? ErrorMessage)
{
    public bool IsSuccess => Status == ExternalProcessStatus.Succeeded;
}

public interface IExternalProcessRunner
{
    Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        CancellationToken cancellationToken = default);
}
