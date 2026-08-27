namespace UProjectHub.Windows.Registry;

public interface IRegistryReader
{
    IReadOnlyList<RegistryValueEntry> ReadCurrentUserValues(
        string subKeyPath,
        CancellationToken cancellationToken = default);
}

public sealed record RegistryValueEntry(
    string Name,
    object? Value);
