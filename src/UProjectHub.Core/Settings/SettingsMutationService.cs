namespace UProjectHub.Core.Settings;

public sealed class SettingsMutationService
{
    private readonly ISettingsRepository _repository;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsMutationService(ISettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task<AppSettings> LoadAsync(
        CancellationToken cancellationToken = default) =>
        _repository.LoadAsync(cancellationToken);

    public Task<AppSettings> UpdateAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        return UpdateAsync(
            (settings, _) => Task.FromResult(update(settings)),
            cancellationToken);
    }

    public async Task<AppSettings> UpdateAsync(
        Func<AppSettings, CancellationToken, Task<AppSettings>> updateAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updateAsync);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await _repository.LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            var updated = await updateAsync(settings, cancellationToken)
                .ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(updated);

            if (!ReferenceEquals(settings, updated))
            {
                await _repository.SaveAsync(updated, cancellationToken)
                    .ConfigureAwait(false);
            }

            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }
}
