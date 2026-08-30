namespace UProjectHub.Core.Settings;

public static class ProjectTagNormalizer
{
    public static IReadOnlyList<string> Normalize(IEnumerable<string?>? tags)
    {
        if (tags is null)
        {
            return [];
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            var trimmed = tag?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        return normalized;
    }
}
