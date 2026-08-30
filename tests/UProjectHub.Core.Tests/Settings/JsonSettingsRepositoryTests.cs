using System.Text.Json;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;
using UProjectHub.Core.Storage;

namespace UProjectHub.Core.Tests.Settings;

[TestClass]
public sealed class JsonSettingsRepositoryTests
{
    [TestMethod]
    public async Task MissingSettingsFileReturnsValidDefaultsAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var repository = CreateRepository(temporaryDirectory.SettingsFilePath);

        var settings = await repository.LoadAsync();

        Assert.HasCount(0, settings.ProjectSearchRoots);
        Assert.HasCount(0, settings.ManualEngineRoots);
        Assert.HasCount(0, settings.ProjectUserStates);
        Assert.AreEqual(ThemeMode.System, settings.ThemeMode);
        Assert.AreEqual(RowDensity.Normal, settings.RowDensity);
        Assert.AreEqual(AppLanguage.English, settings.Language);
        Assert.AreEqual(new ProjectSortDefinition(), settings.ActiveSort);
        Assert.AreEqual(new VisibleFilterState(), settings.VisibleFilters);
        Assert.HasCount(0, settings.ColumnLayout);
        Assert.IsFalse(File.Exists(temporaryDirectory.SettingsFilePath));
    }

    [TestMethod]
    public async Task SavedUserOwnedStateRoundTripsAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var repository = CreateRepository(temporaryDirectory.SettingsFilePath);
        var expected = CreatePopulatedSettings(temporaryDirectory.Path, "RoundTrip");

        await repository.SaveAsync(expected);
        var actual = await repository.LoadAsync();

        AssertSettingsEqual(expected, actual);
    }

    [TestMethod]
    public async Task LegacyUserStateWithoutTagsOrNoteLoadsWithoutLosingExistingValuesAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var projectPath = Path.Combine(temporaryDirectory.Path, "Legacy.uproject");
        var json = $$"""
        {
          "projectSearchRoots": ["{{temporaryDirectory.Path.Replace("\\", "\\\\")}}"],
          "manualEngineRoots": [],
          "projectUserStates": [
            {
              "projectPath": "{{projectPath.Replace("\\", "\\\\")}}",
              "isFavorite": true,
              "lastLaunched": "2026-08-27T01:02:03+00:00"
            }
          ],
          "themeMode": "dark",
          "rowDensity": "compact",
          "language": "korean"
        }
        """;
        await File.WriteAllTextAsync(temporaryDirectory.SettingsFilePath, json);
        var repository = CreateRepository(temporaryDirectory.SettingsFilePath);

        var settings = await repository.LoadAsync();

        CollectionAssert.AreEqual(
            new[] { temporaryDirectory.Path },
            settings.ProjectSearchRoots.ToArray());
        var state = settings.ProjectUserStates.Single();
        Assert.IsTrue(state.IsFavorite);
        Assert.AreEqual(
            new DateTimeOffset(2026, 8, 27, 1, 2, 3, TimeSpan.Zero),
            state.LastLaunched);
        Assert.IsEmpty(state.Tags);
        Assert.AreEqual(string.Empty, state.Note);
        Assert.AreEqual(ThemeMode.Dark, settings.ThemeMode);
        Assert.AreEqual(RowDensity.Compact, settings.RowDensity);
        Assert.AreEqual(AppLanguage.Korean, settings.Language);
    }

    [TestMethod]
    public async Task TagsAreNormalizedAndNotesRoundTripAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var repository = CreateRepository(temporaryDirectory.SettingsFilePath);
        var settings = new AppSettings
        {
            ProjectUserStates =
            [
                new ProjectUserState(new ProjectPath(Path.Combine(
                    temporaryDirectory.Path,
                    "Metadata.uproject")))
                {
                    Tags = [" Client ", "client", "VR", "  "],
                    Note = "Keep this note exactly.\r\nSecond line.",
                },
            ],
        };

        await repository.SaveAsync(settings);
        var actual = await repository.LoadAsync();

        var state = actual.ProjectUserStates.Single();
        CollectionAssert.AreEqual(new[] { "Client", "VR" }, state.Tags.ToArray());
        Assert.AreEqual("Keep this note exactly.\r\nSecond line.", state.Note);
    }

    [TestMethod]
    public async Task CorruptPrimaryLoadsValidBackupAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var repository = CreateRepository(temporaryDirectory.SettingsFilePath);
        var expectedBackup = CreatePopulatedSettings(temporaryDirectory.Path, "Backup");
        var replacedPrimary = CreatePopulatedSettings(temporaryDirectory.Path, "Primary");

        await repository.SaveAsync(expectedBackup);
        await repository.SaveAsync(replacedPrimary);
        Assert.IsTrue(File.Exists(temporaryDirectory.BackupFilePath));
        await File.WriteAllTextAsync(temporaryDirectory.SettingsFilePath, "{ broken json");

        var actual = await repository.LoadAsync();

        AssertSettingsEqual(expectedBackup, actual);
    }

    [TestMethod]
    public async Task CorruptPrimaryAndBackupReturnValidDefaultsAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(temporaryDirectory.SettingsFilePath, "{ broken primary");
        await File.WriteAllTextAsync(temporaryDirectory.BackupFilePath, "{ broken backup");
        var repository = CreateRepository(temporaryDirectory.SettingsFilePath);

        var settings = await repository.LoadAsync();

        Assert.HasCount(0, settings.ProjectSearchRoots);
        Assert.HasCount(0, settings.ManualEngineRoots);
        Assert.HasCount(0, settings.ProjectUserStates);
        Assert.AreEqual(ThemeMode.System, settings.ThemeMode);
        Assert.AreEqual(RowDensity.Normal, settings.RowDensity);
        Assert.AreEqual(AppLanguage.English, settings.Language);
        Assert.AreEqual(new ProjectSortDefinition(), settings.ActiveSort);
        Assert.AreEqual(new VisibleFilterState(), settings.VisibleFilters);
        Assert.HasCount(0, settings.ColumnLayout);
    }

    [TestMethod]
    public async Task ReplacementFailurePreservesExistingPrimaryAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var repository = CreateRepository(temporaryDirectory.SettingsFilePath);
        var original = CreatePopulatedSettings(temporaryDirectory.Path, "Original");
        var replacement = CreatePopulatedSettings(temporaryDirectory.Path, "Replacement");
        await repository.SaveAsync(original);
        var originalBytes = await File.ReadAllBytesAsync(temporaryDirectory.SettingsFilePath);

        await using (var primaryLock = new FileStream(
            temporaryDirectory.SettingsFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
        {
            await Assert.ThrowsAsync<IOException>(() => repository.SaveAsync(replacement));
        }

        var preservedBytes = await File.ReadAllBytesAsync(temporaryDirectory.SettingsFilePath);
        CollectionAssert.AreEqual(originalBytes, preservedBytes);
        Assert.IsEmpty(Directory.EnumerateFiles(temporaryDirectory.Path, "*.tmp"));
        AssertSettingsEqual(original, await repository.LoadAsync());
    }

    [TestMethod]
    public async Task SavedFileIsCompleteReadableJsonWithExplicitContractAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var repository = CreateRepository(temporaryDirectory.SettingsFilePath);
        await repository.SaveAsync(
            CreatePopulatedSettings(temporaryDirectory.Path, "JsonContract"));

        await using var stream = File.OpenRead(temporaryDirectory.SettingsFilePath);
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        Assert.AreEqual(JsonValueKind.Array, root.GetProperty("projectSearchRoots").ValueKind);
        Assert.AreEqual(JsonValueKind.Array, root.GetProperty("manualEngineRoots").ValueKind);
        Assert.AreEqual("dark", root.GetProperty("themeMode").GetString());
        Assert.AreEqual("compact", root.GetProperty("rowDensity").GetString());
        Assert.AreEqual("korean", root.GetProperty("language").GetString());
        Assert.AreEqual(
            JsonValueKind.String,
            root.GetProperty("projectUserStates")[0].GetProperty("projectPath").ValueKind);
        Assert.AreEqual(
            JsonValueKind.Array,
            root.GetProperty("columnLayout").ValueKind);
    }

    private static ISettingsRepository CreateRepository(string settingsFilePath) =>
        new JsonSettingsRepository(settingsFilePath, new AtomicJsonFileWriter());

    private static AppSettings CreatePopulatedSettings(string root, string name)
    {
        var projectPath = new ProjectPath(Path.Combine(
            root,
            "Projects",
            "Temporary",
            "..",
            $"{name}.uproject"));

        return new AppSettings
        {
            ProjectSearchRoots =
            [
                Path.Combine(root, "Game Academy"),
                Path.Combine(root, "Other Projects"),
            ],
            ManualEngineRoots =
            [
                Path.Combine(root, "UnrealEngine-5.10"),
            ],
            ProjectUserStates =
            [
                new ProjectUserState(
                    projectPath,
                    IsFavorite: true,
                    LastLaunched: new DateTimeOffset(
                        2026,
                        8,
                        27,
                        1,
                        2,
                        3,
                        TimeSpan.Zero))
                {
                    Tags = ["Client", "VR"],
                    Note = "Review the lighting pass.",
                },
            ],
            ThemeMode = ThemeMode.Dark,
            RowDensity = RowDensity.Compact,
            Language = AppLanguage.Korean,
            ActiveSort = new ProjectSortDefinition(
                ProjectSortColumn.EngineVersion,
                SortDirection.Ascending),
            VisibleFilters = new VisibleFilterState(
                Engine: "5.10",
                ProjectType: ProjectType.Cpp,
                FavoritesOnly: true),
            ColumnLayout =
            [
                new ColumnLayoutState("Name", IsVisible: true, Width: 320),
                new ColumnLayoutState("LastLaunched", IsVisible: false, Width: null),
            ],
        };
    }

    private static void AssertSettingsEqual(AppSettings expected, AppSettings actual)
    {
        CollectionAssert.AreEqual(
            expected.ProjectSearchRoots.ToArray(),
            actual.ProjectSearchRoots.ToArray());
        CollectionAssert.AreEqual(
            expected.ManualEngineRoots.ToArray(),
            actual.ManualEngineRoots.ToArray());
        Assert.HasCount(expected.ProjectUserStates.Count, actual.ProjectUserStates);
        for (var index = 0; index < expected.ProjectUserStates.Count; index++)
        {
            var expectedState = expected.ProjectUserStates[index];
            var actualState = actual.ProjectUserStates[index];
            Assert.AreEqual(expectedState.ProjectPath, actualState.ProjectPath);
            Assert.AreEqual(expectedState.IsFavorite, actualState.IsFavorite);
            Assert.AreEqual(expectedState.LastLaunched, actualState.LastLaunched);
            CollectionAssert.AreEqual(
                expectedState.Tags.ToArray(),
                actualState.Tags.ToArray());
            Assert.AreEqual(expectedState.Note, actualState.Note);
        }
        Assert.AreEqual(expected.ThemeMode, actual.ThemeMode);
        Assert.AreEqual(expected.RowDensity, actual.RowDensity);
        Assert.AreEqual(expected.Language, actual.Language);
        Assert.AreEqual(expected.ActiveSort, actual.ActiveSort);
        Assert.AreEqual(expected.VisibleFilters, actual.VisibleFilters);
        CollectionAssert.AreEqual(
            expected.ColumnLayout.ToArray(),
            actual.ColumnLayout.ToArray());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public string SettingsFilePath =>
            System.IO.Path.Combine(Path, "settings.json");

        public string BackupFilePath => $"{SettingsFilePath}.bak";

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "UProjectHub.Tests",
                "Settings",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
