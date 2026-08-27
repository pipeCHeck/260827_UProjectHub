using Microsoft.Win32;

namespace UProjectHub.Windows.Registry;

public sealed class WindowsRegistryReader : IRegistryReader
{
    public IReadOnlyList<RegistryValueEntry> ReadCurrentUserValues(
        string subKeyPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKeyPath);
        cancellationToken.ThrowIfCancellationRequested();

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            subKeyPath,
            writable: false);
        if (key is null)
        {
            return Array.Empty<RegistryValueEntry>();
        }

        var entries = new List<RegistryValueEntry>();
        foreach (var valueName in key.GetValueNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = key.GetValue(
                valueName,
                defaultValue: null,
                RegistryValueOptions.DoNotExpandEnvironmentNames);
            entries.Add(new RegistryValueEntry(valueName, value));
        }

        return Array.AsReadOnly(entries.ToArray());
    }
}
