using System.Globalization;

namespace UProjectHub.Core.Versions;

public readonly record struct EngineVersion(int Major, int Minor, int? Patch)
{
    public static bool TryParse(string? value, out EngineVersion version)
    {
        version = default;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var components = value.Split('.');
        if (components.Length is not (2 or 3)
            || !TryParseComponent(components[0], out var major)
            || !TryParseComponent(components[1], out var minor))
        {
            return false;
        }

        int? patch = null;
        if (components.Length == 3)
        {
            if (!TryParseComponent(components[2], out var parsedPatch))
            {
                return false;
            }

            patch = parsedPatch;
        }

        version = new EngineVersion(major, minor, patch);
        return true;
    }

    private static bool TryParseComponent(string value, out int component)
    {
        return int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out component);
    }
}
