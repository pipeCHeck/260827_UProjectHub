using System.Text;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Windows.Launching;

namespace UProjectHub.App.ViewModels;

public sealed class GenerateProjectFilesViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<ProjectFileGenerationResult>>
        _generateAsync;
    private readonly Func<Task> _solutionStateChanged;
    private readonly LocalizationService? _localization;
    private readonly RelayCommand _cancelCommand;
    private CancellationTokenSource? _cancellationSource;
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
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        _generateAsync = generateAsync
            ?? throw new ArgumentNullException(nameof(generateAsync));
        _solutionStateChanged = solutionStateChanged
            ?? throw new ArgumentNullException(nameof(solutionStateChanged));
        _localization = localization;

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
            () => !WasSuccessful);
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
        _cancellationSource?.Dispose();
        _cancellationSource = new CancellationTokenSource();
        IsCompleted = false;
        WasSuccessful = false;
        OutputDetails = string.Empty;
        IsRunning = true;
        StatusText = Localize(
            "String.GenerateProjectFilesRunning",
            "Generating Visual Studio project files...");

        try
        {
            var result = await _generateAsync(_cancellationSource.Token);
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
            IsRunning = false;
        }
    }

    private void Cancel()
    {
        _cancellationSource?.Cancel();
    }

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
}
