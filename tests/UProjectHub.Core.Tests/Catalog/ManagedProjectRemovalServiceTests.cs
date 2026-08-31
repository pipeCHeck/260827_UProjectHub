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
            repositories.Settings,
            new ProjectCatalogOperationGate());

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
            repositories.Settings,
            new ProjectCatalogOperationGate());

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

    [TestMethod]
    public async Task CacheSaveFailureDoesNotRemoveTheProjectFromTheCatalogAsync()
    {
        var targetPath = new ProjectPath(@"D:\Projects\Missing\Missing.uproject");
        var target = CreateProject(targetPath, "Missing", ProjectState.Missing);
        var catalog = CreateCatalog(target);
        var settings = new RecordingSettingsRepository(CreateSettings(
            targetPath,
            retainedPath: null,
            root: @"D:\Projects"));
        var cache = new FailingProjectCacheRepository(CreateCache(target));
        var service = new ManagedProjectRemovalService(
            catalog,
            cache,
            new SettingsMutationService(settings),
            new ProjectCatalogOperationGate());

        await Assert.ThrowsExactlyAsync<IOException>(
            () => service.RemoveMissingAsync(targetPath));

        Assert.AreEqual(target, catalog.GetSnapshot().Projects.Single());
        Assert.AreEqual(0, settings.SaveCount);
        Assert.HasCount(1, settings.Current.ProjectUserStates);
    }

    [TestMethod]
    public async Task SettingsSaveFailureRestoresTheCacheAndKeepsTheCatalogAsync()
    {
        var targetPath = new ProjectPath(@"D:\Projects\Missing\Missing.uproject");
        var target = CreateProject(targetPath, "Missing", ProjectState.Missing);
        var catalog = CreateCatalog(target);
        var initialSettings = CreateSettings(
            targetPath,
            retainedPath: null,
            root: @"D:\Projects");
        var settings = new RecordingSettingsRepository(initialSettings)
        {
            SaveException = new IOException("settings unavailable"),
        };
        var cache = new RecordingProjectCacheRepository(CreateCache(target));
        var service = new ManagedProjectRemovalService(
            catalog,
            cache,
            new SettingsMutationService(settings),
            new ProjectCatalogOperationGate());

        await Assert.ThrowsExactlyAsync<IOException>(
            () => service.RemoveMissingAsync(targetPath));

        Assert.AreEqual(target, catalog.GetSnapshot().Projects.Single());
        Assert.AreEqual(
            targetPath,
            cache.Current.Projects.Single().ProjectFilePath);
        Assert.AreEqual(initialSettings, settings.Current);
    }

    [TestMethod]
    public async Task RefreshThatMakesProjectAvailableDuringRemovalIsNotRemovedAsync()
    {
        var targetPath = new ProjectPath(@"D:\Projects\Recovered\Recovered.uproject");
        var missing = CreateProject(targetPath, "Recovered", ProjectState.Missing);
        var available = missing with
        {
            ProjectState = ProjectState.Available,
            LastModified = missing.LastModified.AddMinutes(5),
        };
        var catalog = CreateCatalog(missing);
        var initialSettings = CreateSettings(
            targetPath,
            retainedPath: null,
            root: @"D:\Projects");
        var settings = new RecordingSettingsRepository(initialSettings);
        var cache = new RecordingProjectCacheRepository(CreateCache(missing));
        var operationGate = new ProjectCatalogOperationGate();
        var service = new ManagedProjectRemovalService(
            catalog,
            cache,
            new SettingsMutationService(settings),
            operationGate);

        await operationGate.WaitAsync();
        var removal = service.RemoveMissingAsync(targetPath);

        catalog.Upsert(available);
        cache.Replace(CreateCache(available));
        operationGate.Release();

        var result = await removal;

        Assert.AreEqual(ManagedProjectRemovalResult.NotMissing, result);
        Assert.AreEqual(available, catalog.GetSnapshot().Projects.Single());
        Assert.AreEqual(
            available.LastModified,
            cache.Current.Projects.Single().LastModified);
        Assert.AreEqual(
            initialSettings.ProjectUserStates.Single(),
            settings.Current.ProjectUserStates.Single());
        Assert.AreEqual(0, settings.SaveCount);
    }

    [TestMethod]
    public async Task SettingsFailureDoesNotRollbackOverNewerRefreshCacheAsync()
    {
        var targetPath = new ProjectPath(@"D:\Projects\Recovered\Recovered.uproject");
        var missing = CreateProject(targetPath, "Recovered", ProjectState.Missing);
        var refreshed = missing with
        {
            LastModified = missing.LastModified.AddMinutes(5),
        };
        var catalog = CreateCatalog(missing);
        var initialSettings = CreateSettings(
            targetPath,
            retainedPath: null,
            root: @"D:\Projects");
        var settings = new RecordingSettingsRepository(initialSettings)
        {
            SaveException = new IOException("settings unavailable"),
        };
        var cache = new RecordingProjectCacheRepository(CreateCache(missing));
        var operationGate = new ProjectCatalogOperationGate();
        var service = new ManagedProjectRemovalService(
            catalog,
            cache,
            new SettingsMutationService(settings),
            operationGate);

        await operationGate.WaitAsync();
        var removal = service.RemoveMissingAsync(targetPath);

        cache.Replace(CreateCache(refreshed));
        operationGate.Release();

        await Assert.ThrowsExactlyAsync<IOException>(() => removal);

        Assert.AreEqual(
            refreshed.LastModified,
            cache.Current.Projects.Single().LastModified);
        Assert.AreEqual(missing, catalog.GetSnapshot().Projects.Single());
        Assert.AreEqual(initialSettings, settings.Current);
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
                LastLaunched: null)
            {
                Tags = ["Retained"],
                Note = "Keep this metadata.",
            });
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

    private sealed class FailingProjectCacheRepository(
        ProjectCacheDocument current) : IProjectCacheRepository
    {
        public Task<ProjectCacheDocument> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(current);

        public Task SaveAsync(
            ProjectCacheDocument document,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("cache unavailable"));
    }

    private sealed class RecordingProjectCacheRepository(
        ProjectCacheDocument current) : IProjectCacheRepository
    {
        public ProjectCacheDocument Current { get; private set; } = current;

        public Task<ProjectCacheDocument> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task SaveAsync(
            ProjectCacheDocument document,
            CancellationToken cancellationToken = default)
        {
            Current = document;
            return Task.CompletedTask;
        }

        public void Replace(ProjectCacheDocument document) => Current = document;
    }

    private sealed class RecordingSettingsRepository(AppSettings current)
        : ISettingsRepository
    {
        public AppSettings Current { get; private set; } = current;

        public int SaveCount { get; private set; }

        public Exception? SaveException { get; init; }

        public Task<AppSettings> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (SaveException is not null)
            {
                return Task.FromException(SaveException);
            }

            Current = settings;
            return Task.CompletedTask;
        }
    }

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
