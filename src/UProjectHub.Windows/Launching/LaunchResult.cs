namespace UProjectHub.Windows.Launching;

public sealed record LaunchResult
{
    private LaunchResult(
        bool isSuccess,
        string? errorMessage,
        DateTimeOffset? launchedAtUtc)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        LaunchedAtUtc = launchedAtUtc;
    }

    public bool IsSuccess { get; }

    public string? ErrorMessage { get; }

    public DateTimeOffset? LaunchedAtUtc { get; }

    public static LaunchResult Succeeded(
        DateTimeOffset? launchedAtUtc = null) =>
        new(true, null, launchedAtUtc);

    public static LaunchResult Failed(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new LaunchResult(false, errorMessage, null);
    }
}
