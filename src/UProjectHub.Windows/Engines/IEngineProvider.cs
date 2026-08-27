namespace UProjectHub.Windows.Engines;

public interface IEngineProvider
{
    Task<EngineProviderResult> DiscoverAsync(
        CancellationToken cancellationToken = default);
}
