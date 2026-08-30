using System.Collections.ObjectModel;
using System.Windows.Input;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Models;
using UProjectHub.Core.Settings;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectNotesViewModel : ObservableObject, IDisposable
{
    private readonly ProjectUserMetadataService _metadata;
    private readonly LocalizationService? _localization;
    private readonly ProjectTagIndex? _tagIndex;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly ObservableCollection<string> _tags;
    private readonly ObservableCollection<string> _tagSuggestions = [];
    private readonly AsyncRelayCommand _addTagCommand;
    private readonly AsyncRelayCommand _removeTagCommand;
    private readonly AsyncRelayCommand _saveNoteCommand;
    private string _newTag = string.Empty;
    private string? _selectedTagSuggestion;
    private bool _isSuggestionsOpen;
    private string _noteText;
    private string _savedNote;
    private string? _tagStatusMessage;
    private string? _noteStatusMessage;
    private bool _hasTagError;
    private bool _hasNoteError;
    private bool _isDisposed;

    public ProjectNotesViewModel(
        UnrealProject project,
        ProjectUserMetadataService metadata,
        LocalizationService? localization = null,
        ProjectTagIndex? tagIndex = null)
    {
        ProjectPath = project?.ProjectFilePath
            ?? throw new ArgumentNullException(nameof(project));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _localization = localization;
        _tagIndex = tagIndex;
        _tags = new ObservableCollection<string>(project.Tags);
        Tags = new ReadOnlyObservableCollection<string>(_tags);
        TagSuggestions = new ReadOnlyObservableCollection<string>(_tagSuggestions);
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
                if (IsSuggestionsOpen
                    && SelectedTagSuggestion is { } selectedSuggestion
                    && string.Equals(
                        _newTag,
                        selectedSuggestion,
                        StringComparison.Ordinal))
                {
                    return;
                }

                RefreshSuggestions();
            }
        }
    }

    public ReadOnlyObservableCollection<string> TagSuggestions { get; }

    public string? SelectedTagSuggestion
    {
        get => _selectedTagSuggestion;
        set => SetProperty(ref _selectedTagSuggestion, value);
    }

    public bool IsSuggestionsOpen
    {
        get => _isSuggestionsOpen;
        set => SetProperty(ref _isSuggestionsOpen, value);
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
        get => TagStatusMessage ?? NoteStatusMessage;
    }

    public bool HasError => HasTagError || HasNoteError;

    public string? TagStatusMessage => _tagStatusMessage;

    public string? NoteStatusMessage => _noteStatusMessage;

    public bool HasTagError => _hasTagError;

    public bool HasNoteError => _hasNoteError;

    public ICommand AddTagCommand => _addTagCommand;

    public ICommand RemoveTagCommand => _removeTagCommand;

    public ICommand SaveNoteCommand => _saveNoteCommand;

    private bool CanAddTag() => !string.IsNullOrWhiteSpace(NewTag);

    private static bool CanRemoveTag(object? parameter) => parameter is string;

    private async Task AddTagAsync()
    {
        if (!ProjectTagNormalizer.TryNormalizeTag(
                NewTag,
                out var normalized,
                out var validationError))
        {
            SetTagStatus(GetTagValidationMessage(validationError), isError: true);
            return;
        }

        var result = await _metadata.AddTagAsync(
            ProjectPath,
            normalized,
            _lifetimeCancellation.Token);
        if (!AcceptTag(result))
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
        if (AcceptTag(result))
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
        if (!AcceptNote(result))
        {
            return;
        }

        _savedNote = result.Project!.Note;
        SetNoteStatus(Localize("String.NoteSaved", "Note saved."), isError: false);
        OnPropertyChanged(nameof(IsNoteDirty));
        _saveNoteCommand.RaiseCanExecuteChanged();
    }

    private bool AcceptTag(ProjectUserMetadataResult result)
    {
        if (_isDisposed)
        {
            return false;
        }

        SetTagStatus(result.IsSuccess ? null : result.ErrorMessage, !result.IsSuccess);
        return result.IsSuccess && result.Project is not null;
    }

    private bool AcceptNote(ProjectUserMetadataResult result)
    {
        if (_isDisposed)
        {
            return false;
        }

        SetNoteStatus(result.IsSuccess ? null : result.ErrorMessage, !result.IsSuccess);
        return result.IsSuccess && result.Project is not null;
    }

    private void SetTagStatus(string? message, bool isError)
    {
        _tagStatusMessage = message;
        _hasTagError = isError;
        OnPropertyChanged(nameof(TagStatusMessage));
        OnPropertyChanged(nameof(HasTagError));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HasError));
    }

    private void SetNoteStatus(string? message, bool isError)
    {
        _noteStatusMessage = message;
        _hasNoteError = isError;
        OnPropertyChanged(nameof(NoteStatusMessage));
        OnPropertyChanged(nameof(HasNoteError));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HasError));
    }

    private void ReplaceTags(IEnumerable<string> tags)
    {
        _tags.Clear();
        foreach (var tag in tags)
        {
            _tags.Add(tag);
        }
    }

    private void RefreshSuggestions()
    {
        _selectedTagSuggestion = null;
        OnPropertyChanged(nameof(SelectedTagSuggestion));
        _tagSuggestions.Clear();
        if (_tagIndex is not null)
        {
            foreach (var suggestion in _tagIndex.GetSuggestions(NewTag)
                         .Where(suggestion => !_tags.Contains(
                             suggestion,
                             StringComparer.OrdinalIgnoreCase)))
            {
                _tagSuggestions.Add(suggestion);
            }
        }

        IsSuggestionsOpen = _tagSuggestions.Count > 0;
    }

    private string GetTagValidationMessage(ProjectTagValidationError error) =>
        error switch
        {
            ProjectTagValidationError.Empty => Localize(
                "String.TagEmptyError",
                "A tag cannot be empty."),
            ProjectTagValidationError.DoubleQuote => Localize(
                "String.TagDoubleQuoteError",
                "A tag cannot contain a double quote because tag search cannot represent it."),
            ProjectTagValidationError.ControlCharacter => Localize(
                "String.TagControlCharacterError",
                "A tag cannot contain a newline or other control character."),
            _ => Localize("String.TagInvalidError", "The tag is invalid."),
        };

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
