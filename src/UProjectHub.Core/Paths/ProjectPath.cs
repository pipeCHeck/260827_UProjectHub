namespace UProjectHub.Core.Paths;

public sealed class ProjectPath : IEquatable<ProjectPath>
{
    public ProjectPath(string path)
    {
        Value = Path.GetFullPath(path)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    public string Value { get; }

    public bool Equals(ProjectPath? other) =>
        other is not null &&
        StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override bool Equals(object? obj) =>
        obj is ProjectPath other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
}
