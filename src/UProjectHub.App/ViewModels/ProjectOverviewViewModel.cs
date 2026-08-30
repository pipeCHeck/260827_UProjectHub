using System.Globalization;
using UProjectHub.App.Services;
using UProjectHub.Core.Models;

namespace UProjectHub.App.ViewModels;

public sealed class ProjectOverviewViewModel
{
    private const string Unknown = "—";
    private const string ExactTimestampFormat = "yyyy-MM-dd HH:mm:ss zzz";

    public ProjectOverviewViewModel(
        UnrealProject project,
        TimeZoneInfo? timeZone = null,
        LocalizationService? localization = null)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        var displayTimeZone = timeZone ?? TimeZoneInfo.Local;

        Name = project.Name;
        ProjectPath = project.ProjectFilePath.Value;
        ProjectDirectory = project.ProjectDirectory;
        EngineAssociation = EmptyAsUnknown(project.EngineAssociation);
        EngineDisplayVersion = EmptyAsUnknown(project.EngineDisplayVersion);
        ProjectType = project.ProjectState
            == UProjectHub.Core.Models.ProjectState.Broken
            ? Unknown
            : project.ProjectType switch
            {
                UProjectHub.Core.Models.ProjectType.Cpp => "C++",
                UProjectHub.Core.Models.ProjectType.Blueprint => "Blueprint",
                _ => Unknown,
            };
        ProjectState = project.ProjectState switch
        {
            UProjectHub.Core.Models.ProjectState.Available =>
                Localize(localization, "String.StateAvailable", "Available"),
            UProjectHub.Core.Models.ProjectState.Missing =>
                Localize(localization, "String.StateMissing", "Missing"),
            UProjectHub.Core.Models.ProjectState.Broken =>
                Localize(localization, "String.StateBroken", "Broken"),
            _ => Unknown,
        };
        EngineState = project.EngineState switch
        {
            EngineResolutionState.Resolved =>
                Localize(localization, "String.EngineResolved", "Resolved"),
            EngineResolutionState.Missing =>
                Localize(localization, "String.EngineMissing", "Missing"),
            EngineResolutionState.Ambiguous =>
                Localize(localization, "String.EngineAmbiguous", "Ambiguous"),
            EngineResolutionState.Unknown =>
                Localize(localization, "String.EngineUnknown", "Unknown"),
            _ => Unknown,
        };
        IsFavorite = project.IsFavorite;
        FavoriteDisplay = Localize(
            localization,
            project.IsFavorite ? "String.Yes" : "String.No",
            project.IsFavorite ? "Yes" : "No");
        LastModified = FormatTimestamp(
            project.LastModified,
            displayTimeZone,
            Unknown);
        var never = Localize(localization, "String.Never", "Never");
        LastLaunched = project.LastLaunched is { } lastLaunched
            ? FormatTimestamp(lastLaunched, displayTimeZone, never)
            : never;
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

    public string FavoriteDisplay { get; }

    public string LastModified { get; }

    public string LastLaunched { get; }

    private static string EmptyAsUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Unknown : value;

    private static string FormatTimestamp(
        DateTimeOffset timestamp,
        TimeZoneInfo timeZone,
        string unknownText) =>
        timestamp == DateTimeOffset.MinValue
            ? unknownText
            : TimeZoneInfo.ConvertTime(timestamp, timeZone)
                .ToString(ExactTimestampFormat, CultureInfo.InvariantCulture);

    private static string Localize(
        LocalizationService? localization,
        string key,
        string fallback) =>
        localization?.GetString(key) is { } value && value != key
            ? value
            : fallback;
}
