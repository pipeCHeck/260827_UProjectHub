using System.Globalization;
using System.Text;
using UProjectHub.Core.Models;

namespace UProjectHub.Core.Searching;

public sealed class ProjectQueryParser
{
    public ProjectQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ProjectQuery([]);
        }

        var terms = Tokenize(text)
            .Select(ParseToken)
            .ToArray();

        return new ProjectQuery(terms);
    }

    private static ProjectQueryTerm ParseToken(string token)
    {
        var separatorIndex = token.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return new PlainTextTerm(token);
        }

        var prefix = token[..separatorIndex];
        var rawValue = token[(separatorIndex + 1)..];

        if (prefix.Equals("version", StringComparison.OrdinalIgnoreCase))
        {
            return TryReadValue(rawValue, out var value)
                ? new VersionTerm(value)
                : new PlainTextTerm(token);
        }

        if (prefix.Equals("type", StringComparison.OrdinalIgnoreCase))
        {
            return ParseProjectType(rawValue) ?? new PlainTextTerm(token);
        }

        if (prefix.Equals("path", StringComparison.OrdinalIgnoreCase))
        {
            return TryReadValue(rawValue, out var value)
                ? new PathTerm(value)
                : new PlainTextTerm(token);
        }

        if (prefix.Equals("modified", StringComparison.OrdinalIgnoreCase))
        {
            return ParseModifiedWithin(rawValue) ?? new PlainTextTerm(token);
        }

        if (prefix.Equals("favorite", StringComparison.OrdinalIgnoreCase))
        {
            return ParseFavorite(rawValue) ?? new PlainTextTerm(token);
        }

        if (prefix.Equals("tag", StringComparison.OrdinalIgnoreCase))
        {
            return TryReadValue(rawValue, out var value)
                ? new TagTerm(value)
                : new PlainTextTerm(token);
        }

        if (prefix.Equals("note", StringComparison.OrdinalIgnoreCase))
        {
            return TryReadValue(rawValue, out var value)
                ? new NoteTerm(value)
                : new PlainTextTerm(token);
        }

        return new PlainTextTerm(token);
    }

    private static ProjectQueryTerm? ParseProjectType(string rawValue)
    {
        if (!TryReadValue(rawValue, out var value))
        {
            return null;
        }

        if (value.Equals("cpp", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectTypeTerm(ProjectType.Cpp);
        }

        if (value.Equals("bp", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectTypeTerm(ProjectType.Blueprint);
        }

        return null;
    }

    private static ProjectQueryTerm? ParseModifiedWithin(string rawValue)
    {
        if (!TryReadValue(rawValue, out var value)
            || value.Length < 2
            || value[^1] is not ('d' or 'D')
            || !int.TryParse(
                value.AsSpan(0, value.Length - 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var days)
            || days <= 0)
        {
            return null;
        }

        return new ModifiedWithinTerm(days);
    }

    private static ProjectQueryTerm? ParseFavorite(string rawValue)
    {
        if (!TryReadValue(rawValue, out var value)
            || !value.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new FavoriteTerm(true);
    }

    private static bool TryReadValue(string rawValue, out string value)
    {
        value = string.Empty;
        if (rawValue.Length == 0)
        {
            return false;
        }

        var startsWithQuote = rawValue[0] == '"';
        var endsWithQuote = rawValue[^1] == '"';

        if (startsWithQuote || endsWithQuote)
        {
            if (!startsWithQuote || !endsWithQuote || rawValue.Length == 2)
            {
                return false;
            }

            value = rawValue[1..^1];
            return !value.Contains('"');
        }

        if (rawValue.Contains('"'))
        {
            return false;
        }

        value = rawValue;
        return true;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var token = new StringBuilder();
        var insideQuotes = false;

        foreach (var character in text)
        {
            if (character == '"')
            {
                insideQuotes = !insideQuotes;
                _ = token.Append(character);
                continue;
            }

            if (char.IsWhiteSpace(character) && !insideQuotes)
            {
                if (token.Length > 0)
                {
                    yield return token.ToString();
                    _ = token.Clear();
                }

                continue;
            }

            _ = token.Append(character);
        }

        if (token.Length > 0)
        {
            yield return token.ToString();
        }
    }
}
