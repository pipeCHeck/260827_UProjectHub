using System.Text.Json;
using UProjectHub.Core.Cache;
using UProjectHub.Core.Models;
using UProjectHub.Core.Storage;

namespace UProjectHub.Core.Tests.Cache;

[TestClass]
public sealed class EngineCacheRepositoryTests
{
    [TestMethod]
    public async Task MissingFileReturnsEmptyCurrentDocumentAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var repository = CreateRepository(temporaryDirectory.CacheFilePath);

        var document = await repository.LoadAsync();

        Assert.AreEqual(EngineCacheDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.HasCount(0, document.Engines);
    }

    [TestMethod]
    public async Task DerivedEngineMetadataRoundTripsAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var repository = CreateRepository(temporaryDirectory.CacheFilePath);
        var expected = CreateDocument(temporaryDirectory.Path);

        await repository.SaveAsync(expected);
        var actual = await repository.LoadAsync();

        Assert.AreEqual(EngineCacheDocument.CurrentSchemaVersion, actual.SchemaVersion);
        CollectionAssert.AreEqual(expected.Engines.ToArray(), actual.Engines.ToArray());
    }

    [TestMethod]
    public async Task CorruptJsonReturnsEmptyDocumentAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(temporaryDirectory.CacheFilePath, "{ broken cache");
        var repository = CreateRepository(temporaryDirectory.CacheFilePath);

        var document = await repository.LoadAsync();

        Assert.AreEqual(EngineCacheDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.HasCount(0, document.Engines);
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
              "engines": []
            }
            """);
        var repository = CreateRepository(temporaryDirectory.CacheFilePath);

        var document = await repository.LoadAsync();

        Assert.AreEqual(EngineCacheDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.HasCount(0, document.Engines);
    }

    [TestMethod]
    public async Task InvalidEngineEntryIsDiscardedWithoutLosingValidEntryAsync()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var rootPath = Path.Combine(temporaryDirectory.Path, "UE_5.10");
        var editorPath = Path.Combine(rootPath, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
        var cacheJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            engines = new object[]
            {
                new
                {
                    displayName = "Unreal Engine 5.10",
                    association = "5.10",
                    displayVersion = "5.10.1",
                    rootPath,
                    editorPath,
                    source = "launcher",
                    isUsable = true,
                },
                new
                {
                    displayName = "Invalid Engine",
                    association = "5.8",
                    displayVersion = "5.8",
                    rootPath = string.Empty,
                    editorPath = string.Empty,
                    source = "unknownProvider",
                    isUsable = false,
                },
            },
        });
        await File.WriteAllTextAsync(temporaryDirectory.CacheFilePath, cacheJson);
        var repository = CreateRepository(temporaryDirectory.CacheFilePath);

        var document = await repository.LoadAsync();

        Assert.HasCount(1, document.Engines);
        Assert.AreEqual("Unreal Engine 5.10", document.Engines[0].DisplayName);
        Assert.AreEqual(EngineSource.Launcher, document.Engines[0].Source);
    }

    private static IEngineCacheRepository CreateRepository(string cacheFilePath) =>
        new JsonEngineCacheRepository(cacheFilePath, new AtomicJsonFileWriter());

    private static EngineCacheDocument CreateDocument(string root) =>
        new()
        {
            Engines =
            [
                new EngineCacheEntry(
                    "Unreal Engine 5.10",
                    Association: "5.10",
                    DisplayVersion: "5.10.1",
                    RootPath: Path.Combine(root, "UE_5.10"),
                    EditorPath: Path.Combine(
                        root,
                        "UE_5.10",
                        "Engine",
                        "Binaries",
                        "Win64",
                        "UnrealEditor.exe"),
                    EngineSource.Launcher,
                    IsUsable: true),
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
            System.IO.Path.Combine(Path, "engine-cache.json");

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "UProjectHub.Tests",
                "EngineCache",
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
