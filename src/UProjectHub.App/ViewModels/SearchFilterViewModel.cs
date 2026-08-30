using System.Collections.ObjectModel;
using System.Windows.Input;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Filtering;
using UProjectHub.Core.Models;
using UProjectHub.Core.Searching;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;
using UProjectHub.Core.Versions;

namespace UProjectHub.App.ViewModels;

public sealed class SearchFilterViewModel : ObservableObject
{
    private readonly ProjectListViewModel _projectList;
    private readonly ProjectQueryParser _queryParser;
    private readonly ProjectFilterService _filterService;
    private readonly ProjectSortService _sortService;
    private readonly ProjectTagIndex _tagIndex;
    private readonly LocalizationService? _localization;
    private readonly ObservableCollection<string> _engineOptions = [];
    private readonly ObservableCollection<EngineFilterOption> _engineFilterOptions = [];
    private readonly ObservableCollection<string> _tagOptions = [];
    private readonly ObservableCollection<TagFilterOption> _tagFilterOptions = [];
    private IReadOnlyList<UnrealProject> _rawProjects = [];
    private string _searchText = string.Empty;
    private string? _selectedEngine;
    private ProjectType? _selectedProjectType;
    private string? _selectedTag;
    private bool _favoritesOnly;
    private ProjectSortDefinition _activeSort = new();
    private bool _hasSnapshot;
    private bool _isUpdatingState;

    public SearchFilterViewModel(
        ProjectListViewModel projectList,
        ProjectQueryParser queryParser,
        ProjectFilterService filterService,
        ProjectSortService sortService,
        LocalizationService? localization = null,
        ProjectTagIndex? tagIndex = null)
    {
        _projectList = projectList ?? throw new ArgumentNullException(nameof(projectList));
        _queryParser = queryParser ?? throw new ArgumentNullException(nameof(queryParser));
        _filterService = filterService ?? throw new ArgumentNullException(nameof(filterService));
        _sortService = sortService ?? throw new ArgumentNullException(nameof(sortService));
        _tagIndex = tagIndex ?? new ProjectTagIndex();
        _localization = localization;

        EngineOptions = new ReadOnlyObservableCollection<string>(_engineOptions);
        EngineFilterOptions = new ReadOnlyObservableCollection<EngineFilterOption>(_engineFilterOptions);
        TagOptions = new ReadOnlyObservableCollection<string>(_tagOptions);
        TagFilterOptions = new ReadOnlyObservableCollection<TagFilterOption>(_tagFilterOptions);
        ProjectTypeOptions = CreateProjectTypeOptions();
        _engineFilterOptions.Add(new EngineFilterOption(AllLabel, null));
        _tagFilterOptions.Add(new TagFilterOption(AllLabel, null));
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }
        ResetCommand = new RelayCommand(Reset);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
    }

    public event EventHandler? PersistedStateChanged;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                OnCriteriaChanged(persist: false);
            }
        }
    }

    public string? SelectedEngine
    {
        get => _selectedEngine;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (SetProperty(ref _selectedEngine, normalized))
            {
                OnPropertyChanged(nameof(SelectedEngineFilterOption));
                OnCriteriaChanged();
            }
        }
    }

    public ProjectType? SelectedProjectType
    {
        get => _selectedProjectType;
        set
        {
            if (SetProperty(ref _selectedProjectType, value))
            {
                OnCriteriaChanged();
            }
        }
    }

    public bool FavoritesOnly
    {
        get => _favoritesOnly;
        set
        {
            if (SetProperty(ref _favoritesOnly, value))
            {
                OnCriteriaChanged();
            }
        }
    }

    public string? SelectedTag
    {
        get => _selectedTag;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (SetProperty(ref _selectedTag, normalized))
            {
                OnPropertyChanged(nameof(SelectedTagFilterOption));
                OnCriteriaChanged();
            }
        }
    }

    public ReadOnlyObservableCollection<string> EngineOptions { get; }

    public ReadOnlyObservableCollection<EngineFilterOption> EngineFilterOptions { get; }

    public ReadOnlyObservableCollection<string> TagOptions { get; }

    public ReadOnlyObservableCollection<TagFilterOption> TagFilterOptions { get; }

    public EngineFilterOption SelectedEngineFilterOption
    {
        get => _engineFilterOptions.FirstOrDefault(option => string.Equals(
                option.Value,
                SelectedEngine,
                StringComparison.OrdinalIgnoreCase))
            ?? _engineFilterOptions[0];
        set => SelectedEngine = value?.Value;
    }

    public TagFilterOption SelectedTagFilterOption
    {
        get => _tagFilterOptions.FirstOrDefault(option => string.Equals(
                option.Value,
                SelectedTag,
                StringComparison.OrdinalIgnoreCase))
            ?? _tagFilterOptions[0];
        set => SelectedTag = value?.Value;
    }

    public IReadOnlyList<ProjectTypeFilterOption> ProjectTypeOptions { get; private set; }

    public ProjectSortDefinition ActiveSort
    {
        get => _activeSort;
        private set
        {
            if (SetProperty(ref _activeSort, value))
            {
                ApplyPipeline();
                RaisePersistedStateChanged();
            }
        }
    }

    public bool HasActiveSearchOrFilters =>
        !string.IsNullOrWhiteSpace(SearchText)
        || SelectedEngine is not null
        || SelectedProjectType is not null
        || SelectedTag is not null
        || FavoritesOnly;

    public ICommand ResetCommand { get; }

    public ICommand ClearSearchCommand { get; }

    public VisibleFilterState VisibleFilters => new(
        SelectedEngine,
        SelectedProjectType,
        FavoritesOnly,
        SelectedTag);

    public void SetSnapshot(ProjectCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _rawProjects = snapshot.Projects.ToArray();
        _projectList.SetSnapshot(snapshot);
        RebuildEngineOptions();
        _tagIndex.Rebuild(_rawProjects);
        RebuildTagOptions();
        _hasSnapshot = true;

        if (SelectedEngine is not null
            && !EngineOptions.Contains(SelectedEngine, StringComparer.OrdinalIgnoreCase))
        {
            _isUpdatingState = true;
            SelectedEngine = null;
            _isUpdatingState = false;
            OnPropertyChanged(nameof(HasActiveSearchOrFilters));
        }

        var normalizedStaleTag = false;
        if (SelectedTag is not null
            && !TagOptions.Contains(SelectedTag, StringComparer.OrdinalIgnoreCase))
        {
            _isUpdatingState = true;
            SelectedTag = null;
            _isUpdatingState = false;
            normalizedStaleTag = true;
            OnPropertyChanged(nameof(HasActiveSearchOrFilters));
        }

        ApplyPipeline();
        if (normalizedStaleTag)
        {
            RaisePersistedStateChanged();
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _isUpdatingState = true;
        SelectedEngine = settings.VisibleFilters.Engine;
        SelectedProjectType = settings.VisibleFilters.ProjectType;
        FavoritesOnly = settings.VisibleFilters.FavoritesOnly;
        SelectedTag = settings.VisibleFilters.Tag;
        ActiveSort = settings.ActiveSort;
        _isUpdatingState = false;
        OnPropertyChanged(nameof(HasActiveSearchOrFilters));

        if (_hasSnapshot
            && SelectedEngine is not null
            && !EngineOptions.Contains(SelectedEngine, StringComparer.OrdinalIgnoreCase))
        {
            _isUpdatingState = true;
            SelectedEngine = null;
            _isUpdatingState = false;
            OnPropertyChanged(nameof(HasActiveSearchOrFilters));
        }

        var normalizedStaleTag = false;
        if (_hasSnapshot
            && SelectedTag is not null
            && !TagOptions.Contains(SelectedTag, StringComparer.OrdinalIgnoreCase))
        {
            _isUpdatingState = true;
            SelectedTag = null;
            _isUpdatingState = false;
            normalizedStaleTag = true;
            OnPropertyChanged(nameof(HasActiveSearchOrFilters));
        }

        ApplyPipeline();
        if (normalizedStaleTag)
        {
            RaisePersistedStateChanged();
        }
    }

    public void RequestSort(ProjectSortColumn column)
    {
        var direction = ActiveSort.Column == column
            ? Reverse(ActiveSort.Direction)
            : GetInitialDirection(column);

        ActiveSort = new ProjectSortDefinition(column, direction);
    }

    private void Reset()
    {
        _isUpdatingState = true;
        SearchText = string.Empty;
        SelectedEngine = null;
        SelectedProjectType = null;
        SelectedTag = null;
        FavoritesOnly = false;
        _isUpdatingState = false;

        OnPropertyChanged(nameof(HasActiveSearchOrFilters));
        ApplyPipeline();
        PersistedStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCriteriaChanged(bool persist = true)
    {
        if (_isUpdatingState)
        {
            return;
        }

        OnPropertyChanged(nameof(HasActiveSearchOrFilters));
        ApplyPipeline();
        if (persist)
        {
            RaisePersistedStateChanged();
        }
    }

    private void RaisePersistedStateChanged()
    {
        if (!_isUpdatingState)
        {
            PersistedStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyPipeline()
    {
        if (_isUpdatingState || !_hasSnapshot)
        {
            return;
        }

        var query = _queryParser.Parse(SearchText);
        var filter = new ProjectFilter(
            SelectedEngine,
            SelectedProjectType,
            FavoritesOnly,
            SelectedTag);
        var filtered = _rawProjects.Where(project =>
            _filterService.Matches(project, query, filter));
        var sorted = _sortService.Sort(filtered, ActiveSort);

        _projectList.SetVisibleProjects(sorted);
    }

    private void RebuildEngineOptions()
    {
        var options = _rawProjects
            .Select(GetEngineOption)
            .Where(option => option is not null)
            .Select(option => option!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => option, EngineVersionComparer.Instance)
            .ThenBy(option => option, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _engineOptions.Clear();
        _engineFilterOptions.Clear();
        _engineFilterOptions.Add(new EngineFilterOption(AllLabel, null));
        foreach (var option in options)
        {
            _engineOptions.Add(option);
            _engineFilterOptions.Add(new EngineFilterOption(option, option));
        }

        OnPropertyChanged(nameof(SelectedEngineFilterOption));
    }

    private void RebuildTagOptions()
    {
        _tagOptions.Clear();
        _tagFilterOptions.Clear();
        _tagFilterOptions.Add(new TagFilterOption(AllLabel, null));
        foreach (var tag in _tagIndex.KnownTags)
        {
            _tagOptions.Add(tag);
            _tagFilterOptions.Add(new TagFilterOption(tag, tag));
        }

        OnPropertyChanged(nameof(SelectedTagFilterOption));
    }

    private static string? GetEngineOption(UnrealProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.EngineDisplayVersion))
        {
            return project.EngineDisplayVersion;
        }

        return string.IsNullOrWhiteSpace(project.EngineAssociation)
            ? null
            : project.EngineAssociation;
    }

    private string AllLabel =>
        _localization?.GetString("String.All") is { } value && value != "String.All"
            ? value
            : "All";

    private IReadOnlyList<ProjectTypeFilterOption> CreateProjectTypeOptions() =>
    [
        new ProjectTypeFilterOption(AllLabel, null),
        new ProjectTypeFilterOption("C++", ProjectType.Cpp),
        new ProjectTypeFilterOption("Blueprint", ProjectType.Blueprint),
    ];

    private void OnLanguageChanged(object? sender, EventArgs eventArgs)
    {
        ProjectTypeOptions = CreateProjectTypeOptions();
        OnPropertyChanged(nameof(ProjectTypeOptions));
        RebuildEngineOptions();
        RebuildTagOptions();
        ApplyPipeline();
    }

    private static SortDirection Reverse(SortDirection direction)
    {
        return direction switch
        {
            SortDirection.Ascending => SortDirection.Descending,
            SortDirection.Descending => SortDirection.Ascending,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
        };
    }

    private static SortDirection GetInitialDirection(ProjectSortColumn column)
    {
        return column switch
        {
            ProjectSortColumn.Name => SortDirection.Ascending,
            ProjectSortColumn.EngineVersion => SortDirection.Ascending,
            ProjectSortColumn.ProjectType => SortDirection.Ascending,
            ProjectSortColumn.LastModified => SortDirection.Descending,
            ProjectSortColumn.LastLaunched => SortDirection.Descending,
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, null),
        };
    }
}

public sealed record EngineFilterOption(string Label, string? Value);

public sealed record ProjectTypeFilterOption(string Label, ProjectType? Value);

public sealed record TagFilterOption(string Label, string? Value);
