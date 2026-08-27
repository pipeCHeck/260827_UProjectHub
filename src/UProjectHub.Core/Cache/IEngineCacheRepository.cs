namespace UProjectHub.Core.Cache;

public interface IEngineCacheRepository
{
    Task<EngineCacheDocument> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        EngineCacheDocument document,
        CancellationToken cancellationToken = default);
}
