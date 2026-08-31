namespace UProjectHub.Core.Catalog;

public sealed class ProjectCatalogOperationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        _gate.WaitAsync(cancellationToken);

    public Task<bool> TryWaitAsync(CancellationToken cancellationToken = default) =>
        _gate.WaitAsync(0, cancellationToken);

    public void Release() => _gate.Release();
}
