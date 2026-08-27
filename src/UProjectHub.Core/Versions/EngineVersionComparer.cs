namespace UProjectHub.Core.Versions;

public sealed class EngineVersionComparer : IComparer<string?>
{
    public static EngineVersionComparer Instance { get; } = new();

    private EngineVersionComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return 1;
        }

        if (y is null)
        {
            return -1;
        }

        var xIsNumeric = EngineVersion.TryParse(x, out var xVersion);
        var yIsNumeric = EngineVersion.TryParse(y, out var yVersion);

        if (xIsNumeric && yIsNumeric)
        {
            return CompareNumeric(xVersion, yVersion);
        }

        if (xIsNumeric)
        {
            return -1;
        }

        if (yIsNumeric)
        {
            return 1;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(x, y);
    }

    private static int CompareNumeric(EngineVersion x, EngineVersion y)
    {
        var majorComparison = x.Major.CompareTo(y.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = x.Minor.CompareTo(y.Minor);
        if (minorComparison != 0)
        {
            return minorComparison;
        }

        return (x.Patch ?? 0).CompareTo(y.Patch ?? 0);
    }
}
