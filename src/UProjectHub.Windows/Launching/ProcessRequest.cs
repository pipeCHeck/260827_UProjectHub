namespace UProjectHub.Windows.Launching;

public sealed class ProcessRequest
{
    public ProcessRequest(
        string fileName,
        IEnumerable<string>? argumentList = null,
        string? workingDirectory = null,
        bool useShellExecute = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        FileName = fileName;
        ArgumentList = Array.AsReadOnly(
            argumentList?.ToArray() ?? []);
        WorkingDirectory = workingDirectory;
        UseShellExecute = useShellExecute;
    }

    public string FileName { get; }

    public IReadOnlyList<string> ArgumentList { get; }

    public string? WorkingDirectory { get; }

    public bool UseShellExecute { get; }
}
