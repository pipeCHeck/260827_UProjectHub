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

    public ManagedProjectRemovalService(
        ProjectCatalog catalog,
        IProjectCacheRepository cacheRepository,
        ISettingsRepository settingsRepository)
        : this(catalog, cacheRepository, new SettingsMutationService(settingsRepository))
    {
    }

    public ManagedProjectRemovalService(
        ProjectCatalog catalog,
        IProjectCacheRepository cacheRepository,
        SettingsMutationService settings)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(cacheRepository);
        ArgumentNullException.ThrowIfNull(settings);

        _catalog = catalog;
        _cacheRepository = cacheRepository;
        _settings = settings;
    }

    public async Task<ManagedProjectRemovalResult> RemoveMissingAsync(
        ProjectPath projectPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectPath);

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
        if (!_catalog.Remove(projectPath))
        {
            return ManagedProjectRemovalResult.NotFound;
        }

        await _cacheRepository.SaveAsync(updatedCache, cancellationToken);
        await _settings.UpdateAsync(settings => settings with
        {
            ProjectUserStates = settings.ProjectUserStates
                .Where(state => !state.ProjectPath.Equals(projectPath))
                .ToArray(),
        }, cancellationToken);
        return ManagedProjectRemovalResult.Removed;
    }
}
