using UProjectHub.Core.Models;

namespace UProjectHub.Windows.Engines;

public sealed class EngineDiscoveryResult
{
    public EngineDiscoveryResult(
        IEnumerable<InstalledEngine> engines,
        IEnumerable<EngineProviderIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(issues);

        Engines = Array.AsReadOnly(engines.ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public IReadOnlyList<InstalledEngine> Engines { get; }

    public IReadOnlyList<EngineProviderIssue> Issues { get; }
}
