using System.Globalization;
using UProjectHub.Core.Models;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectInformationViewModel
{
    private const string Unknown = "—";
    private const string ExactTimestampFormat = "yyyy-MM-dd HH:mm:ss zzz";

    public ProjectInformationViewModel(
        UnrealProject project,
        TimeZoneInfo? timeZone = null)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        var displayTimeZone = timeZone ?? TimeZoneInfo.Local;

        Name = project.Name;
        ProjectPath = project.ProjectFilePath.Value;
        ProjectDirectory = project.ProjectDirectory;
        EngineAssociation = EmptyAsUnknown(project.EngineAssociation);
        EngineDisplayVersion = EmptyAsUnknown(project.EngineDisplayVersion);
        ProjectType = project.ProjectState == UProjectHub.Core.Models.ProjectState.Broken
            ? Unknown
            : project.ProjectType switch
            {
                UProjectHub.Core.Models.ProjectType.Cpp => "C++",
                UProjectHub.Core.Models.ProjectType.Blueprint => "Blueprint",
                _ => Unknown,
            };
        ProjectState = project.ProjectState.ToString();
        EngineState = project.EngineState.ToString();
        IsFavorite = project.IsFavorite;
        LastModified = FormatTimestamp(project.LastModified, displayTimeZone, Unknown);
        LastLaunched = project.LastLaunched is { } lastLaunched
            ? FormatTimestamp(lastLaunched, displayTimeZone, "Never")
            : "Never";
    }

    public UnrealProject Project { get; }

    public string Name { get; }

    public string ProjectPath { get; }

    public string ProjectDirectory { get; }

    public string EngineAssociation { get; }

    public string EngineDisplayVersion { get; }

    public string ProjectType { get; }

    public string ProjectState { get; }

    public string EngineState { get; }

    public bool IsFavorite { get; }

    public string LastModified { get; }

    public string LastLaunched { get; }

    private static string EmptyAsUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Unknown : value;

    private static string FormatTimestamp(
        DateTimeOffset timestamp,
        TimeZoneInfo timeZone,
        string unknownText)
    {
        if (timestamp == DateTimeOffset.MinValue)
        {
            return unknownText;
        }

        return TimeZoneInfo.ConvertTime(timestamp, timeZone)
            .ToString(ExactTimestampFormat, CultureInfo.InvariantCulture);
    }
}
