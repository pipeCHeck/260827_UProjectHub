using System.Collections.ObjectModel;
using UProjectHub.Core.Models;

namespace UProjectHub.Core.Catalog;

public sealed class ProjectCatalogSnapshot
{
    internal ProjectCatalogSnapshot(IEnumerable<UnrealProject> projects)
    {
        Projects = new ReadOnlyCollection<UnrealProject>(projects.ToArray());
    }

    public IReadOnlyList<UnrealProject> Projects { get; }
}
