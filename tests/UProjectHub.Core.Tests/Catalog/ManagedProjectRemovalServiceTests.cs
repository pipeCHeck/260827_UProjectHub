using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Filtering;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;
using UProjectHub.Core.Storage;

namespace UProjectHub.Core.Tests.Catalog;

[TestClass]
public sealed class ManagedProjectRemovalServiceTests
{
    [TestMethod]
    public async Task MissingProjectRemovalUpdatesManagerDataWithoutDeletingFilesAsync()
    {
        using var workspace = TemporaryWorkspace.Create();
        var targetPath = workspace.CreateProject("MissingGame");
        var retainedPath = workspace.CreateProject("RetainedGame");
        var target = CreateProject(targetPath, "MissingGame", ProjectState.Missing);
        var retained = CreateProject(retainedPath, "RetainedGame", ProjectState.Available);
        var catalog = CreateCatalog(target, retained);
        var repositories = CreateRepositories(workspace);
        var initialSettings = CreateSettings(targetPath, retainedPath, workspace.Path);
        await repositories.Settings.SaveAsync(initialSettings);
        await repositories.Cache.SaveAsync(CreateCache(target, retained));
        var service = new ManagedProjectRemovalService(
            catalog,
            repositories.Cache,
            repositories.Settings);

        var result = await service.RemoveMissingAsync(targetPath);

        Assert.AreEqual(ManagedProjectRemovalResult.Removed, result);
        CollectionAssert.AreEqual(
            new[] { retained },
            catalog.GetSnapshot().Projects.ToArray());

        var savedCache = await repositories.Cache.LoadAsync();
        Assert.HasCount(1, savedCache.Projects);
        Assert.AreEqual(retainedPath, savedCache.Projects[0].ProjectFilePath);

        var savedSettings = await repositories.Settings.LoadAsync();
        Assert.HasCount(1, savedSettings.ProjectUserStates);
        Assert.AreEqual(
            initialSettings.ProjectUserStates[1],
            savedSettings.ProjectUserStates[0]);
        AssertSettingsPreferencesEqual(initialSettings, savedSettings);

        Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(targetPath.Value)));
        Assert.IsTrue(File.Exists(targetPath.Value));
        Assert.IsTrue(File.Exists(Path.Combine(
            Path.GetDirectoryName(targetPath.Value)!,
            "Content",
            "Keep.uasset")));
    }

    [TestMethod]
    public async Task AvailableProjectRemovalIsRejectedWithoutChangingAnythingAsync()
    {
        using var workspace = TemporaryWorkspace.Create();
        var targetPath = workspace.CreateProject("AvailableGame");
        var target = CreateProject(targetPath, "AvailableGame", ProjectState.Available);
        var catalog = CreateCatalog(target);
        var repositories = CreateRepositories(workspace);
        var initialSettings = CreateSettings(targetPath, null, workspace.Path);
        await repositories.Settings.SaveAsync(initialSettings);
        await repositories.Cache.SaveAsync(CreateCache(target));
        var settingsBytes = await File.ReadAllBytesAsync(workspace.SettingsFilePath);
        var cacheBytes = await File.ReadAllBytesAsync(workspace.CacheFilePath);
        var service = new ManagedProjectRemovalService(
            catalog,
            repositories.Cache,
            repositories.Settings);

        var result = await service.RemoveMissingAsync(targetPath);

        Assert.AreEqual(ManagedProjectRemovalResult.NotMissing, result);
        CollectionAssert.AreEqual(
            new[] { target },
            catalog.GetSnapshot().Projects.ToArray());
        CollectionAssert.AreEqual(
            settingsBytes,
            await File.ReadAllBytesAsync(workspace.SettingsFilePath));
        CollectionAssert.AreEqual(
            cacheBytes,
            await File.ReadAllBytesAsync(workspace.CacheFilePath));
        Assert.IsTrue(File.Exists(targetPath.Value));
    }

    private static ProjectCatalog CreateCatalog(params UnrealProject[] projects)
    {
        var catalog = new ProjectCatalog();
        foreach (var project in projects)
        {
            catalog.Upsert(project);
        }

        return catalog;
    }

    private static Repositories CreateRepositories(TemporaryWorkspace workspace)
    {
        var writer = new AtomicJsonFileWriter();
        return new Repositories(
            new JsonProjectCacheRepository(workspace.CacheFilePath, writer),
            new JsonSettingsRepository(workspace.SettingsFilePath, writer));
    }

    private static AppSettings CreateSettings(
        ProjectPath targetPath,
        ProjectPath? retainedPath,
        string root)
    {
        var projectUserStates = new List<ProjectUserState>
        {
            new(
                targetPath,
                IsFavorite: true,
                LastLaunched: new DateTimeOffset(
                    2026,
                    8,
                    27,
                    1,
                    2,
                    3,
                    TimeSpan.Zero)),
        };
        if (retainedPath is not null)
        {
            projectUserStates.Add(new ProjectUserState(
                retainedPath,
                IsFavorite: false,
                LastLaunched: null));
        }

        return new AppSettings
        {
            ProjectSearchRoots = [Path.Combine(root, "Projects")],
            ManualEngineRoots = [Path.Combine(root, "UE_5.10")],
            ProjectUserStates = projectUserStates,
            ThemeMode = ThemeMode.Dark,
            RowDensity = RowDensity.Compact,
            ActiveSort = new ProjectSortDefinition(
                ProjectSortColumn.Name,
                SortDirection.Ascending),
            VisibleFilters = new VisibleFilterState(
                Engine: "5.10",
                ProjectType: ProjectType.Cpp,
                FavoritesOnly: true),
            ColumnLayout =
            [
                new ColumnLayoutState("Name", IsVisible: true, Width: 360),
                new ColumnLayoutState("LastLaunched", IsVisible: false, Width: null),
            ],
        };
    }

    private static ProjectCacheDocument CreateCache(params UnrealProject[] projects) =>
        new()
        {
            Projects = projects.Select(project => new ProjectCacheEntry(
                project.ProjectFilePath,
                project.Name,
                project.EngineAssociation,
                project.EngineDisplayVersion,
                project.ProjectType,
                project.LastModified,
                project.ProjectState,
                project.EngineState)).ToArray(),
        };

    private static UnrealProject CreateProject(
        ProjectPath projectPath,
        string name,
        ProjectState state) =>
        new(
            name,
            projectPath,
            EngineAssociation: "5.10",
            EngineDisplayVersion: "5.10.1",
            ProjectType.Cpp,
            new DateTimeOffset(2026, 8, 27, 1, 2, 3, TimeSpan.Zero),
            LastLaunched: null,
            IsFavorite: false,
            state,
            EngineResolutionState.Resolved);

    private static void AssertSettingsPreferencesEqual(
        AppSettings expected,
        AppSettings actual)
    {
        CollectionAssert.AreEqual(
            expected.ProjectSearchRoots.ToArray(),
            actual.ProjectSearchRoots.ToArray());
        CollectionAssert.AreEqual(
            expected.ManualEngineRoots.ToArray(),
            actual.ManualEngineRoots.ToArray());
        Assert.AreEqual(expected.ThemeMode, actual.ThemeMode);
        Assert.AreEqual(expected.RowDensity, actual.RowDensity);
        Assert.AreEqual(expected.ActiveSort, actual.ActiveSort);
        Assert.AreEqual(expected.VisibleFilters, actual.VisibleFilters);
        CollectionAssert.AreEqual(
            expected.ColumnLayout.ToArray(),
            actual.ColumnLayout.ToArray());
    }

    private sealed record Repositories(
        IProjectCacheRepository Cache,
        ISettingsRepository Settings);

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public string SettingsFilePath =>
            System.IO.Path.Combine(Path, "settings.json");

        public string CacheFilePath =>
            System.IO.Path.Combine(Path, "project-cache.json");

        public static TemporaryWorkspace Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "UProjectHub.Tests",
                "ManagedRemoval",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryWorkspace(path);
        }

        public ProjectPath CreateProject(string name)
        {
            var projectDirectory = System.IO.Path.Combine(Path, name);
            var contentDirectory = System.IO.Path.Combine(projectDirectory, "Content");
            Directory.CreateDirectory(contentDirectory);
            var projectFilePath = System.IO.Path.Combine(
                projectDirectory,
                $"{name}.uproject");
            File.WriteAllText(projectFilePath, "{ \"FileVersion\": 3 }");
            File.WriteAllText(
                System.IO.Path.Combine(contentDirectory, "Keep.uasset"),
                "must remain");
            return new ProjectPath(projectFilePath);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
