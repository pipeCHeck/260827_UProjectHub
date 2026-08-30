using System.Windows.Input;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Models;
using UProjectHub.Windows.Launching;
using UProjectHub.Windows.SourceControl;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectSourceControlViewModel : ObservableObject, IDisposable
{
    private readonly UnrealProject _project;
    private readonly ProjectGitStatusStore _statuses;
    private readonly IWebUrlLauncher _webUrlLauncher;
    private readonly LocalizationService? _localization;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly AsyncRelayCommand _refreshCommand;
    private GitProjectStatus? _status;
    private IReadOnlyList<GitRemoteViewModel> _remotes = [];
    private bool _isRefreshing;
    private string? _openErrorMessage;
    private bool _isDisposed;

    public ProjectSourceControlViewModel(
        UnrealProject project,
        ProjectGitStatusStore statuses,
        IWebUrlLauncher webUrlLauncher,
        LocalizationService? localization = null)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _statuses = statuses ?? throw new ArgumentNullException(nameof(statuses));
        _webUrlLauncher = webUrlLauncher
            ?? throw new ArgumentNullException(nameof(webUrlLauncher));
        _localization = localization;
        _refreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);
        _statuses.StatusChanged += OnStatusChanged;
        if (_statuses.TryGet(project) is { } current)
        {
            Apply(current);
        }
    }

    public GitProjectStatus? Status => _status;

    public GitProjectState? State => Status?.State;

    public string StateDisplay => State switch
    {
        GitProjectState.NotRepository => Localize(
            "String.GitNotRepository",
            "Not Repository"),
        GitProjectState.Clean => Localize("String.GitClean", "Clean"),
        GitProjectState.Changed => Localize("String.GitChanged", "Changed"),
        GitProjectState.Failed => Localize("String.GitFailed", "Failed"),
        GitProjectState.GitUnavailable => Localize(
            "String.GitUnavailable",
            "Git Unavailable"),
        _ => Localize("String.GitChecking", "Checking…"),
    };

    public string? RepositoryRoot => Status?.RepositoryRoot;

    public IReadOnlyList<GitRemoteViewModel> Remotes => _remotes;

    public bool HasRemotes => Remotes.Count > 0;

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                _refreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? ErrorMessage =>
        _openErrorMessage ?? Status?.RemoteErrorMessage ?? Status?.ErrorMessage;

    public ICommand RefreshCommand => _refreshCommand;

    public Task ActivateAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_isDisposed || IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            var status = await _statuses.RefreshAsync(
                _project,
                includeRemotes: true,
                _lifetimeCancellation.Token);
            if (!_isDisposed && status is not null)
            {
                Apply(status);
            }
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Closing Project Details cancels the selected-project query.
        }
        finally
        {
            if (!_isDisposed)
            {
                IsRefreshing = false;
            }
        }
    }

    private void OnStatusChanged(
        object? sender,
        ProjectGitStatusChangedEventArgs eventArgs)
    {
        if (!_isDisposed
            && eventArgs.ProjectPath.Equals(_project.ProjectFilePath))
        {
            Apply(eventArgs.Status);
        }
    }

    private void Apply(GitProjectStatus status)
    {
        _status = status;
        _openErrorMessage = null;
        _remotes = Array.AsReadOnly(status.Remotes
            .Select(remote => new GitRemoteViewModel(remote, OpenRemote))
            .ToArray());
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateDisplay));
        OnPropertyChanged(nameof(RepositoryRoot));
        OnPropertyChanged(nameof(Remotes));
        OnPropertyChanged(nameof(HasRemotes));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private void OpenRemote(string url)
    {
        var result = _webUrlLauncher.Open(url);
        _openErrorMessage = result.IsSuccess ? null : result.ErrorMessage;
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private string Localize(string key, string fallback) =>
        _localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _statuses.StatusChanged -= OnStatusChanged;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }
}

public sealed class GitRemoteViewModel
{
    private readonly string? _webUrl;

    public GitRemoteViewModel(GitRemote remote, Action<string> open)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(open);
        Name = remote.Name;
        Url = remote.Url;
        _webUrl = remote.WebUrl;
        OpenCommand = new RelayCommand(
            () => open(_webUrl!),
            () => _webUrl is not null);
    }

    public string Name { get; }

    public string Url { get; }

    public bool CanOpen => _webUrl is not null;

    public ICommand OpenCommand { get; }
}
