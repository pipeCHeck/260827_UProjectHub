using System.Diagnostics.CodeAnalysis;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Catalog;

public sealed class ProjectCatalog
{
    private readonly object _gate = new();
    private readonly Dictionary<ProjectPath, UnrealProject> _projects = [];

    public void Upsert(UnrealProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        lock (_gate)
        {
            _projects[project.ProjectFilePath] = project;
        }
    }

    public bool TryGet(
        ProjectPath projectPath,
        [NotNullWhen(true)] out UnrealProject? project)
    {
        ArgumentNullException.ThrowIfNull(projectPath);

        lock (_gate)
        {
            return _projects.TryGetValue(projectPath, out project);
        }
    }

    public bool MarkMissing(ProjectPath projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);

        lock (_gate)
        {
            if (!_projects.TryGetValue(projectPath, out var project))
            {
                return false;
            }

            _projects[projectPath] = project with
            {
                ProjectState = ProjectState.Missing,
            };
            return true;
        }
    }

    public bool Remove(ProjectPath projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);

        lock (_gate)
        {
            return _projects.Remove(projectPath);
        }
    }

    public ProjectCatalogSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new ProjectCatalogSnapshot(_projects.Values);
        }
    }
}
