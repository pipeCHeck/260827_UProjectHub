using System.Text.Json;
using UProjectHub.Core.Cache;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Storage;

namespace UProjectHub.Core.Tests.Cache;

[TestClass]
public sealed class ProjectCacheRepositoryTests
{
    [TestMethod]
    public async Task MissingFileReturnsEmptyCurrentDocumentAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var repository = CreateRepository(temporaryDirectory.CacheFilePath);

        var document = await repository.LoadAsync();

        Assert.AreEqual(ProjectCacheDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.HasCount(0, document.Projects);
    }

    [TestMethod]
    public async Task DerivedProjectMetadataRoundTripsAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var repository = CreateRepository(temporaryDirectory.CacheFilePath);
        var expected = CreateDocument(temporaryDirectory.Path, "CachedGame");

        await repository.SaveAsync(expected);
        var actual = await repository.LoadAsync();

        Assert.AreEqual(ProjectCacheDocument.CurrentSchemaVersion, actual.SchemaVersion);
        CollectionAssert.AreEqual(expected.Projects.ToArray(), actual.Projects.ToArray());
    }

    [TestMethod]
    public async Task CorruptJsonReturnsEmptyDocumentAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(temporaryDirectory.CacheFilePath, "{ broken cache");
        var repository = CreateRepository(temporaryDirectory.CacheFilePath);

        var document = await repository.LoadAsync();

        Assert.AreEqual(ProjectCacheDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.HasCount(0, document.Projects);
    }

    [TestMethod]
    public async Task UnsupportedSchemaVersionReturnsEmptyDocumentAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(
            temporaryDirectory.CacheFilePath,
            """
            {
              "schemaVersion": 999,
              "projects": []
            }
            """);
        var repository = CreateRepository(temporaryDirectory.CacheFilePath);

        var document = await repository.LoadAsync();

        Assert.AreEqual(ProjectCacheDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.HasCount(0, document.Projects);
    }

    [TestMethod]
    public async Task InvalidSchemaVersionTypeReturnsEmptyDocumentAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(
            temporaryDirectory.CacheFilePath,
            """
            {
              "schemaVersion": "1",
              "projects": []
            }
            """);
        var repository = CreateRepository(temporaryDirectory.CacheFilePath);

        var document = await repository.LoadAsync();

        Assert.AreEqual(ProjectCacheDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.HasCount(0, document.Projects);
    }

    [TestMethod]
    public async Task InvalidProjectEntryIsDiscardedWithoutLosingValidEntryAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var validPath = new ProjectPath(Path.Combine(
            temporaryDirectory.Path,
            "Projects",
            "Valid.uproject"));
        var cacheJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            projects = new object[]
            {
                new
                {
                    projectFilePath = validPath.Value,
                    name = "Valid",
                    engineAssociation = "5.10",
                    engineDisplayVersion = "5.10.1",
                    projectType = "cpp",
                    lastModified = "2026-08-27T01:02:03+00:00",
                    projectState = "available",
                    engineState = "resolved",
                },
                new
                {
                    projectFilePath = string.Empty,
                    name = "Invalid",
                    engineAssociation = "5.8",
                    engineDisplayVersion = "5.8",
                    projectType = "blueprint",
                    lastModified = "2026-08-27T01:02:03+00:00",
                    projectState = "available",
                    engineState = "resolved",
                },
            },
        });
        await File.WriteAllTextAsync(temporaryDirectory.CacheFilePath, cacheJson);
        var repository = CreateRepository(temporaryDirectory.CacheFilePath);

        var document = await repository.LoadAsync();

        Assert.HasCount(1, document.Projects);
        Assert.AreEqual(validPath, document.Projects[0].ProjectFilePath);
        Assert.AreEqual("Valid", document.Projects[0].Name);
    }

    [TestMethod]
    public async Task ProjectCacheDoesNotPersistUserStateOrCreateBackupAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var repository = CreateRepository(temporaryDirectory.CacheFilePath);
        await repository.SaveAsync(CreateDocument(temporaryDirectory.Path, "First"));
        await repository.SaveAsync(CreateDocument(temporaryDirectory.Path, "Second"));

        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(temporaryDirectory.CacheFilePath));
        var root = document.RootElement;
        var project = root.GetProperty("projects")[0];

        Assert.IsFalse(project.TryGetProperty("isFavorite", out _));
        Assert.IsFalse(project.TryGetProperty("lastLaunched", out _));
        Assert.IsFalse(root.TryGetProperty("projectSearchRoots", out _));
        Assert.IsFalse(root.TryGetProperty("manualEngineRoots", out _));
        Assert.IsFalse(File.Exists($"{temporaryDirectory.CacheFilePath}.bak"));
    }

    private static IProjectCacheRepository CreateRepository(string cacheFilePath) =>
        new JsonProjectCacheRepository(cacheFilePath, new AtomicJsonFileWriter());

    private static ProjectCacheDocument CreateDocument(string root, string name) =>
        new()
        {
            Projects =
            [
                new ProjectCacheEntry(
                    new ProjectPath(Path.Combine(
                        root,
                        "Projects",
                        "Temporary",
                        "..",
                        $"{name}.uproject")),
                    name,
                    EngineAssociation: "5.10",
                    EngineDisplayVersion: "5.10.1",
                    ProjectType.Cpp,
                    new DateTimeOffset(2026, 8, 27, 1, 2, 3, TimeSpan.Zero),
                    ProjectState.Available,
                    EngineResolutionState.Resolved),
            ],
        };

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public string CacheFilePath =>
            System.IO.Path.Combine(Path, "project-cache.json");

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "UProjectHub.Tests",
                "ProjectCache",
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
