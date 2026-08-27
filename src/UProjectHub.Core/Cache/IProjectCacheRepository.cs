namespace UProjectHub.Core.Cache;

public interface IProjectCacheRepository
{
    Task<ProjectCacheDocument> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        ProjectCacheDocument document,
        CancellationToken cancellationToken = default);
}
