using UProjectHub.Core.Settings;

namespace UProjectHub.Core.Tests.Settings;

[TestClass]
public sealed class SettingsMutationServiceTests
{
    [TestMethod]
    public async Task ConcurrentUpdatesPreserveBothChangesAsync()
    {
        var repository = new ControlledSettingsRepository(new AppSettings());
        var service = new SettingsMutationService(repository);
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var first = service.UpdateAsync(async (settings, cancellationToken) =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task.WaitAsync(cancellationToken);
            return settings with { ProjectSearchRoots = [@"C:\Projects"] };
        });
        await firstEntered.Task;

        var second = service.UpdateAsync((settings, _) => Task.FromResult(
            settings with { RowDensity = RowDensity.Compact }));
        releaseFirst.SetResult();

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));

        CollectionAssert.AreEqual(
            new[] { @"C:\Projects" },
            repository.Current.ProjectSearchRoots.ToArray());
        Assert.AreEqual(RowDensity.Compact, repository.Current.RowDensity);
    }

    [TestMethod]
    public async Task FailedSaveReleasesBoundaryForRetryAsync()
    {
        var repository = new ControlledSettingsRepository(new AppSettings())
        {
            SaveException = new IOException("disk unavailable"),
        };
        var service = new SettingsMutationService(repository);

        await Assert.ThrowsExactlyAsync<IOException>(() => service.UpdateAsync(
            settings => settings with { RowDensity = RowDensity.Compact }));

        repository.SaveException = null;
        await service.UpdateAsync(
                settings => settings with { RowDensity = RowDensity.Compact })
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(RowDensity.Compact, repository.Current.RowDensity);
    }

    private sealed class ControlledSettingsRepository(AppSettings settings)
        : ISettingsRepository
    {
        public AppSettings Current { get; private set; } = settings;

        public Exception? SaveException { get; set; }

        public Task<AppSettings> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (SaveException is not null)
            {
                return Task.FromException(SaveException);
            }

            Current = settings;
            return Task.CompletedTask;
        }
    }
}
