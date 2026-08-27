using UProjectHub.Core.Models;
using UProjectHub.Core.Settings;

namespace UProjectHub.Windows.Engines.Manual;

public sealed class ManualEngineProvider : IEngineProvider
{
    private readonly IReadOnlyList<string> _manualEngineRoots;
    private readonly ManualEngineValidator _validator;

    public ManualEngineProvider(
        AppSettings settings,
        ManualEngineValidator validator)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(validator);

        _manualEngineRoots = Array.AsReadOnly(
            settings.ManualEngineRoots.ToArray());
        _validator = validator;
    }

    public async Task<EngineProviderResult> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var engines = new List<InstalledEngine>();
        var issues = new List<EngineProviderIssue>();

        foreach (var manualRoot in _manualEngineRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _validator.ValidateAsync(
                manualRoot,
                cancellationToken).ConfigureAwait(false);
            engines.AddRange(result.Engines);
            issues.AddRange(result.Issues);
        }

        return new EngineProviderResult(engines, issues);
    }
}
