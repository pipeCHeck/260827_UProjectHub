using System.IO;
using System.Security;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Discovery;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;
using UProjectHub.Windows.Engines.Manual;

namespace UProjectHub.App.Services;

public sealed class ProjectOperations : IProjectOperations
{
    private readonly SettingsMutationService _settings;
    private readonly ManualEngineValidator _manualEngineValidator;
    private readonly ThemeService _themeService;
    private readonly LocalizationService _localizationService;
    private readonly ProjectCatalog _catalog;
    private readonly Func<IReadOnlyList<string>, AppSettings, CancellationToken, Task<ProjectRefreshResult>> _rescan;
    private readonly Func<CancellationToken, Task<ProjectRescanOperationResult>>? _coordinatedRescan;

    public ProjectOperations(
        ISettingsRepository settingsRepository,
        ManualEngineValidator manualEngineValidator,
        ThemeService themeService,
        LocalizationService localizationService,
        ProjectCatalog catalog,
        Func<IReadOnlyList<string>, AppSettings, CancellationToken, Task<ProjectRefreshResult>> rescan,
        Func<CancellationToken, Task<ProjectRescanOperationResult>>? coordinatedRescan = null)
        : this(
            new SettingsMutationService(settingsRepository),
            manualEngineValidator,
            themeService,
            localizationService,
            catalog,
            rescan,
            coordinatedRescan)
    {
    }

    public ProjectOperations(
        SettingsMutationService settings,
        ManualEngineValidator manualEngineValidator,
        ThemeService themeService,
        LocalizationService localizationService,
        ProjectCatalog catalog,
        Func<IReadOnlyList<string>, AppSettings, CancellationToken, Task<ProjectRefreshResult>> rescan,
        Func<CancellationToken, Task<ProjectRescanOperationResult>>? coordinatedRescan = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _manualEngineValidator = manualEngineValidator ?? throw new ArgumentNullException(nameof(manualEngineValidator));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _localizationService = localizationService
            ?? throw new ArgumentNullException(nameof(localizationService));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _rescan = rescan ?? throw new ArgumentNullException(nameof(rescan));
        _coordinatedRescan = coordinatedRescan;
    }

    public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
        _settings.LoadAsync(cancellationToken);

    public async Task<ProjectOperationResult> AddProjectSearchRootAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeRoot(root, out var normalized, out var error))
        {
            return Failure(error);
        }

        return await MutateAsync(settings =>
        {
            if (ContainsPath(settings.ProjectSearchRoots, normalized))
            {
                return Mutation.Unchanged(settings);
            }

            return Mutation.Changed(settings with
            {
                ProjectSearchRoots = [.. settings.ProjectSearchRoots, normalized],
            });
        }, cancellationToken);
    }

    public async Task<ProjectOperationResult> RemoveProjectSearchRootAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeRoot(root, out var normalized, out var error))
        {
            return Failure(error);
        }

        return await MutateAsync(settings =>
        {
            var remaining = settings.ProjectSearchRoots
                .Where(item => !PathEquals(item, normalized))
                .ToArray();
            return remaining.Length == settings.ProjectSearchRoots.Count
                ? Mutation.Unchanged(settings)
                : Mutation.Changed(settings with { ProjectSearchRoots = remaining });
        }, cancellationToken);
    }

    public async Task<ProjectOperationResult> AddManualEngineRootAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeRoot(root, out var normalized, out var error))
        {
            return Failure(error);
        }

        try
        {
            var changed = false;
            string? validationError = null;
            var updated = await _settings.UpdateAsync(async (settings, token) =>
            {
                if (ContainsPath(settings.ManualEngineRoots, normalized))
                {
                    return settings;
                }

                var validation = await _manualEngineValidator.ValidateAsync(
                    normalized,
                    token);
                var usable = validation.Engines.FirstOrDefault(engine => engine.IsUsable);
                if (usable is null)
                {
                    validationError = validation.Issues.FirstOrDefault()?.Message
                        ?? "The selected folder is not a usable Unreal Engine root.";
                    return settings;
                }

                changed = true;
                return settings with
                {
                    ManualEngineRoots = [.. settings.ManualEngineRoots, usable.RootPath],
                };
            }, cancellationToken);
            return validationError is null
                ? Success(updated, changed)
                : Failure(validationError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedPersistenceFailure(exception))
        {
            return Failure($"Settings could not be saved. {exception.Message}");
        }
    }

    public async Task<ProjectOperationResult> RemoveManualEngineRootAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeRoot(root, out var normalized, out var error))
        {
            return Failure(error);
        }

        return await MutateAsync(settings =>
        {
            var remaining = settings.ManualEngineRoots
                .Where(item => !PathEquals(item, normalized))
                .ToArray();
            return remaining.Length == settings.ManualEngineRoots.Count
                ? Mutation.Unchanged(settings)
                : Mutation.Changed(settings with { ManualEngineRoots = remaining });
        }, cancellationToken);
    }

    public async Task<ProjectOperationResult> SaveAppearanceAsync(
        ThemeMode themeMode,
        RowDensity rowDensity,
        AppLanguage language,
        CancellationToken cancellationToken = default)
    {
        var result = await MutateAsync(settings =>
        {
            if (settings.ThemeMode == themeMode
                && settings.RowDensity == rowDensity
                && settings.Language == language)
            {
                return Mutation.Unchanged(settings);
            }

            return Mutation.Changed(settings with
            {
                ThemeMode = themeMode,
                RowDensity = rowDensity,
                Language = language,
            });
        }, cancellationToken);

        if (result.IsSuccess && result.Settings is not null)
        {
            _themeService.ApplySettings(result.Settings);
            _localizationService.ApplySettings(result.Settings);
        }

        return result;
    }

    public Task<ProjectOperationResult> SaveViewStateAsync(
        ProjectSortDefinition activeSort,
        VisibleFilterState visibleFilters,
        IReadOnlyList<ColumnLayoutState>? columnLayout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeSort);
        ArgumentNullException.ThrowIfNull(visibleFilters);

        return MutateAsync(settings => Mutation.Changed(settings with
        {
            ActiveSort = activeSort,
            VisibleFilters = visibleFilters,
            ColumnLayout = columnLayout?.ToArray() ?? settings.ColumnLayout,
        }), cancellationToken);
    }

    public async Task<ProjectRescanOperationResult> RescanAsync(
        CancellationToken cancellationToken = default)
    {
        if (_coordinatedRescan is not null)
        {
            return await _coordinatedRescan(cancellationToken);
        }

        try
        {
            var settings = await _settings.LoadAsync(cancellationToken);
            var result = await _rescan(
                settings.ProjectSearchRoots.ToArray(),
                settings,
                cancellationToken);
            return new ProjectRescanOperationResult(
                true,
                _catalog.GetSnapshot(),
                result.Issues,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedPersistenceFailure(exception))
        {
            return new ProjectRescanOperationResult(false, null, [], exception.Message);
        }
    }

    private async Task<ProjectOperationResult> MutateAsync(
        Func<AppSettings, Mutation> mutate,
        CancellationToken cancellationToken)
    {
        try
        {
            var changed = false;
            var updated = await _settings.UpdateAsync(settings =>
            {
                var mutation = mutate(settings);
                changed = mutation.HasChanges;
                return mutation.Settings;
            }, cancellationToken);
            return Success(updated, changed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedPersistenceFailure(exception))
        {
            return Failure($"Settings could not be saved. {exception.Message}");
        }
    }

    private static bool TryNormalizeRoot(string? root, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(root))
        {
            error = "A non-empty folder path is required.";
            return false;
        }

        var trimmed = root.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            error = "The folder path must be absolute.";
            return false;
        }

        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException or SecurityException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool ContainsPath(IEnumerable<string> paths, string candidate) =>
        paths.Any(path => PathEquals(path, candidate));

    private static bool PathEquals(string path, string candidate) =>
        TryNormalizeRoot(path, out var normalized, out _)
        && string.Equals(normalized, candidate, StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedPersistenceFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;

    private static ProjectOperationResult Success(AppSettings settings, bool changed) =>
        new(true, changed, settings, null);

    private static ProjectOperationResult Failure(string message) =>
        new(false, false, null, message);

    private sealed record Mutation(AppSettings Settings, bool HasChanges)
    {
        public static Mutation Changed(AppSettings settings) => new(settings, true);

        public static Mutation Unchanged(AppSettings settings) => new(settings, false);
    }
}
