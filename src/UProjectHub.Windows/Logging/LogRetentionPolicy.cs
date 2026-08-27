namespace UProjectHub.Windows.Logging;

public sealed record LogRetentionPolicy
{
    public const long DefaultMaxFileBytes = 2L * 1024 * 1024;
    public const int DefaultMaxBackupFiles = 3;

    public LogRetentionPolicy(long maxFileBytes, int maxBackupFiles)
    {
        if (maxFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFileBytes),
                "Maximum log file size must be positive.");
        }

        if (maxBackupFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBackupFiles),
                "Maximum backup count must be positive.");
        }

        MaxFileBytes = maxFileBytes;
        MaxBackupFiles = maxBackupFiles;
    }

    public long MaxFileBytes { get; }

    public int MaxBackupFiles { get; }

    public static LogRetentionPolicy Default { get; } = new(
        DefaultMaxFileBytes,
        DefaultMaxBackupFiles);
}
