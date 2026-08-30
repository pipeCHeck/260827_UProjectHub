using UProjectHub.Core.Models;
using UProjectHub.Core.Settings;

namespace UProjectHub.App.Services;

public sealed class ProjectTagIndex
{
    private IReadOnlyList<string> _knownTags = [];

    public IReadOnlyList<string> KnownTags => _knownTags;

    public void Rebuild(IEnumerable<UnrealProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        _knownTags = ProjectTagNormalizer.Normalize(
                projects.SelectMany(project => project.Tags))
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tag => tag, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<string> GetSuggestions(string? input)
    {
        var query = input?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return [];
        }

        var prefix = _knownTags
            .Where(tag => tag.StartsWith(query, StringComparison.OrdinalIgnoreCase));
        var contains = _knownTags.Where(tag =>
            !tag.StartsWith(query, StringComparison.OrdinalIgnoreCase)
            && tag.Contains(query, StringComparison.OrdinalIgnoreCase));
        return prefix.Concat(contains).ToArray();
    }
}
