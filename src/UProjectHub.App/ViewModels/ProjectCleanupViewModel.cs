using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Models;
using UProjectHub.Windows.Cleanup;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectCleanupViewModel : ObservableObject, IDisposable
{
    private readonly UnrealProject _project;
    private readonly IProjectCleanupService _cleanupService;
    private readonly Func<Task> _cleanupCompleted;
    private readonly LocalizationService? _localization;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly RelayCommand _beginConfirmationCommand;
    private readonly RelayCommand _cancelConfirmationCommand;
    private readonly AsyncRelayCommand _confirmCleanupCommand;
    private bool _isInspecting;
    private bool _isCleaning;
    private bool _isConfirmationVisible;
    private bool _isDisposed;
    private string _statusText;
    private long _deletedBytes;

    public ProjectCleanupViewModel(
        UnrealProject project,
        IProjectCleanupService cleanupService,
        Func<Task>? cleanupCompleted = null,
        LocalizationService? localization = null)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _cleanupService = cleanupService
            ?? throw new ArgumentNullException(nameof(cleanupService));
        _cleanupCompleted = cleanupCompleted ?? (() => Task.CompletedTask);
        _localization = localization;
        _statusText = Localize(
            "String.ProjectCleanupLoading",
            "Checking cleanup targets…");
        _beginConfirmationCommand = new RelayCommand(
            BeginConfirmation,
            () => CanBeginConfirmation);
        _cancelConfirmationCommand = new RelayCommand(
            CancelConfirmation,
            () => IsConfirmationVisible && !IsCleaning);
        _confirmCleanupCommand = new AsyncRelayCommand(
            ConfirmCleanupAsync,
            () => IsConfirmationVisible && !IsCleaning && HasSelectedItems);
    }

    public string ProjectName => _project.Name;

    public string ProjectPath => _project.ProjectFilePath.Value;

    public ObservableCollection<ProjectCleanupItemViewModel> Items { get; } = [];

    public bool IsInspecting
    {
        get => _isInspecting;
        private set
        {
            if (SetProperty(ref _isInspecting, value))
            {
                NotifyStateChanged();
            }
        }
    }

    public bool IsCleaning
    {
        get => _isCleaning;
        private set
        {
            if (SetProperty(ref _isCleaning, value))
            {
                UpdateItemInteraction();
                NotifyStateChanged();
            }
        }
    }

    public bool IsConfirmationVisible
    {
        get => _isConfirmationVisible;
        private set
        {
            if (SetProperty(ref _isConfirmationVisible, value))
            {
                UpdateItemInteraction();
                NotifyStateChanged();
            }
        }
    }

    public bool IsSelectionVisible => !IsConfirmationVisible;

    public bool HasItems => Items.Count > 0;

    public bool HasSelectedItems => Items.Any(item => item.IsSelected && item.CanDelete);

    public bool CanBeginConfirmation =>
        !IsInspecting && !IsCleaning && !IsConfirmationVisible && HasSelectedItems;

    public bool CanClose => !IsCleaning;

    public bool ShowBinariesWarning =>
        Items.Any(item =>
            item.Kind == ProjectCleanupTargetKind.Binaries && item.IsSelected);

    public bool ShowSolutionInformation =>
        Items.Any(item =>
            item.Kind == ProjectCleanupTargetKind.Solution && item.IsSelected);

    public string SelectedFileSizeText => FormatBytes(Items
        .Where(item => item.IsSelected && item.CanDelete)
        .Sum(item => item.FileSizeBytes));

    public long DeletedBytes
    {
        get => _deletedBytes;
        private set
        {
            if (SetProperty(ref _deletedBytes, value))
            {
                OnPropertyChanged(nameof(DeletedFileSizeText));
            }
        }
    }

    public string DeletedFileSizeText => FormatBytes(DeletedBytes);

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public ICommand BeginConfirmationCommand => _beginConfirmationCommand;

    public ICommand CancelConfirmationCommand => _cancelConfirmationCommand;

    public ICommand ConfirmCleanupCommand => _confirmCleanupCommand;

    public async Task InitializeAsync()
    {
        if (_isDisposed || IsInspecting || Items.Count > 0)
        {
            return;
        }

        IsInspecting = true;
        try
        {
            var inspection = await _cleanupService.InspectAsync(
                _project,
                _lifetime.Token);
            if (_isDisposed)
            {
                return;
            }

            ReplaceItems(inspection, useDefaults: true);
            StatusText = Localize(
                "String.ProjectCleanupReady",
                "Select the generated items to remove.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            if (!_isDisposed)
            {
                IsInspecting = false;
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _lifetime.Cancel();
        foreach (var item in Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        _lifetime.Dispose();
    }

    private void BeginConfirmation()
    {
        if (CanBeginConfirmation)
        {
            IsConfirmationVisible = true;
        }
    }

    private void CancelConfirmation()
    {
        if (!IsCleaning)
        {
            IsConfirmationVisible = false;
        }
    }

    private async Task ConfirmCleanupAsync()
    {
        var selected = Items
            .Where(item => item.IsSelected && item.CanDelete)
            .Select(item => item.Kind)
            .ToArray();
        if (!IsConfirmationVisible || selected.Length == 0)
        {
            return;
        }

        IsCleaning = true;
        StatusText = Localize(
            "String.ProjectCleanupRunning",
            "Removing selected generated items…");
        try
        {
            var result = await _cleanupService.CleanupAsync(
                new ProjectCleanupRequest(_project, selected),
                _lifetime.Token);
            if (_isDisposed)
            {
                return;
            }

            DeletedBytes = result.DeletedBytes;
            await _cleanupCompleted();
            var refreshed = await _cleanupService.InspectAsync(
                _project,
                _lifetime.Token);
            if (_isDisposed)
            {
                return;
            }

            ApplyRefreshedInspection(refreshed, result);
            IsConfirmationVisible = false;
            StatusText = result.Items.Any(item =>
                    item.Status == ProjectCleanupItemStatus.Failed)
                ? Localize(
                    "String.ProjectCleanupPartial",
                    "Cleanup completed with one or more item failures.")
                : string.Format(
                    CultureInfo.CurrentCulture,
                    Localize(
                        "String.ProjectCleanupCompleted",
                        "Cleanup completed. Deleted file size: {0}."),
                    DeletedFileSizeText);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            if (!_isDisposed)
            {
                IsCleaning = false;
            }
        }
    }

    private void ReplaceItems(ProjectCleanupInspection inspection, bool useDefaults)
    {
        foreach (var item in Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        Items.Clear();
        foreach (var inspected in inspection.Items)
        {
            var selected = useDefaults
                && inspected.CanDelete
                && inspected.Kind is ProjectCleanupTargetKind.Intermediate
                    or ProjectCleanupTargetKind.DerivedDataCache
                    or ProjectCleanupTargetKind.VisualStudioWorkspace;
            var item = new ProjectCleanupItemViewModel(
                inspected,
                selected,
                GetDisplayName(inspected.Kind),
                Localize("String.ProjectCleanupPresent", "Present"),
                Localize("String.ProjectCleanupNotFound", "Not found"),
                Localize("String.ProjectCleanupBlocked", "Blocked"),
                Localize(
                    "String.ProjectCleanupDeletedFileSizeFormat",
                    "Deleted file size — {0}"),
                Localize("String.ProjectCleanupAlreadyAbsent", "Already absent"),
                Localize("String.ProjectCleanupItemUnavailable", "Unavailable"),
                Localize("String.ProjectCleanupItemFailed", "Failed"));
            item.PropertyChanged += OnItemPropertyChanged;
            Items.Add(item);
        }

        UpdateItemInteraction();
        NotifyStateChanged();
    }

    private void ApplyRefreshedInspection(
        ProjectCleanupInspection inspection,
        ProjectCleanupResult result)
    {
        var results = result.Items.ToDictionary(item => item.Kind);
        foreach (var item in Items)
        {
            var refreshed = inspection.Items.Single(entry => entry.Kind == item.Kind);
            item.Update(refreshed);
            if (results.TryGetValue(item.Kind, out var cleanupResult))
            {
                item.ApplyResult(cleanupResult);
            }
        }

        NotifyStateChanged();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ProjectCleanupItemViewModel.IsSelected))
        {
            NotifyStateChanged();
        }
    }

    private void UpdateItemInteraction()
    {
        foreach (var item in Items)
        {
            item.SetInteractionEnabled(!IsConfirmationVisible && !IsCleaning);
        }
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsSelectionVisible));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelectedItems));
        OnPropertyChanged(nameof(CanBeginConfirmation));
        OnPropertyChanged(nameof(CanClose));
        OnPropertyChanged(nameof(ShowBinariesWarning));
        OnPropertyChanged(nameof(ShowSolutionInformation));
        OnPropertyChanged(nameof(SelectedFileSizeText));
        _beginConfirmationCommand.RaiseCanExecuteChanged();
        _cancelConfirmationCommand.RaiseCanExecuteChanged();
        _confirmCleanupCommand.RaiseCanExecuteChanged();
    }

    private string GetDisplayName(ProjectCleanupTargetKind kind) => kind switch
    {
        ProjectCleanupTargetKind.Intermediate => "Intermediate/",
        ProjectCleanupTargetKind.DerivedDataCache => "DerivedDataCache/",
        ProjectCleanupTargetKind.VisualStudioWorkspace => ".vs/",
        ProjectCleanupTargetKind.Binaries => "Binaries/",
        ProjectCleanupTargetKind.Solution => Localize(
            "String.ProjectCleanupSolution",
            "Project .sln"),
        _ => kind.ToString(),
    };

    private string Localize(string key, string fallback) =>
        _localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }
}

public sealed class ProjectCleanupItemViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _interactionEnabled = true;
    private bool _exists;
    private bool _canDelete;
    private long _sizeBytes;
    private string? _path;
    private string? _detailText;
    private string? _resultText;
    private readonly string _presentText;
    private readonly string _notFoundText;
    private readonly string _blockedText;
    private readonly string _deletedFileSizeFormat;
    private readonly string _alreadyAbsentText;
    private readonly string _unavailableText;
    private readonly string _failedText;

    internal ProjectCleanupItemViewModel(
        ProjectCleanupItemInspection inspection,
        bool isSelected,
        string displayName,
        string presentText,
        string notFoundText,
        string blockedText,
        string deletedFileSizeFormat,
        string alreadyAbsentText,
        string unavailableText,
        string failedText)
    {
        Kind = inspection.Kind;
        DisplayName = displayName;
        _presentText = presentText;
        _notFoundText = notFoundText;
        _blockedText = blockedText;
        _deletedFileSizeFormat = deletedFileSizeFormat;
        _alreadyAbsentText = alreadyAbsentText;
        _unavailableText = unavailableText;
        _failedText = failedText;
        _isSelected = isSelected;
        Update(inspection);
    }

    public ProjectCleanupTargetKind Kind { get; }

    public string DisplayName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !CanSelect)
            {
                return;
            }

            SetProperty(ref _isSelected, value);
        }
    }

    public bool CanSelect => _interactionEnabled && _canDelete;

    public bool CanDelete => _canDelete;

    public bool Exists => _exists;

    public string? Path => _path;

    public long FileSizeBytes => _sizeBytes;

    public string FileSizeText => ProjectCleanupViewModel.FormatBytes(FileSizeBytes);

    public string? DetailText => _detailText;

    public string? ResultText => _resultText;

    public string AvailabilityText => _detailText is not null && !_canDelete
        ? _blockedText
        : _exists
            ? _presentText
            : _notFoundText;

    internal void SetInteractionEnabled(bool enabled)
    {
        if (_interactionEnabled == enabled)
        {
            return;
        }

        _interactionEnabled = enabled;
        OnPropertyChanged(nameof(CanSelect));
    }

    internal void Update(ProjectCleanupItemInspection inspection)
    {
        _path = inspection.Path
            ?? (inspection.CandidatePaths.Count > 0
                ? string.Join(Environment.NewLine, inspection.CandidatePaths)
                : null);
        _exists = inspection.Exists;
        _canDelete = inspection.CanDelete;
        _sizeBytes = inspection.FileSizeBytes;
        _detailText = inspection.ErrorMessage;
        if (!_canDelete)
        {
            _isSelected = false;
        }

        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(Exists));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanSelect));
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(FileSizeBytes));
        OnPropertyChanged(nameof(FileSizeText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(AvailabilityText));
    }

    internal void ApplyResult(ProjectCleanupItemResult result)
    {
        _resultText = result.Status switch
        {
            ProjectCleanupItemStatus.Deleted =>
                string.Format(
                    CultureInfo.CurrentCulture,
                    _deletedFileSizeFormat,
                    ProjectCleanupViewModel.FormatBytes(result.DeletedBytes)),
            ProjectCleanupItemStatus.NotFound => _alreadyAbsentText,
            ProjectCleanupItemStatus.Unavailable =>
                result.ErrorMessage ?? _unavailableText,
            _ => result.ErrorMessage ?? _failedText,
        };
        OnPropertyChanged(nameof(ResultText));
    }
}
