namespace UProjectHub.Core.Settings;

public static class ProjectTagNormalizer
{
    public static bool TryNormalizeTag(
        string? tag,
        out string normalized,
        out ProjectTagValidationError error)
    {
        normalized = tag?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            error = ProjectTagValidationError.Empty;
            return false;
        }

        if (normalized.Contains('"'))
        {
            error = ProjectTagValidationError.DoubleQuote;
            return false;
        }

        if (normalized.Any(char.IsControl))
        {
            error = ProjectTagValidationError.ControlCharacter;
            return false;
        }

        error = ProjectTagValidationError.None;
        return true;
    }

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

public enum ProjectTagValidationError
{
    None,
    Empty,
    DoubleQuote,
    ControlCharacter,
}
