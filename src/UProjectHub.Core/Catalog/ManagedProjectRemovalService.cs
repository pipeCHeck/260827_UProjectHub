using UProjectHub.Core.Cache;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Settings;

namespace UProjectHub.Core.Catalog;

public enum ManagedProjectRemovalResult
{
    Removed,
    NotFound,
    NotMissing,
}

public sealed class ManagedProjectRemovalService
{
    private readonly ProjectCatalog _catalog;
    private readonly IProjectCacheRepository _cacheRepository;
    private readonly SettingsMutationService _settings;
    private readonly ProjectCatalogOperationGate _operationGate;

    public ManagedProjectRemovalService(
        ProjectCatalog catalog,
        IProjectCacheRepository cacheRepository,
        ISettingsRepository settingsRepository,
        ProjectCatalogOperationGate operationGate)
        : this(
            catalog,
            cacheRepository,
            new SettingsMutationService(settingsRepository),
            operationGate)
    {
    }

    public ManagedProjectRemovalService(
        ProjectCatalog catalog,
        IProjectCacheRepository cacheRepository,
        SettingsMutationService settings,
        ProjectCatalogOperationGate operationGate)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(cacheRepository);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(operationGate);

        _catalog = catalog;
        _cacheRepository = cacheRepository;
        _settings = settings;
        _operationGate = operationGate;
    }

    public async Task<ManagedProjectRemovalResult> RemoveMissingAsync(
        ProjectPath projectPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectPath);

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            return await RemoveMissingCoreAsync(projectPath, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<ManagedProjectRemovalResult> RemoveMissingCoreAsync(
        ProjectPath projectPath,
        CancellationToken cancellationToken)
    {
        if (!_catalog.TryGet(projectPath, out var project))
        {
            return ManagedProjectRemovalResult.NotFound;
        }

        if (project.ProjectState != ProjectState.Missing)
        {
            return ManagedProjectRemovalResult.NotMissing;
        }

        var cache = await _cacheRepository.LoadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var updatedCache = cache with
        {
            Projects = cache.Projects
                .Where(entry => !entry.ProjectFilePath.Equals(projectPath))
                .ToArray(),
        };
        await _cacheRepository.SaveAsync(updatedCache, cancellationToken);
        try
        {
            await _settings.UpdateAsync(settings => settings with
            {
                ProjectUserStates = settings.ProjectUserStates
                    .Where(state => !state.ProjectPath.Equals(projectPath))
                    .ToArray(),
            }, cancellationToken);
        }
        catch
        {
            await _cacheRepository.SaveAsync(cache, CancellationToken.None);
            throw;
        }

        return _catalog.Remove(projectPath)
            ? ManagedProjectRemovalResult.Removed
            : ManagedProjectRemovalResult.NotFound;
    }
}
