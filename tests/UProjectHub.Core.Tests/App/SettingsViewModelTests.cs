using UProjectHub.App.Infrastructure;
using UProjectHub.App.Behaviors;
using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Discovery;
using UProjectHub.Core.Settings;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class SettingsViewModelTests
{
    [TestMethod]
    public async Task Load_PresentsExistingSettingsWithoutStartingRescan()
    {
        var settings = new AppSettings
        {
            ProjectSearchRoots = [@"C:\Projects", @"D:\Study"],
            ManualEngineRoots = [@"C:\UE"],
            ThemeMode = ThemeMode.Dark,
            RowDensity = RowDensity.Compact,
        };
        var fixture = CreateFixture(settings);

        await fixture.ViewModel.LoadAsync();

        CollectionAssert.AreEqual(settings.ProjectSearchRoots.ToArray(), fixture.ViewModel.SearchRoots.ToArray());
        CollectionAssert.AreEqual(settings.ManualEngineRoots.ToArray(), fixture.ViewModel.ManualEngineRoots.ToArray());
        Assert.AreEqual(ThemeMode.Dark, fixture.ViewModel.SelectedThemeMode);
        Assert.AreEqual(RowDensity.Compact, fixture.ViewModel.SelectedRowDensity);
        Assert.AreEqual(0, fixture.Operations.RescanCalls);
    }

    [TestMethod]
    public async Task AddSearchRoot_UsesPickerAndCancelDoesNothing()
    {
        var fixture = CreateFixture(new AppSettings());
        fixture.Picker.Results.Enqueue(@"C:\PickedRoot");

        await fixture.ViewModel.AddSearchRootCommand.ExecuteAsync();

        CollectionAssert.AreEqual(
            new[] { @"C:\PickedRoot" },
            fixture.ViewModel.SearchRoots.ToArray());
        Assert.AreEqual(1, fixture.Operations.AddSearchRootCalls);
        Assert.AreEqual(0, fixture.Operations.RescanCalls);

        fixture.Picker.Results.Enqueue(null);
        await fixture.ViewModel.AddSearchRootCommand.ExecuteAsync();

        Assert.AreEqual(1, fixture.Operations.AddSearchRootCalls);
    }

    [TestMethod]
    public async Task DroppedFolders_UseTheSameAddOperationAsPicker()
    {
        var fixture = CreateFixture(new AppSettings());

        await fixture.ViewModel.AddDroppedSearchRootsCommand.ExecuteAsync(
            new[] { @"C:\DroppedA", @"D:\DroppedB" });

        CollectionAssert.AreEqual(
            new[] { @"C:\DroppedA", @"D:\DroppedB" },
            fixture.Operations.AddedSearchRoots.ToArray());
        Assert.AreEqual(0, fixture.Operations.RescanCalls);
    }

    [TestMethod]
    public void FolderDrop_ExtractsFoldersAndIgnoresFilesWithoutScanning()
    {
        var root = Path.Combine(Path.GetTempPath(), $"UProjectHub-Drop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "NotARoot.uproject");
        File.WriteAllText(file, "{}");
        try
        {
            var data = new System.Windows.DataObject(
                System.Windows.DataFormats.FileDrop,
                new[] { root, file });

            var folders = FolderDropBehavior.GetDroppedFolders(data);

            CollectionAssert.AreEqual(new[] { root }, folders.ToArray());
            Assert.IsTrue(File.Exists(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DuplicateAndRemoveRoot_ReflectOnlySuccessfulSavedSettings()
    {
        var initial = new AppSettings { ProjectSearchRoots = [@"C:\Root"] };
        var fixture = CreateFixture(initial);
        await fixture.ViewModel.LoadAsync();
        fixture.Picker.Results.Enqueue(@"C:\Root");

        await fixture.ViewModel.AddSearchRootCommand.ExecuteAsync();

        Assert.HasCount(1, fixture.ViewModel.SearchRoots);

        fixture.ViewModel.SelectedSearchRoot = @"C:\Root";
        await fixture.ViewModel.RemoveSearchRootCommand.ExecuteAsync();

        Assert.IsEmpty(fixture.ViewModel.SearchRoots);
    }

    [TestMethod]
    public async Task InvalidManualEngine_ShowsDiagnosticAndLeavesListUnchanged()
    {
        var fixture = CreateFixture(new AppSettings());
        fixture.Picker.Results.Enqueue(@"C:\InvalidEngine");
        fixture.Operations.NextManualResult = new ProjectOperationResult(
            false,
            false,
            null,
            "Expected Unreal Editor executable was not found.");

        await fixture.ViewModel.AddManualEngineCommand.ExecuteAsync();

        Assert.IsEmpty(fixture.ViewModel.ManualEngineRoots);
        StringAssert.Contains(fixture.ViewModel.StatusText, "Unreal Editor executable");
    }

    [TestMethod]
    public async Task ValidManualEngineAndRemoval_UpdateTheSavedList()
    {
        var fixture = CreateFixture(new AppSettings());
        fixture.Picker.Results.Enqueue(@"C:\ValidEngine");

        await fixture.ViewModel.AddManualEngineCommand.ExecuteAsync();

        CollectionAssert.AreEqual(
            new[] { @"C:\ValidEngine" },
            fixture.ViewModel.ManualEngineRoots.ToArray());

        fixture.ViewModel.SelectedManualEngineRoot = @"C:\ValidEngine";
        await fixture.ViewModel.RemoveManualEngineCommand.ExecuteAsync();

        Assert.IsEmpty(fixture.ViewModel.ManualEngineRoots);
    }

    [TestMethod]
    public async Task SaveAppearance_PersistsSelectedThemeAndDensity()
    {
        var fixture = CreateFixture(new AppSettings());
        fixture.ViewModel.SelectedThemeMode = ThemeMode.Light;
        fixture.ViewModel.SelectedRowDensity = RowDensity.Compact;

        await fixture.ViewModel.SaveAppearanceCommand.ExecuteAsync();

        Assert.AreEqual(ThemeMode.Light, fixture.Operations.LastThemeMode);
        Assert.AreEqual(RowDensity.Compact, fixture.Operations.LastRowDensity);
        Assert.AreEqual("Appearance saved.", fixture.ViewModel.StatusText);
    }

    [TestMethod]
    public async Task Rescan_IsExplicitAndBlocksReentryUntilCompletion()
    {
        var fixture = CreateFixture(new AppSettings());
        fixture.Operations.BlockRescan = true;

        var first = fixture.ViewModel.RescanCommand.ExecuteAsync();
        await fixture.Operations.RescanStarted.Task;

        Assert.IsTrue(fixture.ViewModel.IsBusy);
        Assert.IsFalse(fixture.ViewModel.RescanCommand.CanExecute(null));
        await fixture.ViewModel.RescanCommand.ExecuteAsync();
        Assert.AreEqual(1, fixture.Operations.RescanCalls);

        fixture.Operations.ReleaseRescan.SetResult();
        await first;

        Assert.IsFalse(fixture.ViewModel.IsBusy);
        Assert.AreEqual("Rescan complete.", fixture.ViewModel.StatusText);
    }

    [TestMethod]
    public async Task OperationFailure_IsPresentedWithoutReplacingVisibleSettings()
    {
        var initial = new AppSettings { ProjectSearchRoots = [@"C:\Existing"] };
        var fixture = CreateFixture(initial);
        await fixture.ViewModel.LoadAsync();
        fixture.Picker.Results.Enqueue(@"D:\Rejected");
        fixture.Operations.NextSearchResult = new ProjectOperationResult(
            false,
            false,
            null,
            "Settings could not be saved.");

        await fixture.ViewModel.AddSearchRootCommand.ExecuteAsync();

        CollectionAssert.AreEqual(
            initial.ProjectSearchRoots.ToArray(),
            fixture.ViewModel.SearchRoots.ToArray());
        Assert.AreEqual("Settings could not be saved.", fixture.ViewModel.StatusText);
    }

    private static Fixture CreateFixture(AppSettings settings)
    {
        var operations = new FakeProjectOperations(settings);
        var picker = new FakeFolderPickerService();
        return new Fixture(
            new SettingsViewModel(operations, picker),
            operations,
            picker);
    }

    private sealed record Fixture(
        SettingsViewModel ViewModel,
        FakeProjectOperations Operations,
        FakeFolderPickerService Picker);

    private sealed class FakeFolderPickerService : IFolderPickerService
    {
        public Queue<string?> Results { get; } = new();

        public string? PickFolder(string title) => Results.Dequeue();
    }

    private sealed class FakeProjectOperations(AppSettings settings) : IProjectOperations
    {
        public AppSettings Current { get; private set; } = settings;

        public int AddSearchRootCalls { get; private set; }

        public List<string> AddedSearchRoots { get; } = [];

        public int RescanCalls { get; private set; }

        public bool BlockRescan { get; set; }

        public TaskCompletionSource RescanStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseRescan { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ProjectOperationResult? NextSearchResult { get; set; }

        public ProjectOperationResult? NextManualResult { get; set; }

        public ThemeMode? LastThemeMode { get; private set; }

        public RowDensity? LastRowDensity { get; private set; }

        public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task<ProjectOperationResult> AddProjectSearchRootAsync(
            string root,
            CancellationToken cancellationToken = default)
        {
            AddSearchRootCalls++;
            AddedSearchRoots.Add(root);
            if (NextSearchResult is { } configured)
            {
                NextSearchResult = null;
                return Task.FromResult(configured);
            }

            if (Current.ProjectSearchRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(new ProjectOperationResult(true, false, Current, null));
            }

            Current = Current with { ProjectSearchRoots = [.. Current.ProjectSearchRoots, root] };
            return Task.FromResult(new ProjectOperationResult(true, true, Current, null));
        }

        public Task<ProjectOperationResult> RemoveProjectSearchRootAsync(
            string root,
            CancellationToken cancellationToken = default)
        {
            Current = Current with
            {
                ProjectSearchRoots = Current.ProjectSearchRoots
                    .Where(item => !string.Equals(item, root, StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
            };
            return Task.FromResult(new ProjectOperationResult(true, true, Current, null));
        }

        public Task<ProjectOperationResult> AddManualEngineRootAsync(
            string root,
            CancellationToken cancellationToken = default)
        {
            if (NextManualResult is { } configured)
            {
                NextManualResult = null;
                return Task.FromResult(configured);
            }

            Current = Current with { ManualEngineRoots = [.. Current.ManualEngineRoots, root] };
            return Task.FromResult(new ProjectOperationResult(true, true, Current, null));
        }

        public Task<ProjectOperationResult> RemoveManualEngineRootAsync(
            string root,
            CancellationToken cancellationToken = default)
        {
            Current = Current with
            {
                ManualEngineRoots = Current.ManualEngineRoots
                    .Where(item => !string.Equals(item, root, StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
            };
            return Task.FromResult(new ProjectOperationResult(true, true, Current, null));
        }

        public Task<ProjectOperationResult> SaveAppearanceAsync(
            ThemeMode themeMode,
            RowDensity rowDensity,
            CancellationToken cancellationToken = default)
        {
            LastThemeMode = themeMode;
            LastRowDensity = rowDensity;
            Current = Current with { ThemeMode = themeMode, RowDensity = rowDensity };
            return Task.FromResult(new ProjectOperationResult(true, true, Current, null));
        }

        public Task<ProjectOperationResult> SaveViewStateAsync(
            UProjectHub.Core.Sorting.ProjectSortDefinition activeSort,
            VisibleFilterState visibleFilters,
            IReadOnlyList<ColumnLayoutState>? columnLayout = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectOperationResult(true, true, Current, null));

        public async Task<ProjectRescanOperationResult> RescanAsync(
            CancellationToken cancellationToken = default)
        {
            RescanCalls++;
            RescanStarted.TrySetResult();
            if (BlockRescan)
            {
                await ReleaseRescan.Task;
            }

            return new ProjectRescanOperationResult(
                true,
                new ProjectCatalog().GetSnapshot(),
                [],
                null);
        }
    }
}
