using System.Windows;
using UProjectHub.App.Behaviors;
using UProjectHub.App.Services;
using UProjectHub.Core.Settings;
using AppThemeMode = UProjectHub.Core.Settings.ThemeMode;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ThemeServiceTests
{
    [TestMethod]
    [DataRow(1000d, true, true, true)]
    [DataRow(999d, true, false, true)]
    [DataRow(820d, true, false, true)]
    [DataRow(819d, false, false, false)]
    public void ResponsiveColumns_UseActualDataGridWidthThresholds(
        double actualWidth,
        bool showType,
        bool showLastLaunched,
        bool showGit)
    {
        var layout = ResponsiveColumnsBehavior.GetLayout(actualWidth);

        Assert.AreEqual(showType, layout.ShowType);
        Assert.AreEqual(showLastLaunched, layout.ShowLastLaunched);
        Assert.AreEqual(showGit, layout.ShowGit);
    }

    [TestMethod]
    public void ApplyTheme_ReplacesTheActiveThemeWithoutAccumulatingDictionaries()
    {
        var resources = new ResourceDictionary();
        var service = CreateService(resources, () => AppThemeMode.Light);

        service.ApplyTheme(AppThemeMode.Light);
        service.ApplyTheme(AppThemeMode.Dark);
        service.ApplyTheme(AppThemeMode.Light);

        Assert.AreEqual(AppThemeMode.Light, service.EffectiveTheme);
        Assert.AreEqual(1, CountThemeDictionaries(resources));
        Assert.IsTrue(HasDictionary(resources, "Light.xaml"));
        Assert.IsFalse(HasDictionary(resources, "Dark.xaml"));
    }

    [TestMethod]
    public void ApplyTheme_SystemUsesInjectedWindowsPreference()
    {
        var lightResources = new ResourceDictionary();
        var darkResources = new ResourceDictionary();

        var lightService = CreateService(lightResources, () => AppThemeMode.Light);
        var darkService = CreateService(darkResources, () => AppThemeMode.Dark);
        lightService.ApplyTheme(AppThemeMode.System);
        darkService.ApplyTheme(AppThemeMode.System);

        Assert.AreEqual(AppThemeMode.Light, lightService.EffectiveTheme);
        Assert.AreEqual(AppThemeMode.Dark, darkService.EffectiveTheme);
        Assert.IsTrue(HasDictionary(lightResources, "Light.xaml"));
        Assert.IsTrue(HasDictionary(darkResources, "Dark.xaml"));
    }

    [TestMethod]
    public void ApplyTheme_WhenSystemPreferenceReadFails_FallsBackToLight()
    {
        var resources = new ResourceDictionary();
        var service = CreateService(
            resources,
            () => throw new IOException("Registry unavailable."));

        service.ApplyTheme(AppThemeMode.System);

        Assert.AreEqual(AppThemeMode.Light, service.EffectiveTheme);
        Assert.IsTrue(HasDictionary(resources, "Light.xaml"));
        Assert.IsFalse(HasDictionary(resources, "Dark.xaml"));
    }

    [TestMethod]
    public void ApplyDensity_ReplacesTheActiveDensityWithoutAccumulatingDictionaries()
    {
        var resources = new ResourceDictionary();
        var service = CreateService(resources, () => AppThemeMode.Light);

        service.ApplyDensity(RowDensity.Normal);
        service.ApplyDensity(RowDensity.Compact);
        service.ApplyDensity(RowDensity.Compact);

        Assert.AreEqual(RowDensity.Compact, service.ActiveDensity);
        Assert.AreEqual(1, CountDensityDictionaries(resources));
        Assert.IsTrue(HasDictionary(resources, "CompactDensity.xaml"));
        Assert.IsFalse(HasDictionary(resources, "NormalDensity.xaml"));
    }

    [TestMethod]
    public void ThemeAndDensitySwitching_PreserveEachOther()
    {
        var resources = new ResourceDictionary();
        var service = CreateService(resources, () => AppThemeMode.Light);

        service.ApplyTheme(AppThemeMode.Dark);
        service.ApplyDensity(RowDensity.Compact);
        service.ApplyTheme(AppThemeMode.Light);

        Assert.AreEqual(AppThemeMode.Light, service.EffectiveTheme);
        Assert.AreEqual(RowDensity.Compact, service.ActiveDensity);
        Assert.AreEqual(1, CountThemeDictionaries(resources));
        Assert.AreEqual(1, CountDensityDictionaries(resources));

        service.ApplyDensity(RowDensity.Normal);

        Assert.AreEqual(AppThemeMode.Light, service.EffectiveTheme);
        Assert.AreEqual(RowDensity.Normal, service.ActiveDensity);
        Assert.AreEqual(1, CountThemeDictionaries(resources));
        Assert.AreEqual(1, CountDensityDictionaries(resources));
    }

    [TestMethod]
    public void ApplySettings_AppliesAppearanceWithoutARepositoryDependency()
    {
        var resources = new ResourceDictionary();
        var service = CreateService(resources, () => AppThemeMode.Light);
        var settings = new AppSettings
        {
            ThemeMode = AppThemeMode.Dark,
            RowDensity = RowDensity.Compact,
        };

        service.ApplySettings(settings);

        Assert.AreEqual(AppThemeMode.Dark, service.EffectiveTheme);
        Assert.AreEqual(RowDensity.Compact, service.ActiveDensity);
        Assert.IsTrue(HasDictionary(resources, "Dark.xaml"));
        Assert.IsTrue(HasDictionary(resources, "CompactDensity.xaml"));
    }

    private static int CountThemeDictionaries(ResourceDictionary resources)
    {
        return resources.MergedDictionaries.Count(dictionary =>
            IsSource(dictionary, "Light.xaml") || IsSource(dictionary, "Dark.xaml"));
    }

    private static int CountDensityDictionaries(ResourceDictionary resources)
    {
        return resources.MergedDictionaries.Count(dictionary =>
            IsSource(dictionary, "NormalDensity.xaml") || IsSource(dictionary, "CompactDensity.xaml"));
    }

    private static bool HasDictionary(ResourceDictionary resources, string fileName)
    {
        return resources.MergedDictionaries.Any(dictionary => IsSource(dictionary, fileName));
    }

    private static bool IsSource(ResourceDictionary dictionary, string fileName)
    {
        var source = dictionary.Source?.OriginalString
            ?? dictionary["Test.Source"] as string;
        return source?.EndsWith(
            $"/Themes/{fileName}",
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private static ThemeService CreateService(
        ResourceDictionary resources,
        Func<AppThemeMode> systemThemeResolver)
    {
        return new ThemeService(
            resources,
            systemThemeResolver,
            source => new ResourceDictionary
            {
                ["Test.Source"] = source.OriginalString,
            });
    }
}
