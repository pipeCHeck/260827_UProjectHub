using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;
using UProjectHub.Windows.Engines;

namespace UProjectHub.Core.Tests.Windows.Engines;

[TestClass]
public sealed class EngineDiscoveryServiceTests
{
    [TestMethod]
    public async Task MergesProviderEnginesAndPreservesProviderIssuesAsync()
    {
        using var fixture = TemporaryEditorPaths.Create();
        var first = fixture.CreateEngine("First", "5.8", EngineSource.Launcher);
        var second = fixture.CreateEngine("Second", "5.9", EngineSource.Manual);
        var expectedIssue = new EngineProviderIssue("manual", "diagnostic");
        var service = new EngineDiscoveryService(
        [
            FixedEngineProvider.Returning(first),
            FixedEngineProvider.Returning([second], [expectedIssue]),
        ]);

        var result = await service.DiscoverAsync();

        CollectionAssert.AreEqual(
            new[] { first, second },
            result.Engines.ToArray());
        CollectionAssert.AreEqual(
            new[] { expectedIssue },
            result.Issues.ToArray());
    }

    [TestMethod]
    public async Task ProviderFailureIsIsolatedWithoutDiscardingOtherResultsAsync()
    {
        using var fixture = TemporaryEditorPaths.Create();
        var sourceBuild = fixture.CreateEngine(
            "Source",
            displayVersion: null,
            EngineSource.SourceBuild,
            association: "{01234567-89ab-cdef-0123-456789abcdef}");
        var manual = fixture.CreateEngine("Manual", "5.8.2", EngineSource.Manual);
        var service = new EngineDiscoveryService(
        [
            FixedEngineProvider.Throwing(new IOException("Launcher unavailable.")),
            FixedEngineProvider.Returning(sourceBuild),
            FixedEngineProvider.Returning(manual),
        ]);

        var result = await service.DiscoverAsync();

        CollectionAssert.AreEqual(
            new[] { sourceBuild, manual },
            result.Engines.ToArray());
        Assert.HasCount(1, result.Issues);
        Assert.Contains(
            result.Issues[0].Message,
            "Launcher unavailable.");
    }

    [TestMethod]
    public async Task SamePhysicalEditorUsesDeterministicFirstSeenCandidateAsync()
    {
        using var fixture = TemporaryEditorPaths.Create();
        var first = fixture.CreateEngine("Shared", "5.8", EngineSource.Manual);
        var alternateEditorPath = first.EditorPath
            .Replace('\\', '/')
            .Replace("engine", "ENGINE", StringComparison.OrdinalIgnoreCase)
            .Replace(
                "/Win64/UnrealEditor.exe",
                "/Win64/Unused/../UnrealEditor.exe",
                StringComparison.OrdinalIgnoreCase);
        var second = first with
        {
            DisplayName = "Second provider candidate",
            EditorPath = alternateEditorPath,
            Source = EngineSource.Launcher,
        };

        var firstThenSecond = await new EngineDiscoveryService(
        [
            FixedEngineProvider.Returning(first),
            FixedEngineProvider.Returning(second),
        ]).DiscoverAsync();
        var secondThenFirst = await new EngineDiscoveryService(
        [
            FixedEngineProvider.Returning(second),
            FixedEngineProvider.Returning(first),
        ]).DiscoverAsync();

        Assert.HasCount(1, firstThenSecond.Engines);
        Assert.AreEqual(first, firstThenSecond.Engines[0]);
        Assert.HasCount(1, secondThenFirst.Engines);
        Assert.AreEqual(second, secondThenFirst.Engines[0]);
    }

    [TestMethod]
    public async Task SameVersionAtDifferentEditorPathsRemainsAmbiguousToResolverAsync()
    {
        using var fixture = TemporaryEditorPaths.Create();
        var launcher = fixture.CreateEngine(
            "Launcher58",
            "5.8",
            EngineSource.Launcher);
        var manual = fixture.CreateEngine(
            "Manual582",
            "5.8.2",
            EngineSource.Manual);
        var service = new EngineDiscoveryService(
        [
            FixedEngineProvider.Returning(launcher),
            FixedEngineProvider.Returning(manual),
        ]);

        var result = await service.DiscoverAsync();
        var resolution = EngineResolver.Resolve("5.8", result.Engines);

        Assert.HasCount(2, result.Engines);
        Assert.AreEqual(EngineResolutionState.Ambiguous, resolution.State);
        Assert.HasCount(2, resolution.MatchingCandidates);
    }

    [TestMethod]
    public async Task CancellationIsPropagatedAsync()
    {
        var service = new EngineDiscoveryService(
        [
            FixedEngineProvider.Returning(),
        ]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.DiscoverAsync(cancellation.Token));
    }

    private sealed class FixedEngineProvider : IEngineProvider
    {
        private readonly EngineProviderResult? _result;
        private readonly Exception? _exception;

        private FixedEngineProvider(
            EngineProviderResult? result,
            Exception? exception)
        {
            _result = result;
            _exception = exception;
        }

        public static FixedEngineProvider Returning(
            params InstalledEngine[] engines) =>
            Returning(engines, []);

        public static FixedEngineProvider Returning(
            IEnumerable<InstalledEngine> engines,
            IEnumerable<EngineProviderIssue> issues) =>
            new(new EngineProviderResult(engines, issues), null);

        public static FixedEngineProvider Throwing(Exception exception) =>
            new(null, exception);

        public Task<EngineProviderResult> DiscoverAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_result!);
        }
    }

    private sealed class TemporaryEditorPaths : IDisposable
    {
        private TemporaryEditorPaths(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TemporaryEditorPaths Create()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                "EngineDiscoveryService",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return new TemporaryEditorPaths(rootPath);
        }

        public InstalledEngine CreateEngine(
            string name,
            string? displayVersion,
            EngineSource source,
            string? association = null)
        {
            var engineRoot = Path.GetFullPath(Path.Combine(RootPath, name));
            var editorPath = Path.Combine(
                engineRoot,
                "Engine",
                "Binaries",
                "Win64",
                "UnrealEditor.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(editorPath)!);
            File.WriteAllText(editorPath, string.Empty);

            return new InstalledEngine(
                DisplayName: name,
                Association: association ?? displayVersion,
                DisplayVersion: displayVersion,
                RootPath: engineRoot,
                EditorPath: editorPath,
                Source: source,
                IsUsable: true);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
