namespace UProjectHub.Windows.Projects;

public sealed class UnrealEditorSettingsParser
{
    private const string CreatedProjectPathsKey = "CreatedProjectPaths";

    public IReadOnlyList<string> ParseCreatedProjectPaths(string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);

        var roots = new List<string>();
        using var reader = new StringReader(contents);
        while (reader.ReadLine() is { } line)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.Length == 0
                || trimmedLine.StartsWith(';')
                || trimmedLine.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmedLine.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = trimmedLine[..separatorIndex].Trim();
            if (!string.Equals(
                key,
                CreatedProjectPathsKey,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = trimmedLine[(separatorIndex + 1)..].Trim();
            if (value.Length > 0)
            {
                roots.Add(value);
            }
        }

        return Array.AsReadOnly(roots.ToArray());
    }
}
