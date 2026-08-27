using UProjectHub.Windows.Registry;

namespace UProjectHub.Core.Tests.Windows.Registry;

internal sealed class FakeRegistryReader : IRegistryReader
{
    private readonly string? _subKeyPath;
    private readonly IReadOnlyList<RegistryValueEntry> _values;
    private readonly Exception? _exception;

    private FakeRegistryReader(
        string? subKeyPath,
        IReadOnlyList<RegistryValueEntry> values,
        Exception? exception)
    {
        _subKeyPath = subKeyPath;
        _values = values;
        _exception = exception;
    }

    public static FakeRegistryReader ForCurrentUserKey(
        string subKeyPath,
        params RegistryValueEntry[] values) =>
        new(subKeyPath, Array.AsReadOnly(values), null);

    public static FakeRegistryReader Empty() =>
        new(null, Array.Empty<RegistryValueEntry>(), null);

    public static FakeRegistryReader Throwing(Exception exception) =>
        new(null, Array.Empty<RegistryValueEntry>(), exception);

    public IReadOnlyList<RegistryValueEntry> ReadCurrentUserValues(
        string subKeyPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_exception is not null)
        {
            throw _exception;
        }

        return string.Equals(
            subKeyPath,
            _subKeyPath,
            StringComparison.OrdinalIgnoreCase)
            ? _values
            : Array.Empty<RegistryValueEntry>();
    }
}
