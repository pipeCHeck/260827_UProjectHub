using System.Collections.ObjectModel;
using System.IO;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Settings;

namespace UProjectHub.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IProjectOperations _operations;
    private readonly IFolderPickerService _folderPicker;
    private readonly LocalizationService? _localization;
    private readonly Action<AppSettings>? _settingsApplied;
    private readonly Action<ProjectCatalogSnapshot>? _projectsRescanned;
    private readonly ObservableCollection<string> _searchRoots = [];
    private readonly ObservableCollection<string> _manualEngineRoots = [];
    private readonly IReadOnlyList<SettingOption<ThemeMode>> _themeOptions;
    private readonly IReadOnlyList<SettingOption<RowDensity>> _rowDensityOptions;
    private readonly IReadOnlyList<SettingOption<AppLanguage>> _languageOptions;
    private string? _selectedSearchRoot;
    private string? _selectedManualEngineRoot;
    private ThemeMode _selectedThemeMode = ThemeMode.System;
    private RowDensity _selectedRowDensity = RowDensity.Normal;
    private AppLanguage _selectedLanguage = AppLanguage.English;
    private string _statusText = "Ready";
    private bool _isBusy;

    public SettingsViewModel(
        IProjectOperations operations,
        IFolderPickerService folderPicker,
        Action<AppSettings>? settingsApplied = null,
        Action<ProjectCatalogSnapshot>? projectsRescanned = null,
        LocalizationService? localization = null)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _localization = localization;
        _statusText = Localize("String.StatusReady", "Ready");
        _settingsApplied = settingsApplied;
        _projectsRescanned = projectsRescanned;

        SearchRoots = new ReadOnlyObservableCollection<string>(_searchRoots);
        ManualEngineRoots = new ReadOnlyObservableCollection<string>(_manualEngineRoots);
        ThemeModes = Enum.GetValues<ThemeMode>();
        RowDensities = Enum.GetValues<RowDensity>();
        Languages = Enum.GetValues<AppLanguage>();
        _themeOptions =
        [
            new(ThemeMode.System, Localize("String.ThemeSystem", "System")),
            new(ThemeMode.Light, Localize("String.ThemeLight", "Light")),
            new(ThemeMode.Dark, Localize("String.ThemeDark", "Dark")),
        ];
        _rowDensityOptions =
        [
            new(RowDensity.Normal, Localize("String.DensityNormal", "Normal")),
            new(RowDensity.Compact, Localize("String.DensityCompact", "Compact")),
        ];
        _languageOptions =
        [
            new(AppLanguage.English, Localize("String.LanguageEnglish", "English")),
            new(AppLanguage.Korean, Localize("String.LanguageKorean", "한국어")),
        ];
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }

        AddSearchRootCommand = new AsyncRelayCommand(AddSearchRootAsync, () => !IsBusy);
        RemoveSearchRootCommand = new AsyncRelayCommand(RemoveSearchRootAsync, () => !IsBusy && SelectedSearchRoot is not null);
        AddDroppedSearchRootsCommand = new AsyncRelayCommand(AddDroppedSearchRootsAsync, parameter => !IsBusy && parameter is IEnumerable<string>);
        AddManualEngineCommand = new AsyncRelayCommand(AddManualEngineAsync, () => !IsBusy);
        RemoveManualEngineCommand = new AsyncRelayCommand(RemoveManualEngineAsync, () => !IsBusy && SelectedManualEngineRoot is not null);
        SaveAppearanceCommand = new AsyncRelayCommand(SaveAppearanceAsync, () => !IsBusy);
        RescanCommand = new AsyncRelayCommand(RescanAsync, () => !IsBusy);
    }

    public ReadOnlyObservableCollection<string> SearchRoots { get; }

    public ReadOnlyObservableCollection<string> ManualEngineRoots { get; }

    public IReadOnlyList<ThemeMode> ThemeModes { get; }

    public IReadOnlyList<RowDensity> RowDensities { get; }

    public IReadOnlyList<AppLanguage> Languages { get; }

    public IReadOnlyList<SettingOption<ThemeMode>> ThemeOptions => _themeOptions;

    public IReadOnlyList<SettingOption<RowDensity>> RowDensityOptions => _rowDensityOptions;

    public IReadOnlyList<SettingOption<AppLanguage>> LanguageOptions => _languageOptions;

    public string? SelectedSearchRoot
    {
        get => _selectedSearchRoot;
        set
        {
            if (SetProperty(ref _selectedSearchRoot, value))
            {
                RemoveSearchRootCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? SelectedManualEngineRoot
    {
        get => _selectedManualEngineRoot;
        set
        {
            if (SetProperty(ref _selectedManualEngineRoot, value))
            {
                RemoveManualEngineCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ThemeMode SelectedThemeMode
    {
        get => _selectedThemeMode;
        set => SetProperty(ref _selectedThemeMode, value);
    }

    public RowDensity SelectedRowDensity
    {
        get => _selectedRowDensity;
        set => SetProperty(ref _selectedRowDensity, value);
    }

    public AppLanguage SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetProperty(ref _selectedLanguage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public AsyncRelayCommand AddSearchRootCommand { get; }

    public AsyncRelayCommand RemoveSearchRootCommand { get; }

    public AsyncRelayCommand AddDroppedSearchRootsCommand { get; }

    public AsyncRelayCommand AddManualEngineCommand { get; }

    public AsyncRelayCommand RemoveManualEngineCommand { get; }

    public AsyncRelayCommand SaveAppearanceCommand { get; }

    public AsyncRelayCommand RescanCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await RunBusyAsync(async () =>
        {
            var settings = await _operations.LoadSettingsAsync(cancellationToken);
            ApplySettings(settings);
            StatusText = Localize("String.StatusSettingsLoaded", "Settings loaded.");
        });
    }

    private async Task AddSearchRootAsync()
    {
        var selected = _folderPicker.PickFolder(Localize("String.SelectSearchRoot", "Select a project search root"));
        if (selected is not null)
        {
            await ApplyOperationAsync(
                () => _operations.AddProjectSearchRootAsync(selected),
                "Search root saved.",
                "String.StatusSearchRootSaved");
        }
    }

    private async Task AddDroppedSearchRootsAsync(object? parameter)
    {
        if (parameter is not IEnumerable<string> folders)
        {
            return;
        }

        foreach (var folder in folders)
        {
            var result = await _operations.AddProjectSearchRootAsync(folder);
            if (!result.IsSuccess)
            {
                StatusText = result.Message ?? "Search root could not be saved.";
                return;
            }

            if (result.Settings is not null)
            {
                ApplySettings(result.Settings);
            }
        }

        StatusText = Localize("String.StatusSearchRootsSaved", "Search roots saved.");
    }

    private Task RemoveSearchRootAsync() => ApplyOperationAsync(
        () => _operations.RemoveProjectSearchRootAsync(SelectedSearchRoot!),
        "Search root removed.",
        "String.StatusSearchRootRemoved");

    private async Task AddManualEngineAsync()
    {
        var selected = _folderPicker.PickFolder(Localize("String.SelectEngineRoot", "Select an Unreal Engine root"));
        if (selected is not null)
        {
            await ApplyOperationAsync(
                () => _operations.AddManualEngineRootAsync(selected),
                "Manual engine saved.",
                "String.StatusManualEngineSaved");
        }
    }

    private Task RemoveManualEngineAsync() => ApplyOperationAsync(
        () => _operations.RemoveManualEngineRootAsync(SelectedManualEngineRoot!),
        "Manual engine removed.",
        "String.StatusManualEngineRemoved");

    private Task SaveAppearanceAsync() => ApplyOperationAsync(
        () => _operations.SaveAppearanceAsync(
            SelectedThemeMode,
            SelectedRowDensity,
            SelectedLanguage),
        "Appearance saved.",
        "String.StatusAppearanceSaved");

    private async Task RescanAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _operations.RescanAsync();
            if (!result.IsSuccess)
            {
                StatusText = result.Message ?? "Rescan failed.";
                return;
            }

            if (result.Snapshot is not null)
            {
                _projectsRescanned?.Invoke(result.Snapshot);
            }

            StatusText = result.Issues.Count == 0
                ? Localize("String.StatusRescanComplete", "Rescan complete.")
                : string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Localize("String.StatusRescanIssuesFormat", "Rescan complete with {0} issue(s)."),
                    result.Issues.Count);
        });
    }

    private async Task ApplyOperationAsync(
        Func<Task<ProjectOperationResult>> operation,
        string successMessage,
        string? successResourceKey = null)
    {
        await RunBusyAsync(async () =>
        {
            var result = await operation();
            if (!result.IsSuccess || result.Settings is null)
            {
                StatusText = result.Message
                    ?? Localize("String.StatusSettingSaveFailed", "The setting could not be saved.");
                return;
            }

            ApplySettings(result.Settings);
            StatusText = successResourceKey is null
                ? successMessage
                : Localize(successResourceKey, successMessage);
        });
    }

    private async Task RunBusyAsync(Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            StatusText = Localize("String.StatusOperationCanceled", "Operation canceled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        Replace(_searchRoots, settings.ProjectSearchRoots);
        Replace(_manualEngineRoots, settings.ManualEngineRoots);
        SelectedSearchRoot = null;
        SelectedManualEngineRoot = null;
        SelectedThemeMode = settings.ThemeMode;
        SelectedRowDensity = settings.RowDensity;
        SelectedLanguage = settings.Language;
        _settingsApplied?.Invoke(settings);
    }

    private void RaiseCommandStates()
    {
        AddSearchRootCommand.RaiseCanExecuteChanged();
        RemoveSearchRootCommand.RaiseCanExecuteChanged();
        AddDroppedSearchRootsCommand.RaiseCanExecuteChanged();
        AddManualEngineCommand.RaiseCanExecuteChanged();
        RemoveManualEngineCommand.RaiseCanExecuteChanged();
        SaveAppearanceCommand.RaiseCanExecuteChanged();
        RescanCommand.RaiseCanExecuteChanged();
    }

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private string Localize(string key, string fallback) =>
        _localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;

    private void OnLanguageChanged(object? sender, EventArgs eventArgs)
    {
        _themeOptions[0].SetLabel(Localize("String.ThemeSystem", "System"));
        _themeOptions[1].SetLabel(Localize("String.ThemeLight", "Light"));
        _themeOptions[2].SetLabel(Localize("String.ThemeDark", "Dark"));
        _rowDensityOptions[0].SetLabel(Localize("String.DensityNormal", "Normal"));
        _rowDensityOptions[1].SetLabel(Localize("String.DensityCompact", "Compact"));
        _languageOptions[0].SetLabel(Localize("String.LanguageEnglish", "English"));
        _languageOptions[1].SetLabel(Localize("String.LanguageKorean", "한국어"));
    }
}

public sealed class SettingOption<T>(T value, string label) : ObservableObject
{
    private string _label = label;

    public T Value { get; } = value;

    public string Label => _label;

    internal void SetLabel(string label) =>
        SetProperty(ref _label, label, nameof(Label));
}
