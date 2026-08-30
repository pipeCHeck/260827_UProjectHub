using UProjectHub.App.Infrastructure;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Windows.Cleanup;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ProjectCleanupViewModelTests
{
    [TestMethod]
    public async Task InitializationUsesSafeDefaultsAndExposesExactInspectionDataAsync()
    {
        var project = CreateProject(ProjectType.Cpp);
        var service = new FakeCleanupService(project, CreateInspection(project));
        using var viewModel = new ProjectCleanupViewModel(project, service);

        await viewModel.InitializeAsync();

        Assert.HasCount(5, viewModel.Items);
        Assert.IsTrue(Item(viewModel, ProjectCleanupTargetKind.Intermediate).IsSelected);
        Assert.IsTrue(Item(viewModel, ProjectCleanupTargetKind.DerivedDataCache).IsSelected);
        Assert.IsTrue(Item(viewModel, ProjectCleanupTargetKind.VisualStudioWorkspace).IsSelected);
        Assert.IsFalse(Item(viewModel, ProjectCleanupTargetKind.Binaries).IsSelected);
        Assert.IsFalse(Item(viewModel, ProjectCleanupTargetKind.Solution).IsSelected);
        Assert.AreEqual(@"D:\Projects\Game\Intermediate", Item(viewModel, ProjectCleanupTargetKind.Intermediate).Path);
        Assert.AreEqual("10 B", Item(viewModel, ProjectCleanupTargetKind.Intermediate).SizeText);
        Assert.AreEqual("Present", Item(viewModel, ProjectCleanupTargetKind.Intermediate).AvailabilityText);
        Assert.IsTrue(viewModel.CanBeginConfirmation);
        Assert.IsFalse(viewModel.IsConfirmationVisible);
    }

    [TestMethod]
    public async Task MissingAndUnsafeItemsCannotBeSelectedAsync()
    {
        var project = CreateProject(ProjectType.Cpp);
        var inspection = CreateInspection(project) with
        {
            Items =
            [
                Inspection(ProjectCleanupTargetKind.Intermediate, "Intermediate", false, false),
                Inspection(ProjectCleanupTargetKind.DerivedDataCache, "DerivedDataCache", true, false, error: "Reparse point found."),
                Inspection(ProjectCleanupTargetKind.VisualStudioWorkspace, ".vs", true, true, 3),
                Inspection(ProjectCleanupTargetKind.Binaries, "Binaries", false, false),
                Inspection(ProjectCleanupTargetKind.Solution, "Game.sln", false, false),
            ],
        };
        using var viewModel = new ProjectCleanupViewModel(
            project,
            new FakeCleanupService(project, inspection));

        await viewModel.InitializeAsync();

        Assert.IsFalse(Item(viewModel, ProjectCleanupTargetKind.Intermediate).CanSelect);
        Assert.IsFalse(Item(viewModel, ProjectCleanupTargetKind.Intermediate).IsSelected);
        Assert.AreEqual("Not found", Item(viewModel, ProjectCleanupTargetKind.Intermediate).AvailabilityText);
        Assert.IsFalse(Item(viewModel, ProjectCleanupTargetKind.DerivedDataCache).CanSelect);
        Assert.AreEqual("Blocked", Item(viewModel, ProjectCleanupTargetKind.DerivedDataCache).AvailabilityText);
        Assert.AreEqual("Reparse point found.", Item(viewModel, ProjectCleanupTargetKind.DerivedDataCache).DetailText);
        Assert.IsTrue(Item(viewModel, ProjectCleanupTargetKind.VisualStudioWorkspace).IsSelected);
    }

    [TestMethod]
    public async Task RiskSelectionsShowGuidanceAndRequireFinalConfirmationAsync()
    {
        var project = CreateProject(ProjectType.Cpp);
        using var viewModel = new ProjectCleanupViewModel(
            project,
            new FakeCleanupService(project, CreateInspection(project)));
        await viewModel.InitializeAsync();

        Item(viewModel, ProjectCleanupTargetKind.Binaries).IsSelected = true;
        Item(viewModel, ProjectCleanupTargetKind.Solution).IsSelected = true;

        Assert.IsTrue(viewModel.ShowBinariesWarning);
        Assert.IsTrue(viewModel.ShowSolutionInformation);
        Assert.IsFalse(viewModel.IsConfirmationVisible);
        viewModel.BeginConfirmationCommand.Execute(null);
        Assert.IsTrue(viewModel.IsConfirmationVisible);
        Assert.IsFalse(viewModel.Items.All(item => item.CanSelect));
    }

    [TestMethod]
    public async Task ConfirmRunsSelectedTargetsThenRefreshesActualStateAndResultsAsync()
    {
        var project = CreateProject(ProjectType.Cpp);
        var service = new FakeCleanupService(project, CreateInspection(project));
        var callbackCount = 0;
        using var viewModel = new ProjectCleanupViewModel(
            project,
            service,
            () =>
            {
                callbackCount++;
                return Task.CompletedTask;
            });
        await viewModel.InitializeAsync();
        Item(viewModel, ProjectCleanupTargetKind.DerivedDataCache).IsSelected = false;
        Item(viewModel, ProjectCleanupTargetKind.VisualStudioWorkspace).IsSelected = false;
        Item(viewModel, ProjectCleanupTargetKind.Solution).IsSelected = true;
        service.Result = new ProjectCleanupResult(
        [
            new ProjectCleanupItemResult(
                ProjectCleanupTargetKind.Intermediate,
                @"D:\Projects\Game\Intermediate",
                ProjectCleanupItemStatus.Deleted,
                10,
                null),
            new ProjectCleanupItemResult(
                ProjectCleanupTargetKind.Solution,
                @"D:\Projects\Game\Game.sln",
                ProjectCleanupItemStatus.Deleted,
                50,
                null),
        ]);
        service.RefreshedInspection = CreateMissingInspection(project);
        viewModel.BeginConfirmationCommand.Execute(null);

        await ((AsyncRelayCommand)viewModel.ConfirmCleanupCommand).ExecuteAsync();

        CollectionAssert.AreEquivalent(
            new[]
            {
                ProjectCleanupTargetKind.Intermediate,
                ProjectCleanupTargetKind.Solution,
            },
            service.LastRequest!.Targets.ToArray());
        Assert.AreEqual(60, viewModel.FreedBytes);
        Assert.AreEqual("60 B", viewModel.FreedSizeText);
        Assert.IsFalse(Item(viewModel, ProjectCleanupTargetKind.Intermediate).Exists);
        Assert.AreEqual("Deleted — 10 B", Item(viewModel, ProjectCleanupTargetKind.Intermediate).ResultText);
        Assert.AreEqual(1, callbackCount);
        Assert.IsFalse(viewModel.IsConfirmationVisible);
    }

    private static ProjectCleanupItemViewModel Item(
        ProjectCleanupViewModel viewModel,
        ProjectCleanupTargetKind kind) =>
        viewModel.Items.Single(item => item.Kind == kind);

    private static ProjectCleanupInspection CreateInspection(UnrealProject project) =>
        new(project,
        [
            Inspection(ProjectCleanupTargetKind.Intermediate, "Intermediate", true, true, 10),
            Inspection(ProjectCleanupTargetKind.DerivedDataCache, "DerivedDataCache", true, true, 20),
            Inspection(ProjectCleanupTargetKind.VisualStudioWorkspace, ".vs", true, true, 30),
            Inspection(ProjectCleanupTargetKind.Binaries, "Binaries", true, true, 40),
            Inspection(ProjectCleanupTargetKind.Solution, "Game.sln", true, true, 50),
        ]);

    private static ProjectCleanupInspection CreateMissingInspection(UnrealProject project) =>
        new(project,
        [
            Inspection(ProjectCleanupTargetKind.Intermediate, "Intermediate", false, false),
            Inspection(ProjectCleanupTargetKind.DerivedDataCache, "DerivedDataCache", true, true, 20),
            Inspection(ProjectCleanupTargetKind.VisualStudioWorkspace, ".vs", true, true, 30),
            Inspection(ProjectCleanupTargetKind.Binaries, "Binaries", true, true, 40),
            Inspection(ProjectCleanupTargetKind.Solution, "Game.sln", false, false),
        ]);

    private static ProjectCleanupItemInspection Inspection(
        ProjectCleanupTargetKind kind,
        string relativePath,
        bool exists,
        bool canDelete,
        long size = 0,
        string? error = null) =>
        new(
            kind,
            Path.GetFullPath(Path.Combine(@"D:\Projects\Game", relativePath)),
            exists,
            canDelete,
            size,
            error,
            Array.Empty<string>());

    private static UnrealProject CreateProject(ProjectType type) =>
        new(
            "Game",
            new ProjectPath(@"D:\Projects\Game\Game.uproject"),
            "5.8",
            "5.8",
            type,
            DateTimeOffset.UnixEpoch,
            LastLaunched: null,
            IsFavorite: false,
            ProjectState.Available,
            EngineResolutionState.Resolved);

    private sealed class FakeCleanupService(
        UnrealProject project,
        ProjectCleanupInspection inspection) : IProjectCleanupService
    {
        private ProjectCleanupInspection _inspection = inspection;

        public ProjectCleanupRequest? LastRequest { get; private set; }

        public ProjectCleanupResult Result { get; set; } =
            new(Array.Empty<ProjectCleanupItemResult>());

        public ProjectCleanupInspection? RefreshedInspection { get; set; }

        public Task<ProjectCleanupInspection> InspectAsync(
            UnrealProject requestedProject,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(project, requestedProject);
            var result = _inspection;
            if (LastRequest is not null && RefreshedInspection is not null)
            {
                result = RefreshedInspection;
                _inspection = result;
            }

            return Task.FromResult(result);
        }

        public Task<ProjectCleanupResult> CleanupAsync(
            ProjectCleanupRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Result);
        }
    }
}
