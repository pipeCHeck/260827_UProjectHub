using System.IO;
using System.Security;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;
using UProjectHub.Core.Settings;

namespace UProjectHub.App.Services;

public sealed class CurrentEngineSnapshot
{
    private InstalledEngine[] _engines = [];

    public IReadOnlyList<InstalledEngine> Engines =>
        Array.AsReadOnly(Volatile.Read(ref _engines));

    public void Replace(IEnumerable<InstalledEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        Volatile.Write(ref _engines, engines.ToArray());
    }

    public EngineResolution Resolve(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return EngineResolver.Resolve(
            project.EngineAssociation,
            Volatile.Read(ref _engines));
    }
}

public sealed class ApplicationCoordinator
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly IProjectCacheRepository _projectCacheRepository;
    private readonly IEngineCacheRepository _engineCacheRepository;
    private readonly ProjectCatalog _catalog;
    private readonly CurrentEngineSnapshot _engines;
    private readonly ThemeService _themeService;
    private readonly LocalizationService? _localizationService;
    private readonly MainViewModel _mainViewModel;
    private readonly StatusBarViewModel _statusBar;
    private readonly BackgroundRefreshService _backgroundRefresh;
    private readonly IUiDispatcher _dispatcher;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _taskGate = new();
    private readonly HashSet<Task> _runningTasks = [];
    private int _started;

    public ApplicationCoordinator(
        ISettingsRepository settingsRepository,
        IProjectCacheRepository projectCacheRepository,
        IEngineCacheRepository engineCacheRepository,
        ProjectCatalog catalog,
        CurrentEngineSnapshot engines,
        ThemeService themeService,
        MainViewModel mainViewModel,
        StatusBarViewModel statusBar,
        BackgroundRefreshService backgroundRefresh,
        IUiDispatcher dispatcher,
        IAppLogger logger,
        LocalizationService? localizationService = null)
    {
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _projectCacheRepository = projectCacheRepository ?? throw new ArgumentNullException(nameof(projectCacheRepository));
        _engineCacheRepository = engineCacheRepository ?? throw new ArgumentNullException(nameof(engineCacheRepository));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _engines = engines ?? throw new ArgumentNullException(nameof(engines));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _localizationService = localizationService;
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _backgroundRefresh = backgroundRefresh ?? throw new ArgumentNullException(nameof(backgroundRefresh));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        var startupToken = linked.Token;
        _logger.Info("Application startup.");
        var settings = await LoadSettingsAsync(startupToken);
        await _dispatcher.InvokeAsync(() =>
        {
            _localizationService?.ApplySettings(settings);
            _themeService.ApplySettings(settings);
            _mainViewModel.ApplySettings(settings);
        }, startupToken);

        var projectCache = await LoadProjectCacheAsync(startupToken);
        var engineCache = await LoadEngineCacheAsync(startupToken);
        _engines.Replace(engineCache.Engines.Select(ToInstalledEngine));
        RestoreCatalog(projectCache, settings);

        await _dispatcher.InvokeAsync(
            () =>
            {
                _mainViewModel.SetEngines(_engines.Engines);
                _mainViewModel.SetProjects(_catalog.GetSnapshot());
            },
            startupToken);
        _logger.Info($"Published {projectCache.Projects.Count} cached project(s).");

        var refreshTask = RunStartupRefreshOperationAsync(
            settings,
            _lifetimeCancellation.Token);
        Track(refreshTask);
    }

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        var settings = await LoadSettingsAsync(linked.Token);
        var task = RunRefreshOperationAsync(settings, linked.Token);
        Track(task);
        return await task;
    }

    public async Task<ProjectRescanOperationResult> RescanAsync(
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        var settings = await LoadSettingsAsync(linked.Token);
        var task = RunRescanOperationAsync(settings, linked.Token);
        Track(task);
        return await task;
    }

    public async Task StopAsync()
    {
        _lifetimeCancellation.Cancel();
        Task[] tasks;
        lock (_taskGate)
        {
            tasks = _runningTasks.ToArray();
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Background operation canceled during shutdown.");
        }
    }

    private async Task<bool> RunRefreshOperationAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var result = await RunOperationAsync(
            "String.StatusRefreshing",
            "Background refresh",
            () => _backgroundRefresh.RefreshAsync(settings, cancellationToken),
            cancellationToken);
        return result is not null;
    }

    private async Task<bool> RunStartupRefreshOperationAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var result = await RunOperationAsync(
            "String.StatusRefreshing",
            "Background refresh",
            () => _backgroundRefresh.StartupRefreshAsync(settings, cancellationToken),
            cancellationToken);
        return result is not null;
    }

    private async Task<ProjectRescanOperationResult> RunRescanOperationAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var result = await RunOperationAsync(
            "String.StatusRescanning",
            "Project rescan",
            () => _backgroundRefresh.RescanAsync(settings, cancellationToken),
            cancellationToken);
        return result is null
            ? new ProjectRescanOperationResult(
                false,
                null,
                [],
                "Another project operation is already running.")
            : new ProjectRescanOperationResult(
                true,
                result.Snapshot,
                result.ProjectIssues,
                null);
    }

    private async Task<BackgroundRefreshResult?> RunOperationAsync(
        string activeStatusResourceKey,
        string operationName,
        Func<Task<BackgroundRefreshResult>> operation,
        CancellationToken cancellationToken)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
        {
            _logger.Warning($"{operationName} was skipped because another operation is active.");
            return null;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            _statusBar.SetOperationActive(true);
            _statusBar.SetLocalizedStatus(activeStatusResourceKey, operationName);
        });
        _logger.Info($"{operationName} started.");

        try
        {
            var result = await operation();
            await _dispatcher.InvokeAsync(
                () => _mainViewModel.SetEngines(result.Engines),
                cancellationToken);
            LogIssues(result);
            await SaveFinalCachesAsync(result, cancellationToken);
            _logger.Info($"{operationName} completed.");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Info($"{operationName} canceled.");
            throw;
        }
        catch (Exception exception) when (IsExpectedOperationalFailure(exception))
        {
            _logger.Error($"{operationName} failed.", exception);
            return null;
        }
        finally
        {
            _operationGate.Release();
            await _dispatcher.InvokeAsync(() =>
            {
                _statusBar.SetOperationActive(false);
                _statusBar.SetLocalizedStatus("String.StatusReady", "Ready");
            });
        }
    }

    private async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settingsRepository.LoadAsync(cancellationToken);
            _logger.Info("Settings loaded.");
            return settings;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedOperationalFailure(exception))
        {
            _logger.Warning($"Settings load failed; defaults are in use. {exception.Message}");
            return new AppSettings();
        }
    }

    private async Task<ProjectCacheDocument> LoadProjectCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            var document = await _projectCacheRepository.LoadAsync(cancellationToken);
            _logger.Info($"Project cache loaded with {document.Projects.Count} entry(ies).");
            return document;
        }
        catch (Exception exception) when (IsExpectedOperationalFailure(exception))
        {
            _logger.Warning($"Project cache load failed. {exception.Message}");
            return new ProjectCacheDocument();
        }
    }

    private async Task<EngineCacheDocument> LoadEngineCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            var document = await _engineCacheRepository.LoadAsync(cancellationToken);
            _logger.Info($"Engine cache loaded with {document.Engines.Count} entry(ies).");
            return document;
        }
        catch (Exception exception) when (IsExpectedOperationalFailure(exception))
        {
            _logger.Warning($"Engine cache load failed. {exception.Message}");
            return new EngineCacheDocument();
        }
    }

    private void RestoreCatalog(ProjectCacheDocument document, AppSettings settings)
    {
        foreach (var entry in document.Projects)
        {
            var userState = settings.ProjectUserStates.FirstOrDefault(state =>
                state.ProjectPath.Equals(entry.ProjectFilePath));
            var project = new UnrealProject(
                entry.Name,
                entry.ProjectFilePath,
                entry.EngineAssociation,
                entry.EngineDisplayVersion,
                entry.ProjectType,
                entry.LastModified,
                userState?.LastLaunched,
                userState?.IsFavorite ?? false,
                entry.ProjectState,
                entry.EngineState);
            var resolution = _engines.Resolve(project);
            _catalog.Upsert(project with
            {
                EngineState = resolution.State,
                EngineDisplayVersion = resolution.ResolvedCandidate?.DisplayVersion
                    ?? project.EngineDisplayVersion,
            });
        }
    }

    private async Task SaveFinalCachesAsync(
        BackgroundRefreshResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await _projectCacheRepository.SaveAsync(
                ToProjectCache(result.Snapshot),
                cancellationToken);
        }
        catch (Exception exception) when (IsExpectedOperationalFailure(exception))
        {
            _logger.Error("Final project cache save failed.", exception);
        }

        try
        {
            await _engineCacheRepository.SaveAsync(
                ToEngineCache(result.Engines),
                cancellationToken);
        }
        catch (Exception exception) when (IsExpectedOperationalFailure(exception))
        {
            _logger.Error("Final engine cache save failed.", exception);
        }
    }

    private void LogIssues(BackgroundRefreshResult result)
    {
        foreach (var issue in result.ProjectIssues)
        {
            _logger.Warning($"Project refresh issue at {issue.Path}: {issue.Message}");
        }

        foreach (var issue in result.EngineIssues)
        {
            _logger.Warning($"Engine provider issue at {issue.Context}: {issue.Message}");
        }

        foreach (var issue in result.KnownRootIssues)
        {
            _logger.Warning($"Known project root issue at {issue.Path}: {issue.Message}");
        }

        foreach (var project in result.Snapshot.Projects.Where(project =>
            project.EngineState is EngineResolutionState.Missing
                or EngineResolutionState.Ambiguous
                or EngineResolutionState.Unknown))
        {
            _logger.Warning($"Engine resolution {project.EngineState} for {project.ProjectFilePath.Value}.");
        }
    }

    private void Track(Task task)
    {
        lock (_taskGate)
        {
            _runningTasks.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                lock (_taskGate)
                {
                    _runningTasks.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static InstalledEngine ToInstalledEngine(EngineCacheEntry entry) => new(
        entry.DisplayName,
        entry.Association,
        entry.DisplayVersion,
        entry.RootPath,
        entry.EditorPath,
        entry.Source,
        entry.IsUsable);

    private static ProjectCacheDocument ToProjectCache(ProjectCatalogSnapshot snapshot) => new()
    {
        Projects = snapshot.Projects.Select(project => new ProjectCacheEntry(
            project.ProjectFilePath,
            project.Name,
            project.EngineAssociation,
            project.EngineDisplayVersion,
            project.ProjectType,
            project.LastModified,
            project.ProjectState,
            project.EngineState)).ToArray(),
    };

    private static EngineCacheDocument ToEngineCache(IEnumerable<InstalledEngine> engines) => new()
    {
        Engines = engines.Select(engine => new EngineCacheEntry(
            engine.DisplayName,
            engine.Association,
            engine.DisplayVersion,
            engine.RootPath,
            engine.EditorPath,
            engine.Source,
            engine.IsUsable)).ToArray(),
    };

    private static bool IsExpectedOperationalFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;
}
