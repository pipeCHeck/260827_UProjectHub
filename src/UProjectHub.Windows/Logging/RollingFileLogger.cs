using System.Globalization;
using System.Security;
using System.Text;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Time;

namespace UProjectHub.Windows.Logging;

public sealed class RollingFileLogger : IAppLogger
{
    private const string TruncationMarker = "...[truncated]";

    private static readonly UTF8Encoding Utf8WithoutBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly string _logFilePath;
    private readonly LogRetentionPolicy _retentionPolicy;
    private readonly IClock _clock;
    private readonly object _writeLock = new();

    public RollingFileLogger(
        string logFilePath,
        LogRetentionPolicy retentionPolicy,
        IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);
        ArgumentNullException.ThrowIfNull(retentionPolicy);
        ArgumentNullException.ThrowIfNull(clock);

        _logFilePath = Path.GetFullPath(logFilePath);
        _retentionPolicy = retentionPolicy;
        _clock = clock;
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warning(string message) => Write("WARN", message, null);

    public void Error(string message) => Write("ERROR", message, null);

    public void Error(string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write("ERROR", message, exception);
    }

    private void Write(
        string level,
        string message,
        Exception? exception)
    {
        var entry = BoundEntry(FormatEntry(level, message, exception));
        var entryByteCount = Utf8WithoutBom.GetByteCount(entry);

        try
        {
            lock (_writeLock)
            {
                var logDirectory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                RotateIfRequired(entryByteCount);
                File.AppendAllText(_logFilePath, entry, Utf8WithoutBom);
            }
        }
        catch (Exception sinkException) when (IsExpectedFileSinkFailure(sinkException))
        {
        }
    }

    private string FormatEntry(
        string level,
        string message,
        Exception? exception)
    {
        var timestamp = _clock.UtcNow
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);
        var builder = new StringBuilder()
            .Append(timestamp)
            .Append(" [")
            .Append(level)
            .Append("] ")
            .Append(NormalizeSingleLine(message));

        if (exception is not null)
        {
            builder
                .Append(" | ")
                .Append(exception.GetType().Name)
                .Append(": ")
                .Append(NormalizeSingleLine(exception.Message));
        }

        return builder.Append(Environment.NewLine).ToString();
    }

    private string BoundEntry(string entry)
    {
        if (Utf8WithoutBom.GetByteCount(entry) <= _retentionPolicy.MaxFileBytes)
        {
            return entry;
        }

        var markerByteCount = Utf8WithoutBom.GetByteCount(TruncationMarker);
        if (_retentionPolicy.MaxFileBytes >= markerByteCount)
        {
            var contentBudget = _retentionPolicy.MaxFileBytes - markerByteCount;
            return TakeUtf8Prefix(entry, contentBudget) + TruncationMarker;
        }

        return TakeUtf8Prefix(entry, _retentionPolicy.MaxFileBytes);
    }

    private static string TakeUtf8Prefix(string value, long maxBytes)
    {
        if (maxBytes <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        long usedBytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (usedBytes + rune.Utf8SequenceLength > maxBytes)
            {
                break;
            }

            builder.Append(rune.ToString());
            usedBytes += rune.Utf8SequenceLength;
        }

        return builder.ToString();
    }

    private void RotateIfRequired(long entryByteCount)
    {
        var currentLength = File.Exists(_logFilePath)
            ? new FileInfo(_logFilePath).Length
            : 0;
        if (currentLength == 0
            || currentLength <= _retentionPolicy.MaxFileBytes - entryByteCount)
        {
            return;
        }

        Rotate();
    }

    private void Rotate()
    {
        RemoveBackupsOutsideRetention();

        var oldestBackup = GetBackupPath(_retentionPolicy.MaxBackupFiles);
        if (File.Exists(oldestBackup))
        {
            File.Delete(oldestBackup);
        }

        for (var index = _retentionPolicy.MaxBackupFiles - 1;
             index >= 1;
             index--)
        {
            var source = GetBackupPath(index);
            if (!File.Exists(source))
            {
                continue;
            }

            var destination = GetBackupPath(index + 1);
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(source, destination);
        }

        if (File.Exists(_logFilePath))
        {
            var firstBackup = GetBackupPath(1);
            if (File.Exists(firstBackup))
            {
                File.Delete(firstBackup);
            }

            File.Move(_logFilePath, firstBackup);
        }
    }

    private void RemoveBackupsOutsideRetention()
    {
        var directory = Path.GetDirectoryName(_logFilePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var logFileName = Path.GetFileName(_logFilePath);
        var backupPrefix = $"{logFileName}.";
        foreach (var path in Directory.EnumerateFiles(
            directory,
            $"{logFileName}.*",
            SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            if (!fileName.StartsWith(
                    backupPrefix,
                    StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(
                    fileName[backupPrefix.Length..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var backupNumber)
                || backupNumber <= _retentionPolicy.MaxBackupFiles)
            {
                continue;
            }

            File.Delete(path);
        }
    }

    private string GetBackupPath(int index) => $"{_logFilePath}.{index}";

    private static string NormalizeSingleLine(string? value) =>
        (value ?? string.Empty)
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');

    private static bool IsExpectedFileSinkFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException;
}
