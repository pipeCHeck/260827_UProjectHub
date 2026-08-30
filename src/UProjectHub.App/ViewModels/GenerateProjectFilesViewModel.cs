using System.Text;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Windows.Launching;

namespace UProjectHub.App.ViewModels;

public sealed class GenerateProjectFilesViewModel : ObservableObject, IDisposable
{
    private const int LiveOutputLimit = 32 * 1024;
    private static readonly TimeSpan LiveOutputFlushInterval =
        TimeSpan.FromMilliseconds(100);

    private readonly Func<
        IProgress<ExternalProcessOutput>?,
        CancellationToken,
        Task<ProjectFileGenerationResult>> _generateAsync;
    private readonly Func<Task> _solutionStateChanged;
    private readonly LocalizationService? _localization;
    private readonly SynchronizationContext? _uiContext;
    private readonly RelayCommand _cancelCommand;
    private CancellationTokenSource? _cancellationSource;
    private CancellationTokenSource? _streamingSource;
    private long _runGeneration;
    private long _activeStreamingGeneration;
    private bool _disposed;
    private bool _isRunning;
    private bool _isCompleted;
    private bool _wasSuccessful;
    private string _statusText;
    private string _outputDetails = string.Empty;

    public GenerateProjectFilesViewModel(
        ProjectFileGenerationRequest request,
        Func<CancellationToken, Task<ProjectFileGenerationResult>> generateAsync,
        Func<Task> solutionStateChanged,
        LocalizationService? localization = null)
        : this(
            request,
            (_, cancellationToken) => generateAsync(cancellationToken),
            solutionStateChanged,
            localization)
    {
        ArgumentNullException.ThrowIfNull(generateAsync);
    }

    public GenerateProjectFilesViewModel(
        ProjectFileGenerationRequest request,
        Func<
            IProgress<ExternalProcessOutput>?,
            CancellationToken,
            Task<ProjectFileGenerationResult>> generateAsync,
        Func<Task> solutionStateChanged,
        LocalizationService? localization = null)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        _generateAsync = generateAsync
            ?? throw new ArgumentNullException(nameof(generateAsync));
        _solutionStateChanged = solutionStateChanged
            ?? throw new ArgumentNullException(nameof(solutionStateChanged));
        _localization = localization;
        _uiContext = SynchronizationContext.Current;

        ProjectName = request.Project.Name;
        ProjectPath = request.Project.ProjectFilePath.Value;
        EngineDisplayName = request.Engine.DisplayName;
        EngineRoot = request.Engine.RootPath;
        ExpectedSolutionPath = request.ExpectedSolutionPath;
        _statusText = Localize(
            "String.GenerateProjectFilesReady",
            "Review the target details, then choose Generate to continue.");

        GenerateCommand = new AsyncRelayCommand(
            GenerateAsync,
            () => !WasSuccessful && !_disposed);
        _cancelCommand = new RelayCommand(Cancel, () => IsRunning);
    }

    public ProjectFileGenerationRequest Request { get; }

    public string ProjectName { get; }

    public string ProjectPath { get; }

    public string EngineDisplayName { get; }

    public string EngineRoot { get; }

    public string ExpectedSolutionPath { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                _cancelCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanClose));
            }
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        private set
        {
            if (SetProperty(ref _isCompleted, value))
            {
                GenerateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool WasSuccessful
    {
        get => _wasSuccessful;
        private set
        {
            if (SetProperty(ref _wasSuccessful, value))
            {
                GenerateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanClose => !IsRunning;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string OutputDetails
    {
        get => _outputDetails;
        private set
        {
            if (SetProperty(ref _outputDetails, value))
            {
                OnPropertyChanged(nameof(HasOutputDetails));
            }
        }
    }

    public bool HasOutputDetails => !string.IsNullOrWhiteSpace(OutputDetails);

    public AsyncRelayCommand GenerateCommand { get; }

    public RelayCommand CancelCommand => _cancelCommand;

    private async Task GenerateAsync()
    {
        if (_disposed)
        {
            return;
        }

        _cancellationSource?.Dispose();
        _streamingSource?.Dispose();
        var cancellationSource = new CancellationTokenSource();
        var streamingSource = new CancellationTokenSource();
        _cancellationSource = cancellationSource;
        _streamingSource = streamingSource;
        var runGeneration = Interlocked.Increment(ref _runGeneration);
        var outputBuffer = new LiveOutputBuffer(LiveOutputLimit);
        var outputProgress = new WeakOutputProgress(
            this,
            runGeneration,
            outputBuffer);
        Volatile.Write(ref _activeStreamingGeneration, runGeneration);
        var streamingTask = FlushLiveOutputAsync(
            runGeneration,
            outputBuffer,
            streamingSource.Token);

        IsCompleted = false;
        WasSuccessful = false;
        OutputDetails = string.Empty;
        IsRunning = true;
        StatusText = Localize(
            "String.GenerateProjectFilesRunning",
            "Generating Visual Studio project files...");

        try
        {
            var result = await _generateAsync(
                outputProgress,
                cancellationSource.Token);
            await StopStreamingAsync(
                runGeneration,
                streamingSource,
                streamingTask);
            if (!IsCurrentRun(runGeneration))
            {
                return;
            }

            IsCompleted = true;
            WasSuccessful = result.IsSuccess;
            OutputDetails = FormatDetails(result);
            StatusText = GetStatusText(result);

            if (result.IsSuccess)
            {
                await _solutionStateChanged();
            }
        }
        finally
        {
            await StopStreamingAsync(
                runGeneration,
                streamingSource,
                streamingTask);
            if (ReferenceEquals(_cancellationSource, cancellationSource))
            {
                _cancellationSource = null;
            }

            if (ReferenceEquals(_streamingSource, streamingSource))
            {
                _streamingSource = null;
            }

            if (IsCurrentRun(runGeneration))
            {
                IsRunning = false;
            }

            cancellationSource.Dispose();
            streamingSource.Dispose();
        }
    }

    private void Cancel()
    {
        _cancellationSource?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Increment(ref _runGeneration);
        Volatile.Write(ref _activeStreamingGeneration, 0);
        _streamingSource?.Cancel();
        _cancellationSource?.Cancel();
        GenerateCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
    }

    private async Task FlushLiveOutputAsync(
        long runGeneration,
        LiveOutputBuffer outputBuffer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(
                        LiveOutputFlushInterval,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (outputBuffer.TryGetSnapshot(out var snapshot))
                {
                    PostLiveOutput(runGeneration, snapshot);
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task StopStreamingAsync(
        long runGeneration,
        CancellationTokenSource streamingSource,
        Task streamingTask)
    {
        Interlocked.CompareExchange(
            ref _activeStreamingGeneration,
            0,
            runGeneration);
        streamingSource.Cancel();
        await streamingTask.ConfigureAwait(false);
    }

    private void ReceiveOutput(
        long runGeneration,
        LiveOutputBuffer outputBuffer,
        ExternalProcessOutput output)
    {
        if (_disposed
            || Volatile.Read(ref _activeStreamingGeneration) != runGeneration)
        {
            return;
        }

        outputBuffer.Append(output);
    }

    private void PostLiveOutput(long runGeneration, string snapshot)
    {
        void Apply()
        {
            if (!_disposed
                && Volatile.Read(ref _activeStreamingGeneration)
                    == runGeneration)
            {
                OutputDetails = snapshot;
            }
        }

        if (_uiContext is null
            || ReferenceEquals(SynchronizationContext.Current, _uiContext))
        {
            Apply();
            return;
        }

        _uiContext.Post(static state => ((Action)state!).Invoke(), (Action)Apply);
    }

    private bool IsCurrentRun(long runGeneration) =>
        !_disposed && Volatile.Read(ref _runGeneration) == runGeneration;

    private string GetStatusText(ProjectFileGenerationResult result)
    {
        return result.Status switch
        {
            ProjectFileGenerationStatus.Succeeded =>
                GetSuccessfulStatus(result.SolutionSelection),
            ProjectFileGenerationStatus.Cancelled => Localize(
                "String.GenerateProjectFilesCanceled",
                "Project-file generation was canceled."),
            ProjectFileGenerationStatus.AlreadyRunning => Localize(
                "String.GenerateProjectFilesAlreadyRunning",
                "Project-file generation is already running for this project."),
            _ => Localize(
                "String.GenerateProjectFilesFailed",
                "Visual Studio project-file generation failed."),
        };
    }

    private string GetSuccessfulStatus(
        VisualStudioSolutionSelection? selection) => selection?.State switch
    {
        VisualStudioSolutionState.Available => Localize(
            "String.GenerateProjectFilesSucceeded",
            "Visual Studio project files were generated and the .sln is available."),
        VisualStudioSolutionState.Multiple => Localize(
            "String.GenerateProjectFilesSucceededMultiple",
            "Project files were generated, but multiple .sln files were found."),
        VisualStudioSolutionState.Inaccessible => Localize(
            "String.GenerateProjectFilesSucceededInaccessible",
            "Project files were generated, but the project folder could not be inspected."),
        _ => Localize(
            "String.GenerateProjectFilesSucceededMissing",
            "Project files were generated, but no .sln file was found."),
    };

    private static string FormatDetails(ProjectFileGenerationResult result)
    {
        var details = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            details.AppendLine(result.ErrorMessage.Trim());
        }

        AppendSection(details, "Output", result.StandardOutputTail);
        AppendSection(details, "Errors", result.StandardErrorTail);
        return details.ToString().Trim();
    }

    private static void AppendSection(
        StringBuilder builder,
        string heading,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine($"{heading}:");
        builder.Append(value.Trim());
    }

    private string Localize(string key, string fallback) =>
        _localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;

    private sealed class WeakOutputProgress : IProgress<ExternalProcessOutput>
    {
        private readonly WeakReference<GenerateProjectFilesViewModel> _owner;
        private readonly long _runGeneration;
        private readonly LiveOutputBuffer _outputBuffer;

        public WeakOutputProgress(
            GenerateProjectFilesViewModel owner,
            long runGeneration,
            LiveOutputBuffer outputBuffer)
        {
            _owner = new WeakReference<GenerateProjectFilesViewModel>(owner);
            _runGeneration = runGeneration;
            _outputBuffer = outputBuffer;
        }

        public void Report(ExternalProcessOutput value)
        {
            if (_owner.TryGetTarget(out var owner))
            {
                owner.ReceiveOutput(_runGeneration, _outputBuffer, value);
            }
        }
    }

    private sealed class LiveOutputBuffer
    {
        private const int SegmentOverhead = 10;
        private readonly object _gate = new();
        private readonly int _limit;
        private readonly Queue<ExternalProcessOutput> _segments = new();
        private int _bufferedLength;
        private bool _isDirty;

        public LiveOutputBuffer(int limit)
        {
            _limit = limit;
        }

        public void Append(ExternalProcessOutput output)
        {
            if (string.IsNullOrEmpty(output.Text))
            {
                return;
            }

            var text = output.Text;
            var maximumTextLength = _limit - SegmentOverhead;
            if (text.Length > maximumTextLength)
            {
                text = text[^maximumTextLength..];
            }

            var segment = output with { Text = text };
            var segmentLength = text.Length + SegmentOverhead;
            lock (_gate)
            {
                _segments.Enqueue(segment);
                _bufferedLength += segmentLength;
                while (_bufferedLength > _limit && _segments.Count > 1)
                {
                    var removed = _segments.Dequeue();
                    _bufferedLength -= removed.Text.Length + SegmentOverhead;
                }

                _isDirty = true;
            }
        }

        public bool TryGetSnapshot(out string snapshot)
        {
            lock (_gate)
            {
                if (!_isDirty)
                {
                    snapshot = string.Empty;
                    return false;
                }

                _isDirty = false;
                var builder = new StringBuilder(_bufferedLength);
                ExternalProcessOutputStream? previousStream = null;
                foreach (var segment in _segments)
                {
                    if (segment.Stream != previousStream)
                    {
                        if (builder.Length > 0 && builder[^1] != '\n')
                        {
                            builder.AppendLine();
                        }

                        builder.Append(segment.Stream
                            == ExternalProcessOutputStream.StandardOutput
                                ? "[stdout] "
                                : "[stderr] ");
                    }

                    builder.Append(segment.Text);
                    previousStream = segment.Stream;
                }

                snapshot = builder.ToString();
                return true;
            }
        }
    }
}
