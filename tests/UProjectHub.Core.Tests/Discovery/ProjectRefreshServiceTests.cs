using UProjectHub.Core.Activity;
using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Discovery;
using UProjectHub.Core.Models;
using UProjectHub.Core.Parsing;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Settings;

namespace UProjectHub.Core.Tests.Discovery;

[TestClass]
public sealed class ProjectRefreshServiceTests
{
    [TestMethod]
    public async Task RefreshUpdatesOnlyKnownProjectsAndMergesUserStateAsync()
    {
        using var fixture = TemporaryProjectDirectory.Create();
        var knownPath = fixture.CreateProject("Known", "5.10", isCpp: true);
        var newPath = fixture.CreateProject("New", "5.9", isCpp: false);
        var lastLaunched = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
        var catalog = new ProjectCatalog();
        catalog.Upsert(DiscoveryTestProjects.Create(knownPath) with
        {
            IsFavorite = true,
            LastLaunched = lastLaunched,
        });
        var cache = new RecordingProjectCacheRepository();
        var progress = new RecordingProjectProgress();
        var settings = new AppSettings
        {
            ProjectSearchRoots = [fixture.RootPath],
            ProjectUserStates =
            [
                new ProjectUserState(
                    new ProjectPath(knownPath.Value.ToUpperInvariant()),
                    IsFavorite: true,
                    LastLaunched: lastLaunched),
            ],
        };
        var service = DiscoveryTestServices.CreateRefresh(catalog, cache);

        var result = await service.RefreshKnownAsync(settings, progress);

        Assert.HasCount(1, result.Updates);
        Assert.HasCount(1, progress.Updates);
        Assert.HasCount(1, catalog.GetSnapshot().Projects);
        Assert.IsFalse(catalog.TryGet(newPath, out _));
        Assert.IsTrue(catalog.TryGet(knownPath, out var refreshed));
        Assert.AreEqual(ProjectState.Available, refreshed.ProjectState);
        Assert.AreEqual(ProjectType.Cpp, refreshed.ProjectType);
        Assert.AreEqual("5.10", refreshed.EngineAssociation);
        Assert.IsTrue(refreshed.IsFavorite);
        Assert.AreEqual(lastLaunched, refreshed.LastLaunched);
        Assert.AreEqual(knownPath, progress.Updates[0].ProjectFilePath);
        Assert.HasCount(1, cache.SavedDocuments);
        Assert.HasCount(1, cache.SavedDocuments[0].Projects);
        Assert.IsNull(typeof(ProjectCacheEntry).GetProperty("IsFavorite"));
        Assert.IsNull(typeof(ProjectCacheEntry).GetProperty("LastLaunched"));
    }

    [TestMethod]
    public async Task RefreshDoesNotOverwriteNewerInMemoryTagsAndNoteWithStaleSettingsAsync()
    {
        using var fixture = TemporaryProjectDirectory.Create();
        var knownPath = fixture.CreateProject("Known", "5.10", isCpp: true);
        var catalog = new ProjectCatalog();
        catalog.Upsert(DiscoveryTestProjects.Create(knownPath) with
        {
            Tags = ["Current"],
            Note = "Saved while refresh was running.",
        });
        var staleSettings = new AppSettings
        {
            ProjectUserStates =
            [
                new ProjectUserState(knownPath)
                {
                    Tags = ["Stale"],
                    Note = "Old note",
                },
            ],
        };
        var service = DiscoveryTestServices.CreateRefresh(
            catalog,
            new RecordingProjectCacheRepository());

        await service.RefreshKnownAsync(staleSettings);

        Assert.IsTrue(catalog.TryGet(knownPath, out var refreshed));
        CollectionAssert.AreEqual(new[] { "Current" }, refreshed.Tags.ToArray());
        Assert.AreEqual("Saved while refresh was running.", refreshed.Note);
        Assert.AreEqual("5.10", refreshed.EngineAssociation);
        Assert.AreEqual(ProjectType.Cpp, refreshed.ProjectType);
    }

    [TestMethod]
    public async Task RefreshKeepsMissingAndContinuesWithAvailableProjectAsync()
    {
        using var fixture = TemporaryProjectDirectory.Create();
        var missingPath = fixture.PathFor("Missing");
        var availablePath = fixture.CreateProject("Available", "5.9", isCpp: false);
        var catalog = new ProjectCatalog();
        catalog.Upsert(DiscoveryTestProjects.Create(missingPath));
        catalog.Upsert(DiscoveryTestProjects.Create(availablePath));
        var cache = new RecordingProjectCacheRepository();
        var progress = new RecordingProjectProgress();
        var service = DiscoveryTestServices.CreateRefresh(catalog, cache);

        var result = await service.RefreshKnownAsync(new AppSettings(), progress);

        Assert.HasCount(2, result.Updates);
        Assert.HasCount(2, progress.Updates);
        Assert.HasCount(2, catalog.GetSnapshot().Projects);
        Assert.IsTrue(catalog.TryGet(missingPath, out var missing));
        Assert.AreEqual(ProjectState.Missing, missing.ProjectState);
        Assert.IsTrue(catalog.TryGet(availablePath, out var available));
        Assert.AreEqual(ProjectState.Available, available.ProjectState);
        Assert.HasCount(2, cache.SavedDocuments.Single().Projects);
        Assert.IsTrue(cache.SavedDocuments.Single().Projects.Any(entry =>
            entry.ProjectFilePath.Equals(missingPath)
            && entry.ProjectState == ProjectState.Missing));
    }

    [TestMethod]
    public async Task MalformedKnownProjectDoesNotPreventOtherRefreshAsync()
    {
        using var fixture = TemporaryProjectDirectory.Create();
        var brokenPath = fixture.CreateMalformedProject("Broken");
        var validPath = fixture.CreateProject("Valid", "5.10", isCpp: true);
        var catalog = new ProjectCatalog();
        catalog.Upsert(DiscoveryTestProjects.Create(brokenPath));
        catalog.Upsert(DiscoveryTestProjects.Create(validPath));
        var cache = new RecordingProjectCacheRepository();
        var progress = new RecordingProjectProgress();
        var service = DiscoveryTestServices.CreateRefresh(catalog, cache);

        var result = await service.RefreshKnownAsync(new AppSettings(), progress);

        Assert.HasCount(2, result.Updates);
        Assert.HasCount(1, result.Issues);
        Assert.HasCount(2, progress.Updates);
        Assert.IsTrue(catalog.TryGet(brokenPath, out var broken));
        Assert.AreEqual(ProjectState.Broken, broken.ProjectState);
        Assert.IsTrue(catalog.TryGet(validPath, out var valid));
        Assert.AreEqual(ProjectState.Available, valid.ProjectState);
        Assert.IsNotNull(progress.Updates.Single(update =>
            update.ProjectFilePath.Equals(brokenPath)).Issue);
    }

    [TestMethod]
    public async Task CancellationAfterIncrementalUpdateKeepsPartialCatalogAndSkipsCacheSaveAsync()
    {
        using var fixture = TemporaryProjectDirectory.Create();
        var firstPath = fixture.CreateProject("First", "5.9", isCpp: false);
        var secondPath = fixture.CreateProject("Second", "5.10", isCpp: true);
        var catalog = new ProjectCatalog();
        catalog.Upsert(DiscoveryTestProjects.Create(firstPath, ProjectState.Missing));
        catalog.Upsert(DiscoveryTestProjects.Create(secondPath, ProjectState.Missing));
        var cache = new RecordingProjectCacheRepository();
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProjectProgress(cancellation.Cancel);
        var service = DiscoveryTestServices.CreateRefresh(catalog, cache);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.RefreshKnownAsync(
                new AppSettings(),
                progress,
                cancellation.Token));

        Assert.HasCount(1, progress.Updates);
        Assert.AreEqual(
            1,
            catalog.GetSnapshot().Projects.Count(project =>
                project.ProjectState == ProjectState.Available));
        Assert.AreEqual(
            1,
            catalog.GetSnapshot().Projects.Count(project =>
                project.ProjectState == ProjectState.Missing));
        Assert.AreEqual(0, cache.SaveCallCount);
    }
}

internal static class DiscoveryTestServices
{
    public static ProjectRefreshService CreateRefresh(
        ProjectCatalog catalog,
        IProjectCacheRepository cacheRepository) =>
        new(
            catalog,
            new ProjectMetadataLoader(
                new UProjectParser(),
                new ProjectActivityDetector(new ProjectActivityPolicy())),
            cacheRepository);

    public static ProjectRescanService CreateRescan(
        ProjectCatalog catalog,
        IProjectCacheRepository cacheRepository)
    {
        var metadataLoader = new ProjectMetadataLoader(
            new UProjectParser(),
            new ProjectActivityDetector(new ProjectActivityPolicy()));
        var discoveryService = new ProjectDiscoveryService(
            new ProjectRootScanner(new SystemProjectDirectoryEnumerator()),
            metadataLoader);
        return new ProjectRescanService(
            catalog,
            discoveryService,
            cacheRepository);
    }
}

internal static class DiscoveryTestProjects
{
    public static UnrealProject Create(
        ProjectPath projectPath,
        ProjectState projectState = ProjectState.Available) =>
        new(
            Path.GetFileNameWithoutExtension(projectPath.Value),
            projectPath,
            "stale",
            null,
            ProjectType.Blueprint,
            DateTimeOffset.MinValue,
            null,
            false,
            projectState,
            EngineResolutionState.Unknown);
}

internal sealed class RecordingProjectCacheRepository : IProjectCacheRepository
{
    public List<ProjectCacheDocument> SavedDocuments { get; } = [];

    public int SaveCallCount => SavedDocuments.Count;

    public Task<ProjectCacheDocument> LoadAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProjectCacheDocument());

    public Task SaveAsync(
        ProjectCacheDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SavedDocuments.Add(document);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingProjectProgress(
    Action? afterReport = null) : IProgress<ProjectRefreshUpdate>
{
    public List<ProjectRefreshUpdate> Updates { get; } = [];

    public void Report(ProjectRefreshUpdate value)
    {
        Updates.Add(value);
        afterReport?.Invoke();
    }
}

internal sealed class TemporaryProjectDirectory : IDisposable
{
    private TemporaryProjectDirectory(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public static TemporaryProjectDirectory Create()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "UProjectHub.Tests",
            "RefreshRescan",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        return new TemporaryProjectDirectory(rootPath);
    }

    public ProjectPath PathFor(string name) =>
        new(Path.Combine(RootPath, name, $"{name}.uproject"));

    public ProjectPath CreateProject(
        string name,
        string engineAssociation,
        bool isCpp)
    {
        var projectPath = PathFor(name);
        Directory.CreateDirectory(projectPath.Value[..projectPath.Value.LastIndexOf(
            Path.DirectorySeparatorChar)]);
        var modules = isCpp
            ? ",\n  \"Modules\": [{ \"Name\": \"Game\", \"Type\": \"Runtime\" }]"
            : string.Empty;
        File.WriteAllText(
            projectPath.Value,
            $$"""
            {
              "FileVersion": 3,
              "EngineAssociation": "{{engineAssociation}}"{{modules}}
            }
            """);
        return projectPath;
    }

    public ProjectPath CreateMalformedProject(string name)
    {
        var projectPath = PathFor(name);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath.Value)!);
        File.WriteAllText(projectPath.Value, "{ malformed json");
        return projectPath;
    }

    public void Dispose()
    {
        Directory.Delete(RootPath, recursive: true);
    }
}
