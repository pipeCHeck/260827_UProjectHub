using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Settings;

public sealed record ProjectUserState(
    ProjectPath ProjectPath,
    bool IsFavorite = false,
    DateTimeOffset? LastLaunched = null)
{
    public IReadOnlyList<string> Tags { get; init; } = [];

    public string Note { get; init; } = string.Empty;

    public bool Equals(ProjectUserState? other)
    {
        return ReferenceEquals(this, other)
            || other is not null
            && ProjectPath.Equals(other.ProjectPath)
            && IsFavorite == other.IsFavorite
            && LastLaunched == other.LastLaunched
            && Tags.SequenceEqual(other.Tags, StringComparer.Ordinal)
            && string.Equals(Note, other.Note, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProjectPath);
        hash.Add(IsFavorite);
        hash.Add(LastLaunched);
        foreach (var tag in Tags)
        {
            hash.Add(tag, StringComparer.Ordinal);
        }

        hash.Add(Note, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
