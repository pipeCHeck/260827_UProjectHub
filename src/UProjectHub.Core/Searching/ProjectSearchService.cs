using UProjectHub.Core.Models;
using UProjectHub.Core.Time;

namespace UProjectHub.Core.Searching;

public sealed class ProjectSearchService
{
    private readonly IClock _clock;

    public ProjectSearchService(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    public bool Matches(UnrealProject project, ProjectQuery query)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(query);

        return query.Terms.All(term => MatchesTerm(project, term));
    }

    private bool MatchesTerm(UnrealProject project, ProjectQueryTerm term)
    {
        return term switch
        {
            PlainTextTerm plainText => MatchesPlainText(project, plainText.Text),
            VersionTerm version => MatchesEngineMetadata(project, version.Value),
            ProjectTypeTerm projectType => project.ProjectType == projectType.ProjectType,
            PathTerm path => Contains(project.ProjectFilePath.Value, path.Value),
            ModifiedWithinTerm modified => MatchesModified(project, modified.Days),
            FavoriteTerm favorite => project.IsFavorite == favorite.IsFavorite,
            _ => false,
        };
    }

    private static bool MatchesPlainText(UnrealProject project, string text)
    {
        return Contains(project.Name, text)
            || Contains(project.ProjectFilePath.Value, text)
            || MatchesEngineMetadata(project, text)
            || MatchesProjectTypeText(project.ProjectType, text);
    }

    private static bool MatchesEngineMetadata(UnrealProject project, string text)
    {
        return Contains(project.EngineDisplayVersion, text)
            || Contains(project.EngineAssociation, text);
    }

    private static bool MatchesProjectTypeText(ProjectType projectType, string text)
    {
        return projectType switch
        {
            ProjectType.Cpp => Contains("cpp", text) || Contains("c++", text),
            ProjectType.Blueprint =>
                Contains("blueprint", text) || Contains("bp", text),
            _ => false,
        };
    }

    private bool MatchesModified(UnrealProject project, int days)
    {
        if (days <= 0)
        {
            return false;
        }

        var cutoff = _clock.UtcNow - TimeSpan.FromDays(days);
        return project.LastModified >= cutoff;
    }

    private static bool Contains(string? source, string value)
    {
        return source?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;
    }
}
