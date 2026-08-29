using System.Collections.ObjectModel;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Models;
using UProjectHub.Windows.Launching;

namespace UProjectHub.App.ViewModels;

public sealed class NewProjectViewModel : ObservableObject
{
    private readonly IUnrealEditorLauncher _launcher;
    private readonly StatusBarViewModel _statusBar;
    private readonly LocalizationService? _localization;
    private readonly ObservableCollection<EngineLaunchOption> _engineOptions = [];
    private EngineLaunchOption _selectedEngineOption;

    public NewProjectViewModel(
        IUnrealEditorLauncher launcher,
        StatusBarViewModel statusBar,
        LocalizationService? localization = null)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _localization = localization;
        _selectedEngineOption = CreatePlaceholder();

        _engineOptions.Add(_selectedEngineOption);
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }
        EngineOptions = new ReadOnlyObservableCollection<EngineLaunchOption>(
            _engineOptions);
        LaunchCommand = new RelayCommand(
            Launch,
            () => SelectedEngine is not null);
    }

    public ReadOnlyObservableCollection<EngineLaunchOption> EngineOptions { get; }

    public EngineLaunchOption SelectedEngineOption
    {
        get => _selectedEngineOption;
        set
        {
            var normalized = value ?? _engineOptions[0];
            if (SetProperty(ref _selectedEngineOption, normalized))
            {
                OnPropertyChanged(nameof(SelectedEngine));
                LaunchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public InstalledEngine? SelectedEngine
    {
        get => SelectedEngineOption.Engine;
        set => SelectedEngineOption = value is null
            ? _engineOptions[0]
            : _engineOptions.FirstOrDefault(option => string.Equals(
                option.Engine?.EditorPath,
                value.EditorPath,
                StringComparison.OrdinalIgnoreCase)) ?? _engineOptions[0];
    }

    public RelayCommand LaunchCommand { get; }

    public void SetEngines(IEnumerable<InstalledEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);

        var selectedEditorPath = SelectedEngine?.EditorPath;
        while (_engineOptions.Count > 1)
        {
            _engineOptions.RemoveAt(_engineOptions.Count - 1);
        }

        foreach (var engine in engines.Where(engine => engine.IsUsable))
        {
            _engineOptions.Add(new EngineLaunchOption(
                engine.DisplayName,
                engine));
        }

        _selectedEngineOption = selectedEditorPath is null
            ? _engineOptions[0]
            : _engineOptions.FirstOrDefault(option => string.Equals(
                option.Engine?.EditorPath,
                selectedEditorPath,
                StringComparison.OrdinalIgnoreCase)) ?? _engineOptions[0];
        OnPropertyChanged(nameof(SelectedEngineOption));
        OnPropertyChanged(nameof(SelectedEngine));
        OnPropertyChanged(nameof(EngineOptions));
        LaunchCommand.RaiseCanExecuteChanged();
    }

    private void Launch()
    {
        var engine = SelectedEngine;
        if (engine is null)
        {
            return;
        }

        var result = _launcher.LaunchNewProject(engine);
        _statusBar.SetStatus(result.IsSuccess
            ? Localize("String.StatusEditorStarted", "Unreal Editor started.")
            : result.ErrorMessage
                ?? Localize("String.StatusEditorStartFailed", "Unreal Editor could not be started."));
    }

    private EngineLaunchOption CreatePlaceholder() => new(
        Localize("String.SelectVersion", "Select version"),
        null);

    private string Localize(string key, string fallback) =>
        _localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;

    private void OnLanguageChanged(object? sender, EventArgs eventArgs)
    {
        var wasPlaceholderSelected = SelectedEngine is null;
        var placeholder = CreatePlaceholder();
        _engineOptions[0] = placeholder;
        if (wasPlaceholderSelected)
        {
            _selectedEngineOption = placeholder;
            OnPropertyChanged(nameof(SelectedEngineOption));
        }
    }
}

public sealed record EngineLaunchOption(
    string Label,
    InstalledEngine? Engine);
