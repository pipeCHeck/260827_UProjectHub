using System.Collections.ObjectModel;
using System.Windows.Input;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Models;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectNotesViewModel : ObservableObject, IDisposable
{
    private readonly ProjectUserMetadataService _metadata;
    private readonly LocalizationService? _localization;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly ObservableCollection<string> _tags;
    private readonly AsyncRelayCommand _addTagCommand;
    private readonly AsyncRelayCommand _removeTagCommand;
    private readonly AsyncRelayCommand _saveNoteCommand;
    private string _newTag = string.Empty;
    private string _noteText;
    private string _savedNote;
    private string? _statusMessage;
    private bool _hasError;
    private bool _isDisposed;

    public ProjectNotesViewModel(
        UnrealProject project,
        ProjectUserMetadataService metadata,
        LocalizationService? localization = null)
    {
        ProjectPath = project?.ProjectFilePath
            ?? throw new ArgumentNullException(nameof(project));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _localization = localization;
        _tags = new ObservableCollection<string>(project.Tags);
        Tags = new ReadOnlyObservableCollection<string>(_tags);
        _noteText = project.Note;
        _savedNote = project.Note;

        _addTagCommand = new AsyncRelayCommand(AddTagAsync, CanAddTag);
        _removeTagCommand = new AsyncRelayCommand(RemoveTagAsync, CanRemoveTag);
        _saveNoteCommand = new AsyncRelayCommand(SaveNoteAsync, () => IsNoteDirty);
    }

    private UProjectHub.Core.Paths.ProjectPath ProjectPath { get; }

    public ReadOnlyObservableCollection<string> Tags { get; }

    public string NewTag
    {
        get => _newTag;
        set
        {
            if (SetProperty(ref _newTag, value ?? string.Empty))
            {
                _addTagCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NoteText
    {
        get => _noteText;
        set
        {
            if (SetProperty(ref _noteText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsNoteDirty));
                _saveNoteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNoteDirty =>
        !string.Equals(_noteText, _savedNote, StringComparison.Ordinal);

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public ICommand AddTagCommand => _addTagCommand;

    public ICommand RemoveTagCommand => _removeTagCommand;

    public ICommand SaveNoteCommand => _saveNoteCommand;

    private bool CanAddTag() => !string.IsNullOrWhiteSpace(NewTag);

    private static bool CanRemoveTag(object? parameter) => parameter is string;

    private async Task AddTagAsync()
    {
        var result = await _metadata.AddTagAsync(
            ProjectPath,
            NewTag,
            _lifetimeCancellation.Token);
        if (!Accept(result))
        {
            return;
        }

        ReplaceTags(result.Project!.Tags);
        NewTag = string.Empty;
    }

    private async Task RemoveTagAsync(object? parameter)
    {
        if (parameter is not string tag)
        {
            return;
        }

        var result = await _metadata.RemoveTagAsync(
            ProjectPath,
            tag,
            _lifetimeCancellation.Token);
        if (Accept(result))
        {
            ReplaceTags(result.Project!.Tags);
        }
    }

    private async Task SaveNoteAsync()
    {
        var result = await _metadata.SaveNoteAsync(
            ProjectPath,
            NoteText,
            _lifetimeCancellation.Token);
        if (!Accept(result))
        {
            return;
        }

        _savedNote = result.Project!.Note;
        HasError = false;
        StatusMessage = Localize("String.NoteSaved", "Note saved.");
        OnPropertyChanged(nameof(IsNoteDirty));
        _saveNoteCommand.RaiseCanExecuteChanged();
    }

    private bool Accept(ProjectUserMetadataResult result)
    {
        if (_isDisposed)
        {
            return false;
        }

        HasError = !result.IsSuccess;
        StatusMessage = result.IsSuccess ? null : result.ErrorMessage;
        return result.IsSuccess && result.Project is not null;
    }

    private void ReplaceTags(IEnumerable<string> tags)
    {
        _tags.Clear();
        foreach (var tag in tags)
        {
            _tags.Add(tag);
        }
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
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }
}
