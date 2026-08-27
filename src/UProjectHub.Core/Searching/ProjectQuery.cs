namespace UProjectHub.Core.Searching;

public sealed class ProjectQuery
{
    public ProjectQuery(IEnumerable<ProjectQueryTerm> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        Terms = Array.AsReadOnly(terms.ToArray());
    }

    public IReadOnlyList<ProjectQueryTerm> Terms { get; }
}
