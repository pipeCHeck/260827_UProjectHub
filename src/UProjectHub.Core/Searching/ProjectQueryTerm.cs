using UProjectHub.Core.Models;

namespace UProjectHub.Core.Searching;

public abstract record ProjectQueryTerm;

public sealed record PlainTextTerm(string Text) : ProjectQueryTerm;

public sealed record VersionTerm(string Value) : ProjectQueryTerm;

public sealed record ProjectTypeTerm(ProjectType ProjectType) : ProjectQueryTerm;

public sealed record PathTerm(string Value) : ProjectQueryTerm;

public sealed record ModifiedWithinTerm(int Days) : ProjectQueryTerm;

public sealed record FavoriteTerm(bool IsFavorite) : ProjectQueryTerm;
