using UProjectHub.Core.Activity;
using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Discovery;
using UProjectHub.Core.Models;
using UProjectHub.Core.Parsing;
using UProjectHub.Core.Settings;

namespace UProjectHub.Core.Tests.Discovery;

[TestClass]
public sealed class ProjectRescanServiceTests
{
    [TestMethod]
    public async Task RescanDiscoversNewProjectAndRefreshesKnownProjectAsync()
    {
        using var fixture = TemporaryProjectDirectory.Create();
        var knownPath = fixture.CreateProject("Known", "5.10", isCpp: true);
        var newPath = fixture.CreateProject("New", "5.9", isCpp: false);
        var catalog = new ProjectCatalog();
        catalog.Upsert(DiscoveryTestProjects.Create(knownPath, ProjectState.Missing));
        var cache = new RecordingProjectCacheRepository();
        var progress = new RecordingProjectProgress();
        var settings = new AppSettings
        {
            ProjectUserStates =
            [
                new(knownPath, IsFavorite: true),
            ],
        };
        var service = DiscoveryTestServices.CreateRescan(catalog, cache);

        var result = await service.RescanAsync(
            [fixture.RootPath],
            settings,
            progress);

        Assert.HasCount(2, result.Updates);
        Assert.HasCount(2, progress.Updates);
        Assert.HasCount(2, catalog.GetSnapshot().Projects);
        Assert.IsTrue(catalog.TryGet(knownPath, out var known));
        Assert.AreEqual(ProjectState.Available, known.ProjectState);
        Assert.IsTrue(known.IsFavorite);
        Assert.IsTrue(catalog.TryGet(newPath, out var discovered));
        Assert.AreEqual(ProjectState.Available, discovered.ProjectState);
        Assert.AreEqual(ProjectType.Blueprint, discovered.ProjectType);
        Assert.HasCount(2, cache.SavedDocuments.Single().Projects);
    }

    [TestMethod]
    public async Task BrokenCandidateDoesNotPreventNewValidProjectFromBeingAddedAsync()
    {
        using var fixture = TemporaryProjectDirectory.Create();
        var brokenPath = fixture.CreateMalformedProject("Broken");
        var validPath = fixture.CreateProject("Valid", "5.10", isCpp: true);
        var catalog = new ProjectCatalog();
        var cache = new RecordingProjectCacheRepository();
        var progress = new RecordingProjectProgress();
        var service = DiscoveryTestServices.CreateRescan(catalog, cache);

        var result = await service.RescanAsync(
            [fixture.RootPath],
            new AppSettings(),
            progress);

        Assert.HasCount(2, result.Updates);
        Assert.HasCount(1, result.Issues);
        Assert.IsTrue(catalog.TryGet(brokenPath, out var broken));
        Assert.AreEqual(ProjectState.Broken, broken.ProjectState);
        Assert.IsTrue(catalog.TryGet(validPath, out var valid));
        Assert.AreEqual(ProjectState.Available, valid.ProjectState);
        Assert.IsNotNull(progress.Updates.Single(update =>
            update.ProjectFilePath.Equals(brokenPath)).Issue);
        Assert.IsNull(progress.Updates.Single(update =>
            update.ProjectFilePath.Equals(validPath)).Issue);
    }

    [TestMethod]
    public async Task CancellationAfterIncrementalUpdateKeepsPartialCatalogAndSkipsCacheSaveAsync()
    {
        using var fixture = TemporaryProjectDirectory.Create();
        fixture.CreateProject("First", "5.9", isCpp: false);
        fixture.CreateProject("Second", "5.10", isCpp: true);
        var catalog = new ProjectCatalog();
        var cache = new RecordingProjectCacheRepository();
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProjectProgress(cancellation.Cancel);
        var parser = new CountingProjectParser();
        var service = CreateRescan(catalog, cache, parser);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.RescanAsync(
                [fixture.RootPath],
                new AppSettings(),
                progress,
                cancellation.Token));

        Assert.HasCount(1, progress.Updates);
        Assert.HasCount(1, catalog.GetSnapshot().Projects);
        Assert.AreEqual(1, parser.ParseCallCount);
        Assert.AreEqual(0, cache.SaveCallCount);
    }

    private static ProjectRescanService CreateRescan(
        ProjectCatalog catalog,
        IProjectCacheRepository cacheRepository,
        IUProjectParser parser)
    {
        var metadataLoader = new ProjectMetadataLoader(
            parser,
            new ProjectActivityDetector(new ProjectActivityPolicy()));
        var discoveryService = new ProjectDiscoveryService(
            new ProjectRootScanner(new SystemProjectDirectoryEnumerator()),
            metadataLoader);
        return new ProjectRescanService(
            catalog,
            discoveryService,
            cacheRepository);
    }

    private sealed class CountingProjectParser : IUProjectParser
    {
        private readonly UProjectParser _inner = new();

        public int ParseCallCount { get; private set; }

        public Task<UProjectParseResult> ParseAsync(
            string projectFilePath,
            CancellationToken cancellationToken = default)
        {
            ParseCallCount++;
            return _inner.ParseAsync(projectFilePath, cancellationToken);
        }
    }
}
