using System.Reflection;
using System.Windows;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.Core.Settings;

namespace UProjectHub.Core.Tests.App;

[TestClass]
[DoNotParallelize]
public sealed class LocalizationServiceTests
{
    private static readonly string[] RequiredKeys =
    [
        "String.AppTitle",
        "String.ProjectCountFormat",
        "String.ShowingCountFormat",
        "String.SearchPlaceholder",
        "String.EngineVersion",
        "String.Type",
        "String.LastModified",
        "String.LastLaunched",
        "String.FavoritesOnly",
        "String.All",
        "String.Reset",
        "String.NoProjects",
        "String.NoProjectsHint",
        "String.NoResults",
        "String.ResetSearchFilters",
        "String.Settings",
        "String.Refresh",
        "String.ProjectInformation",
        "String.ProjectDetails",
        "String.Overview",
        "String.Diagnostics",
        "String.DiagnosticsDescription",
        "String.DiagnosticsEmpty",
        "String.DiagnosticSeverityInfo",
        "String.DiagnosticSeverityWarning",
        "String.DiagnosticSeverityError",
        "String.DiagnosticEngineMissing",
        "String.DiagnosticEngineAmbiguous",
        "String.DiagnosticEngineUnknown",
        "String.DiagnosticPartialFailure",
        "String.StateMissing",
        "String.StateBroken",
        "String.Apply",
        "String.Close",
        "String.Language",
        "String.StatusReady",
        "String.NewProject",
        "String.SelectVersion",
        "String.OpenProject",
        "String.OpenVisualStudio",
        "String.OpenFolder",
        "String.OpenVisualStudioProjectUnavailable",
        "String.OpenVisualStudioCppOnly",
        "String.OpenVisualStudioSolutionMissing",
        "String.OpenVisualStudioSolutionMultiple",
        "String.OpenVisualStudioSolutionInaccessible",
        "String.OpenVisualStudioSolutionUnavailable",
        "String.CopyPath",
        "String.AddFavorite",
        "String.RemoveFavorite",
        "String.RemoveFromList",
        "String.Projects",
        "String.SearchLocations",
        "String.AddSearchRoot",
        "String.RescanProjects",
        "String.ManualEngines",
        "String.AddEngineRoot",
        "String.Appearance",
        "String.Theme",
        "String.RowDensity",
        "String.ProjectFile",
        "String.Directory",
        "String.EngineAssociation",
        "String.ProjectType",
        "String.ProjectState",
        "String.EngineState",
        "String.Favorite",
        "String.Never",
        "String.Yes",
        "String.No",
        "String.StatusRefreshing",
        "String.StatusRescanning",
    ];

    [TestMethod]
    public void NewSettingsDefaultToEnglish()
    {
        Assert.AreEqual(AppLanguage.English, new AppSettings().Language);
    }

    [STATestMethod]
    public void ApplyLanguageSwitchesEnglishKoreanEnglishWithoutAccumulatingDictionaries()
    {
        var resources = new ResourceDictionary();
        var service = CreateService(resources);

        service.ApplyLanguage(AppLanguage.English);
        Assert.AreEqual("Engine Version", service.GetString("String.EngineVersion"));

        service.ApplyLanguage(AppLanguage.Korean);
        Assert.AreEqual("엔진 버전", service.GetString("String.EngineVersion"));

        service.ApplyLanguage(AppLanguage.English);
        Assert.AreEqual("Engine Version", service.GetString("String.EngineVersion"));
        Assert.AreEqual(AppLanguage.English, service.CurrentLanguage);
        Assert.AreEqual(1, CountLocalizationDictionaries(resources));
    }

    [STATestMethod]
    public void EveryRequiredUiStringExistsInBothLanguages()
    {
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            var resources = new ResourceDictionary();
            var service = CreateService(resources);
            service.ApplyLanguage(language);

            foreach (var key in RequiredKeys)
            {
                Assert.IsFalse(
                    string.IsNullOrWhiteSpace(service.GetString(key)),
                    $"{language} is missing {key}.");
                Assert.AreNotEqual(
                    key,
                    service.GetString(key),
                    $"{language} is missing {key}.");
            }
        }
    }

    [TestMethod]
    public void DisplayVersionComesFromTheApplicationInformationalVersion()
    {
        var informationalVersion = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        Assert.AreEqual("0.4.4 r", informationalVersion);
        Assert.AreEqual("v0.4.4 r", AppVersion.Display);
    }

    private static int CountLocalizationDictionaries(ResourceDictionary resources) =>
        resources.MergedDictionaries.Count(dictionary =>
            dictionary.Contains("String.AppTitle"));

    private static LocalizationService CreateService(ResourceDictionary resources) =>
        new(
            resources,
            source => LoadDictionary(source.OriginalString.EndsWith(
                "Strings.ko.xaml",
                StringComparison.OrdinalIgnoreCase)
                    ? "Localization/Strings.ko.xaml"
                    : "Localization/Strings.en.xaml"));

    private static ResourceDictionary LoadDictionary(string relativePath) =>
        (ResourceDictionary)Application.LoadComponent(
            new Uri(
                $"/UProjectHub.App;component/{relativePath}",
                UriKind.Relative));
}
