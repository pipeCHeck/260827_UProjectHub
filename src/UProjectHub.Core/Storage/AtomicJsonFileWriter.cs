using System.Text.Json;

namespace UProjectHub.Core.Storage;

public sealed class AtomicJsonFileWriter
{
    public async Task WriteAsync<T>(
        string targetFilePath,
        T value,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default)
    {
        await WriteAsync(
            targetFilePath,
            value,
            serializerOptions,
            preserveBackup: true,
            cancellationToken);
    }

    public async Task WriteAsync<T>(
        string targetFilePath,
        T value,
        JsonSerializerOptions serializerOptions,
        bool preserveBackup,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(serializerOptions);

        var fullTargetPath = Path.GetFullPath(targetFilePath);
        var targetDirectory = Path.GetDirectoryName(fullTargetPath)!;
        Directory.CreateDirectory(targetDirectory);

        var temporaryFilePath = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await WriteCompleteJsonAsync(
                temporaryFilePath,
                value,
                serializerOptions,
                cancellationToken);
            await ValidateJsonAsync(temporaryFilePath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(fullTargetPath))
            {
                File.Replace(
                    temporaryFilePath,
                    fullTargetPath,
                    preserveBackup ? $"{fullTargetPath}.bak" : null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryFilePath, fullTargetPath);
            }
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryFilePath);
        }
    }

    private static async Task WriteCompleteJsonAsync<T>(
        string temporaryFilePath,
        T value,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            temporaryFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await JsonSerializer.SerializeAsync(
            stream,
            value,
            serializerOptions,
            cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static async Task ValidateJsonAsync(
        string temporaryFilePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            temporaryFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }

    private static void TryDeleteTemporaryFile(string temporaryFilePath)
    {
        try
        {
            File.Delete(temporaryFilePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
