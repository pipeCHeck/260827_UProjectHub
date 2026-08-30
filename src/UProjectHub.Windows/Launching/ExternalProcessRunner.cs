using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Text;

namespace UProjectHub.Windows.Launching;

public sealed class ExternalProcessRunner : IExternalProcessRunner
{
    private const int DefaultOutputLimit = 16 * 1024;
    private static readonly TimeSpan CancellationCleanupTimeout =
        TimeSpan.FromSeconds(5);

    private readonly int _outputLimit;

    public ExternalProcessRunner(int outputLimit = DefaultOutputLimit)
    {
        if (outputLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputLimit));
        }

        _outputLimit = outputLimit;
    }

    public Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        CancellationToken cancellationToken = default) =>
        RunAsync(request, cancellationToken, outputProgress: null);

    public async Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        CancellationToken cancellationToken,
        IProgress<ExternalProcessOutput>? outputProgress)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(string.Empty, string.Empty);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.ArgumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return FailedToStart("The process could not be started.");
            }
        }
        catch (Exception exception) when (IsExpectedStartFailure(exception))
        {
            return FailedToStart(exception.Message);
        }

        var standardOutput = new OutputTailBuffer(_outputLimit);
        var standardError = new OutputTailBuffer(_outputLimit);
        var serializedProgress = outputProgress is null
            ? null
            : new SerializedOutputProgress(outputProgress);
        var outputTask = CaptureAsync(
            process.StandardOutput,
            standardOutput,
            ExternalProcessOutputStream.StandardOutput,
            serializedProgress);
        var errorTask = CaptureAsync(
            process.StandardError,
            standardError,
            ExternalProcessOutputStream.StandardError,
            serializedProgress);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            var cleanupTask = CompleteCancellationCleanupAsync(
                process,
                outputTask,
                errorTask);
            var cleanupCompleted = await WaitForCancellationCleanupAsync(
                    cleanupTask,
                    CancellationCleanupTimeout)
                .ConfigureAwait(false);
            if (!cleanupCompleted)
            {
                ObserveFailure(cleanupTask);
                return Cancelled(standardOutput.Value, standardError.Value);
            }

            return Cancelled(standardOutput.Value, standardError.Value);
        }

        await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        var exitCode = process.ExitCode;
        return exitCode == 0
            ? new ExternalProcessResult(
                ExternalProcessStatus.Succeeded,
                exitCode,
                standardOutput.Value,
                standardError.Value,
                ErrorMessage: null)
            : new ExternalProcessResult(
                ExternalProcessStatus.NonZeroExit,
                exitCode,
                standardOutput.Value,
                standardError.Value,
                $"The process exited with code {exitCode}.");
    }

    private static async Task CaptureAsync(
        StreamReader reader,
        OutputTailBuffer tail,
        ExternalProcessOutputStream stream,
        SerializedOutputProgress? progress)
    {
        var buffer = new char[2048];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (count == 0)
            {
                return;
            }

            var text = new string(buffer, 0, count);
            tail.Append(text.AsSpan());
            progress?.Report(new ExternalProcessOutput(stream, text));
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or NotSupportedException
            or Win32Exception)
        {
            // Cancellation still wins if the process exits between checks or
            // termination cannot be requested.
        }
    }

    private static Task CompleteCancellationCleanupAsync(
        Process process,
        Task outputTask,
        Task errorTask) =>
        Task.WhenAll(
            WaitForExitAfterCancellationAsync(process),
            AwaitCaptureAfterExitAsync(outputTask, errorTask));

    internal static async Task<bool> WaitForCancellationCleanupAsync(
        Task cleanupTask,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(cleanupTask);

        try
        {
            await cleanupTask.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static void ObserveFailure(Task task)
    {
        _ = task.ContinueWith(
            static completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously
                | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static async Task WaitForExitAfterCancellationAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The process already ended or was never associated with a handle.
        }
    }

    private static async Task AwaitCaptureAfterExitAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException
            or ObjectDisposedException)
        {
            // Process termination can close redirected pipes while a read is pending.
        }
    }

    private static ExternalProcessResult FailedToStart(string message) =>
        new(
            ExternalProcessStatus.FailedToStart,
            ExitCode: null,
            StandardOutputTail: string.Empty,
            StandardErrorTail: string.Empty,
            ErrorMessage: message);

    private static ExternalProcessResult Cancelled(
        string standardOutput,
        string standardError) =>
        new(
            ExternalProcessStatus.Cancelled,
            ExitCode: null,
            standardOutput,
            standardError,
            ErrorMessage: "The operation was cancelled.");

    private static bool IsExpectedStartFailure(Exception exception) =>
        exception is Win32Exception
            or InvalidOperationException
            or ArgumentException
            or IOException
            or UnauthorizedAccessException
            or SecurityException;

    private sealed class OutputTailBuffer
    {
        private readonly object _gate = new();
        private readonly int _limit;
        private readonly StringBuilder _builder;

        public OutputTailBuffer(int limit)
        {
            _limit = limit;
            _builder = new StringBuilder(limit);
        }

        public string Value
        {
            get
            {
                lock (_gate)
                {
                    return _builder.ToString();
                }
            }
        }

        public void Append(ReadOnlySpan<char> value)
        {
            lock (_gate)
            {
                if (value.Length >= _limit)
                {
                    _builder.Clear();
                    _builder.Append(value[^_limit..]);
                    return;
                }

                var overflow = _builder.Length + value.Length - _limit;
                if (overflow > 0)
                {
                    _builder.Remove(0, overflow);
                }

                _builder.Append(value);
            }
        }
    }

    private sealed class SerializedOutputProgress(
        IProgress<ExternalProcessOutput> progress)
    {
        private readonly object _gate = new();

        public void Report(ExternalProcessOutput output)
        {
            lock (_gate)
            {
                progress.Report(output);
            }
        }
    }
}
