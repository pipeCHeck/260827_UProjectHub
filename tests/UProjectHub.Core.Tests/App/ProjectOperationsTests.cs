using System.Windows;
using UProjectHub.App.Services;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Discovery;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;
using UProjectHub.Windows.Engines.Manual;
using AppThemeMode = UProjectHub.Core.Settings.ThemeMode;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ProjectOperationsTests
{
    [TestMethod]
    public async Task SearchRoots_ArePersistedWithoutScanningTheirContents()
    {
        using var temp = new TemporaryDirectory();
        var emptyRoot = temp.CreateDirectory("Empty");
        var noProjectRoot = temp.CreateDirectory("NoProject");
        File.WriteAllText(Path.Combine(noProjectRoot, "notes.txt"), "keep");
        var nestedRoot = temp.CreateDirectory("Nested");
        var nestedProject = Directory.CreateDirectory(Path.Combine(nestedRoot, "Game")).FullName;
        File.WriteAllText(Path.Combine(nestedProject, "Game.uproject"), "{}");
        var fixture = CreateFixture(new AppSettings());

        await fixture.Service.AddProjectSearchRootAsync(emptyRoot);
        await fixture.Service.AddProjectSearchRootAsync(noProjectRoot);
        await fixture.Service.AddProjectSearchRootAsync(nestedRoot);

        Assert.HasCount(3, fixture.Settings.Current.ProjectSearchRoots);
        Assert.AreEqual(0, fixture.RescanCalls);
        Assert.IsTrue(File.Exists(Path.Combine(noProjectRoot, "notes.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(nestedProject, "Game.uproject")));
    }

    [TestMethod]
    public async Task SearchRootDuplicate_UsesCanonicalWindowsIdentityWithoutSavingAgain()
    {
        using var temp = new TemporaryDirectory();
        var root = temp.CreateDirectory("Unreal");
        var fixture = CreateFixture(new AppSettings());

        var first = await fixture.Service.AddProjectSearchRootAsync(root);
        var duplicate = await fixture.Service.AddProjectSearchRootAsync(
            root.ToUpperInvariant() + Path.DirectorySeparatorChar);

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(first.Changed);
        Assert.IsTrue(duplicate.IsSuccess);
        Assert.IsFalse(duplicate.Changed);
        Assert.HasCount(1, fixture.Settings.Current.ProjectSearchRoots);
        Assert.HasCount(1, fixture.Settings.SaveCalls);
    }

    [TestMethod]
    public async Task RemoveSearchRoot_OnlyChangesSettingsAndPreservesOtherFields()
    {
        using var temp = new TemporaryDirectory();
        var root = temp.CreateDirectory("KeepOnDisk");
        var marker = Path.Combine(root, "marker.txt");
        File.WriteAllText(marker, "keep");
        var initial = CreatePopulatedSettings() with
        {
            ProjectSearchRoots = [root, @"C:\OtherRoot"],
        };
        var fixture = CreateFixture(initial);

        var result = await fixture.Service.RemoveProjectSearchRootAsync(root);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Changed);
        CollectionAssert.AreEqual(
            new[] { @"C:\OtherRoot" },
            fixture.Settings.Current.ProjectSearchRoots.ToArray());
        Assert.IsTrue(File.Exists(marker));
        AssertUnrelatedSettingsPreserved(
            initial,
            fixture.Settings.Current,
            compareProjectRoots: false);
    }

    [TestMethod]
    public async Task ManualEngine_UsesValidatorAndOnlyPersistsUsableCanonicalRoots()
    {
        using var temp = new TemporaryDirectory();
        var validRoot = CreateManualEngine(temp, "Valid", includeEditor: true);
        var invalidRoot = CreateManualEngine(temp, "MissingEditor", includeEditor: false);
        var fixture = CreateFixture(new AppSettings());

        var valid = await fixture.Service.AddManualEngineRootAsync(validRoot);
        var duplicate = await fixture.Service.AddManualEngineRootAsync(
            validRoot.ToUpperInvariant() + Path.DirectorySeparatorChar);
        var invalid = await fixture.Service.AddManualEngineRootAsync(invalidRoot);

        Assert.IsTrue(valid.IsSuccess);
        Assert.IsTrue(valid.Changed);
        Assert.IsTrue(duplicate.IsSuccess);
        Assert.IsFalse(duplicate.Changed);
        Assert.IsFalse(invalid.IsSuccess);
        StringAssert.Contains(invalid.Message!, "Unreal Editor executable");
        Assert.HasCount(1, fixture.Settings.Current.ManualEngineRoots);
        Assert.HasCount(1, fixture.Settings.SaveCalls);
    }

    [TestMethod]
    public async Task RemoveManualEngine_OnlyRemovesItsSettingsEntry()
    {
        using var temp = new TemporaryDirectory();
        var root = CreateManualEngine(temp, "Manual", includeEditor: true);
        var editor = Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
        var initial = CreatePopulatedSettings() with { ManualEngineRoots = [root] };
        var fixture = CreateFixture(initial);

        var result = await fixture.Service.RemoveManualEngineRootAsync(root);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsEmpty(fixture.Settings.Current.ManualEngineRoots);
        Assert.IsTrue(File.Exists(editor));
        AssertUnrelatedSettingsPreserved(
            initial,
            fixture.Settings.Current,
            compareManualRoots: false);
    }

    [TestMethod]
    public async Task SaveAppearance_AppliesThemeOnlyAfterPersistenceSucceeds()
    {
        var initial = CreatePopulatedSettings();
        var fixture = CreateFixture(initial);

        var saved = await fixture.Service.SaveAppearanceAsync(
            AppThemeMode.Dark,
            RowDensity.Compact,
            AppLanguage.Korean);

        Assert.IsTrue(saved.IsSuccess);
        Assert.AreEqual(AppThemeMode.Dark, fixture.Settings.Current.ThemeMode);
        Assert.AreEqual(RowDensity.Compact, fixture.Settings.Current.RowDensity);
        Assert.AreEqual(AppLanguage.Korean, fixture.Settings.Current.Language);
        Assert.AreEqual(AppThemeMode.Dark, fixture.Theme.EffectiveTheme);
        Assert.AreEqual(RowDensity.Compact, fixture.Theme.ActiveDensity);
        Assert.AreEqual(AppLanguage.Korean, fixture.Localization.CurrentLanguage);
        AssertUnrelatedSettingsPreserved(
            initial,
            fixture.Settings.Current,
            compareAppearance: false);

        fixture.Settings.SaveException = new IOException("disk unavailable");
        var failed = await fixture.Service.SaveAppearanceAsync(
            AppThemeMode.Light,
            RowDensity.Normal,
            AppLanguage.English);

        Assert.IsFalse(failed.IsSuccess);
        Assert.AreEqual(AppThemeMode.Dark, fixture.Theme.EffectiveTheme);
        Assert.AreEqual(RowDensity.Compact, fixture.Theme.ActiveDensity);
        Assert.AreEqual(AppLanguage.Korean, fixture.Localization.CurrentLanguage);
    }

    [TestMethod]
    public async Task SaveViewState_PreservesColumnLayoutAndEveryUnrelatedSetting()
    {
        var initial = CreatePopulatedSettings();
        var fixture = CreateFixture(initial);
        var sort = new ProjectSortDefinition(
            ProjectSortColumn.EngineVersion,
            SortDirection.Ascending);
        var filters = new VisibleFilterState(
            "5.10",
            ProjectType.Cpp,
            true,
            "Team Project");

        var result = await fixture.Service.SaveViewStateAsync(
            sort,
            filters,
            columnLayout: null);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(sort, fixture.Settings.Current.ActiveSort);
        Assert.AreEqual(filters, fixture.Settings.Current.VisibleFilters);
        CollectionAssert.AreEqual(
            initial.ColumnLayout.ToArray(),
            fixture.Settings.Current.ColumnLayout.ToArray());
        Assert.AreEqual(initial.ProjectUserStates[0], fixture.Settings.Current.ProjectUserStates[0]);
        CollectionAssert.AreEqual(
            initial.ProjectSearchRoots.ToArray(),
            fixture.Settings.Current.ProjectSearchRoots.ToArray());
        CollectionAssert.AreEqual(
            initial.ManualEngineRoots.ToArray(),
            fixture.Settings.Current.ManualEngineRoots.ToArray());
    }

    [TestMethod]
    public async Task Rescan_UsesCurrentPersistedRootsOnlyWhenExplicitlyInvoked()
    {
        var initial = CreatePopulatedSettings() with
        {
            ProjectSearchRoots = [@"C:\Projects", @"D:\Study"],
        };
        var fixture = CreateFixture(initial);

        var result = await fixture.Service.RescanAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, fixture.RescanCalls);
        CollectionAssert.AreEqual(
            initial.ProjectSearchRoots.ToArray(),
            fixture.LastRescanRoots!.ToArray());
        Assert.AreSame(initial, fixture.LastRescanSettings);
    }

    [TestMethod]
    public async Task PersistenceFailure_DoesNotReportAChangedSettingsSnapshot()
    {
        using var temp = new TemporaryDirectory();
        var fixture = CreateFixture(CreatePopulatedSettings());
        fixture.Settings.SaveException = new IOException("disk unavailable");

        var result = await fixture.Service.AddProjectSearchRootAsync(
            temp.CreateDirectory("Root"));

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(result.Changed);
        Assert.IsNull(result.Settings);
        Assert.AreEqual(0, fixture.RescanCalls);
    }

    private static Fixture CreateFixture(AppSettings settings)
    {
        var repository = new FakeSettingsRepository(settings);
        var resources = new ResourceDictionary();
        var theme = new ThemeService(
            resources,
            () => AppThemeMode.Light,
            source => new ResourceDictionary { ["Test.Source"] = source.OriginalString });
        var localization = new LocalizationService(
            resources,
            source => new ResourceDictionary
            {
                ["Test.Source"] = source.OriginalString,
            });
        var catalog = new ProjectCatalog();
        var fixture = new Fixture(repository, theme, localization);
        fixture.Service = new ProjectOperations(
            new SettingsMutationService(repository),
            new ManualEngineValidator(),
            theme,
            localization,
            catalog,
            fixture.RescanAsync);
        return fixture;
    }

    private static AppSettings CreatePopulatedSettings() => new()
    {
        ProjectSearchRoots = [@"C:\Existing"],
        ManualEngineRoots = [@"C:\Engine"],
        ProjectUserStates =
        [
            new ProjectUserState(
                new ProjectPath(@"C:\Game\Game.uproject"),
                true,
                new DateTimeOffset(2026, 8, 28, 1, 2, 3, TimeSpan.Zero))
            {
                Tags = ["Client", "Prototype"],
                Note = "Preserve across unrelated settings writes.",
            },
        ],
        ThemeMode = AppThemeMode.System,
        RowDensity = RowDensity.Normal,
        Language = AppLanguage.English,
        ActiveSort = new ProjectSortDefinition(
            ProjectSortColumn.LastModified,
            SortDirection.Descending),
        VisibleFilters = new VisibleFilterState("5.8", ProjectType.Blueprint, true),
        ColumnLayout = [new ColumnLayoutState("ProjectType", true, 123)],
    };

    private static void AssertUnrelatedSettingsPreserved(
        AppSettings expected,
        AppSettings actual,
        bool compareProjectRoots = true,
        bool compareManualRoots = true,
        bool compareAppearance = true)
    {
        CollectionAssert.AreEqual(expected.ProjectUserStates.ToArray(), actual.ProjectUserStates.ToArray());
        if (compareProjectRoots)
        {
            CollectionAssert.AreEqual(expected.ProjectSearchRoots.ToArray(), actual.ProjectSearchRoots.ToArray());
        }

        if (compareManualRoots)
        {
            CollectionAssert.AreEqual(expected.ManualEngineRoots.ToArray(), actual.ManualEngineRoots.ToArray());
        }

        if (compareAppearance)
        {
            Assert.AreEqual(expected.ThemeMode, actual.ThemeMode);
            Assert.AreEqual(expected.RowDensity, actual.RowDensity);
            Assert.AreEqual(expected.Language, actual.Language);
        }

        Assert.AreEqual(expected.ActiveSort, actual.ActiveSort);
        Assert.AreEqual(expected.VisibleFilters, actual.VisibleFilters);
        CollectionAssert.AreEqual(expected.ColumnLayout.ToArray(), actual.ColumnLayout.ToArray());
    }

    private static string CreateManualEngine(
        TemporaryDirectory temp,
        string name,
        bool includeEditor)
    {
        var root = temp.CreateDirectory(name);
        var buildDirectory = Directory.CreateDirectory(
            Path.Combine(root, "Engine", "Build")).FullName;
        File.WriteAllText(
            Path.Combine(buildDirectory, "Build.version"),
            "{\"MajorVersion\":5,\"MinorVersion\":8,\"PatchVersion\":2}");
        if (includeEditor)
        {
            var editorDirectory = Directory.CreateDirectory(
                Path.Combine(root, "Engine", "Binaries", "Win64")).FullName;
            File.WriteAllText(Path.Combine(editorDirectory, "UnrealEditor.exe"), string.Empty);
        }

        return root;
    }

    private sealed class Fixture(
        FakeSettingsRepository settings,
        ThemeService theme,
        LocalizationService localization)
    {
        public ProjectOperations Service { get; set; } = null!;

        public FakeSettingsRepository Settings { get; } = settings;

        public ThemeService Theme { get; } = theme;

        public LocalizationService Localization { get; } = localization;

        public int RescanCalls { get; private set; }

        public IReadOnlyList<string>? LastRescanRoots { get; private set; }

        public AppSettings? LastRescanSettings { get; private set; }

        public Task<ProjectRefreshResult> RescanAsync(
            IReadOnlyList<string> roots,
            AppSettings appSettings,
            CancellationToken cancellationToken)
        {
            RescanCalls++;
            LastRescanRoots = roots;
            LastRescanSettings = appSettings;
            return Task.FromResult<ProjectRefreshResult>(new([], []));
        }
    }

    private sealed class FakeSettingsRepository(AppSettings settings) : ISettingsRepository
    {
        public AppSettings Current { get; private set; } = settings;

        public Exception? SaveException { get; set; }

        public List<AppSettings> SaveCalls { get; } = [];

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            if (SaveException is not null)
            {
                return Task.FromException(SaveException);
            }

            SaveCalls.Add(settings);
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"UProjectHub-Task26-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateDirectory(string name) =>
            Directory.CreateDirectory(System.IO.Path.Combine(Path, name)).FullName;

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
