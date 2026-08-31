using System.Windows.Input;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Tests.Time;
using UProjectHub.Windows.Launching;
using UProjectHub.Windows.Cleanup;
using UProjectHub.Windows.SourceControl;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ProjectContextActionsViewModelTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AvailableCppProjectExposesSharedActionSetAndMissingOnlyRemovalRule()
    {
        var fixture = CreateFixture(CreateProject(ProjectType.Cpp, ProjectState.Available));

        Assert.IsTrue(fixture.ViewModel.OpenProjectCommand.CanExecute(null));
        Assert.IsTrue(fixture.ViewModel.OpenInVisualStudioCommand.CanExecute(null));
        Assert.IsTrue(fixture.ViewModel.GenerateProjectFilesCommand.CanExecute(null));
        Assert.IsFalse(fixture.ViewModel.RemoveFromListCommand.CanExecute(null));
        Assert.IsNotNull(fixture.ViewModel.OpenProjectFolderCommand);
        Assert.IsNotNull(fixture.ViewModel.CopyPathCommand);
        Assert.IsNotNull(fixture.ViewModel.ToggleFavoriteCommand);
        Assert.IsNotNull(fixture.ViewModel.ProjectDetailsCommand);
        Assert.IsNotNull(fixture.ViewModel.TagsAndNotesCommand);
        Assert.IsTrue(fixture.ViewModel.ProjectCleanupCommand.CanExecute(null));
        Assert.AreEqual("Add to Favorites", fixture.ViewModel.ToggleFavoriteLabel);
        Assert.IsNull(fixture.ViewModel.OpenInVisualStudioUnavailableReason);
        Assert.IsNull(fixture.ViewModel.GenerateProjectFilesUnavailableReason);
    }

    [TestMethod]
    public async Task TagsAndNotesActionOpensDetailsDirectlyOnThatSectionAsync()
    {
        var fixture = CreateFixture(CreateProject(
            ProjectType.Cpp,
            ProjectState.Available));

        await ExecuteAsync(fixture.ViewModel.TagsAndNotesCommand);

        var details = fixture.DetailsRequests.Single();
        Assert.AreEqual(ProjectDetailsSection.TagsAndNotes, details.SelectedSection);
        Assert.AreEqual(2, details.SelectedTabIndex);
    }

    [TestMethod]
    public async Task SourceControlActionOpensDetailsAndImmediatelyRefreshesGitAsync()
    {
        var fixture = CreateFixture(
            CreateProject(ProjectType.Cpp, ProjectState.Available),
            withGit: true);
        await using var gitStore = fixture.GitStore!;

        await ExecuteAsync(fixture.ViewModel.SourceControlCommand);

        var details = fixture.DetailsRequests.Single();
        Assert.AreEqual(ProjectDetailsSection.SourceControl, details.SelectedSection);
        Assert.AreEqual(3, details.SelectedTabIndex);
        Assert.IsNotNull(details.SourceControl);
        Assert.IsTrue(fixture.GitService!.IncludedRemotes);
        details.Dispose();
    }

    [TestMethod]
    public void CleanupIsExplicitlyOpenedForAvailableBlueprintButNotMissingProject()
    {
        var available = CreateFixture(CreateProject(
            ProjectType.Blueprint,
            ProjectState.Available));
        var missing = CreateFixture(CreateProject(
            ProjectType.Cpp,
            ProjectState.Missing));

        Assert.IsTrue(available.ViewModel.ProjectCleanupCommand.CanExecute(null));
        Assert.IsFalse(missing.ViewModel.ProjectCleanupCommand.CanExecute(null));
        available.ViewModel.ProjectCleanupCommand.Execute(null);

        Assert.HasCount(1, available.CleanupRequests);
        Assert.AreEqual(0, available.CleanupService.CleanupCount);
    }

    [TestMethod]
    public void BlueprintProjectExplainsWhyExistingSolutionCannotOpen()
    {
        var fixture = CreateFixture(CreateProject(
            ProjectType.Blueprint,
            ProjectState.Available));

        Assert.IsFalse(fixture.ViewModel.OpenInVisualStudioCommand.CanExecute(null));
        Assert.IsFalse(fixture.ViewModel.GenerateProjectFilesCommand.CanExecute(null));
        Assert.AreEqual(
            "Existing .sln files can be opened only for C++ projects.",
            fixture.ViewModel.OpenInVisualStudioUnavailableReason);
        Assert.AreEqual(
            "Visual Studio project files can be generated only for C++ projects.",
            fixture.ViewModel.GenerateProjectFilesUnavailableReason);
    }

    [TestMethod]
    [DataRow(
        VisualStudioSolutionState.Missing,
        "No existing .sln file was found. Generate Visual Studio project files to create one.")]
    [DataRow(
        VisualStudioSolutionState.Multiple,
        "Multiple .sln files were found, so no unique solution could be selected.")]
    [DataRow(
        VisualStudioSolutionState.Inaccessible,
        "The project folder could not be inspected for .sln files.")]
    public void UnavailableSolutionStateExplainsWhyExistingSolutionCannotOpen(
        VisualStudioSolutionState solutionState,
        string expectedReason)
    {
        var fixture = CreateFixture(
            CreateProject(ProjectType.Cpp, ProjectState.Available),
            solutionState);

        Assert.IsFalse(fixture.ViewModel.OpenInVisualStudioCommand.CanExecute(null));
        Assert.AreEqual(
            expectedReason,
            fixture.ViewModel.OpenInVisualStudioUnavailableReason);
    }

    [TestMethod]
    public void MissingBrokenAndBlueprintAvailabilityIsSafeAndDeterministic()
    {
        var missing = CreateFixture(CreateProject(ProjectType.Cpp, ProjectState.Missing));
        Assert.IsFalse(missing.ViewModel.OpenProjectCommand.CanExecute(null));
        Assert.IsFalse(missing.ViewModel.OpenInVisualStudioCommand.CanExecute(null));
        Assert.IsTrue(missing.ViewModel.RemoveFromListCommand.CanExecute(null));

        var broken = CreateFixture(CreateProject(ProjectType.Cpp, ProjectState.Broken));
        Assert.IsFalse(broken.ViewModel.OpenProjectCommand.CanExecute(null));
        Assert.IsFalse(broken.ViewModel.RemoveFromListCommand.CanExecute(null));

        var blueprint = CreateFixture(CreateProject(ProjectType.Blueprint, ProjectState.Available));
        Assert.IsFalse(blueprint.ViewModel.OpenInVisualStudioCommand.CanExecute(null));
    }

    [TestMethod]
    public void MissingSolutionRemainsActionableThroughExplicitGenerateConfirmation()
    {
        var fixture = CreateFixture(
            CreateProject(ProjectType.Cpp, ProjectState.Available),
            VisualStudioSolutionState.Missing);

        Assert.IsFalse(fixture.ViewModel.OpenInVisualStudioCommand.CanExecute(null));
        Assert.IsTrue(fixture.ViewModel.GenerateProjectFilesCommand.CanExecute(null));
        Assert.IsEmpty(fixture.GenerationRequests);

        fixture.ViewModel.GenerateProjectFilesCommand.Execute(null);

        Assert.HasCount(1, fixture.GenerationRequests);
        Assert.AreEqual(0, fixture.ProjectFilesGenerator.GenerateCount);
    }

    [TestMethod]
    public async Task SuccessfulGenerationImmediatelyRefreshesOpenSolutionActionAsync()
    {
        var fixture = CreateFixture(
            CreateProject(ProjectType.Cpp, ProjectState.Available),
            VisualStudioSolutionState.Missing);
        fixture.ProjectFilesGenerator.OnGenerate = () =>
            fixture.VisualStudio.SolutionState = VisualStudioSolutionState.Available;
        fixture.ViewModel.GenerateProjectFilesCommand.Execute(null);

        await fixture.GenerationRequests.Single().GenerateCommand.ExecuteAsync();

        Assert.IsTrue(fixture.ViewModel.OpenInVisualStudioCommand.CanExecute(null));
        Assert.IsNull(fixture.ViewModel.OpenInVisualStudioUnavailableReason);
    }

    [TestMethod]
    public async Task SuccessfulGenerationRefreshesLatestDiagnosticsOffCallingThreadAsync()
    {
        var fixture = CreateFixture(
            CreateProject(ProjectType.Cpp, ProjectState.Available),
            VisualStudioSolutionState.Missing);
        await fixture.DiagnosticStore.RefreshAsync(fixture.Project);
        Assert.HasCount(1, fixture.DiagnosticStore.TryGet(fixture.Project)!.Findings);
        fixture.ProjectFilesGenerator.OnGenerate = () =>
            fixture.VisualStudio.SolutionState = VisualStudioSolutionState.Available;
        fixture.ViewModel.GenerateProjectFilesCommand.Execute(null);
        var uiContext = new InlineSynchronizationContext();
        var previousContext = SynchronizationContext.Current;
        fixture.VisualStudio.LastLocateContext = null;

        SynchronizationContext.SetSynchronizationContext(uiContext);
        var execution = fixture.GenerationRequests.Single()
            .GenerateCommand.ExecuteAsync();
        SynchronizationContext.SetSynchronizationContext(previousContext);
        await execution;

        Assert.AreNotSame(
            uiContext,
            fixture.VisualStudio.LastLocateContext);
        Assert.IsEmpty(
            fixture.DiagnosticStore.TryGet(fixture.Project)!.Findings);
    }

    [TestMethod]
    public async Task ProjectDetailsShowsImmediatelyAndRunsSolutionLookupOffCallingThreadAsync()
    {
        var fixture = CreateFixture(
            CreateProject(ProjectType.Cpp, ProjectState.Available),
            VisualStudioSolutionState.Missing);
        var lookupStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLookup = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.VisualStudio.OnLocate = () =>
        {
            lookupStarted.TrySetResult();
            releaseLookup.Task.GetAwaiter().GetResult();
        };
        var uiContext = new InlineSynchronizationContext();
        var previousContext = SynchronizationContext.Current;

        SynchronizationContext.SetSynchronizationContext(uiContext);
        var execution = ((AsyncRelayCommand)fixture.ViewModel.ProjectDetailsCommand)
            .ExecuteAsync();
        SynchronizationContext.SetSynchronizationContext(previousContext);
        await lookupStarted.Task;

        Assert.HasCount(1, fixture.DetailsRequests);
        Assert.AreNotSame(
            uiContext,
            fixture.VisualStudio.LastLocateContext);
        releaseLookup.TrySetResult();
        await execution;
        Assert.HasCount(1, fixture.DetailsRequests[0].Diagnostics.Findings);
    }

    [TestMethod]
    public async Task ClosedProjectDetailsIgnoresLateDiagnosticResultAsync()
    {
        var fixture = CreateFixture(
            CreateProject(ProjectType.Cpp, ProjectState.Available),
            VisualStudioSolutionState.Missing);
        var lookupStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLookup = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.VisualStudio.OnLocate = () =>
        {
            lookupStarted.TrySetResult();
            releaseLookup.Task.GetAwaiter().GetResult();
        };

        var execution = ((AsyncRelayCommand)fixture.ViewModel.ProjectDetailsCommand)
            .ExecuteAsync();
        await lookupStarted.Task;
        var details = fixture.DetailsRequests.Single();
        details.Dispose();
        await execution.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsEmpty(details.Diagnostics.Findings);
        Assert.IsTrue(details.Diagnostics.HasNoFindings);
        releaseLookup.TrySetResult();
    }

    [TestMethod]
    public async Task CommandsDelegateToOneActionServiceAndInformationUsesPresentationCallbackAsync()
    {
        var fixture = CreateFixture(CreateProject(ProjectType.Cpp, ProjectState.Available));

        await ExecuteAsync(fixture.ViewModel.ToggleFavoriteCommand);
        await ExecuteAsync(fixture.ViewModel.OpenProjectCommand);
        fixture.ViewModel.OpenProjectFolderCommand.Execute(null);
        fixture.ViewModel.CopyPathCommand.Execute(null);
        fixture.ViewModel.OpenInVisualStudioCommand.Execute(null);
        fixture.ViewModel.ProjectDetailsCommand.Execute(null);

        Assert.AreEqual(1, fixture.UnrealLauncher.LaunchCount);
        Assert.AreEqual(1, fixture.Explorer.FolderCount);
        Assert.AreEqual(fixture.Project.ProjectFilePath.Value, fixture.Clipboard.Text);
        Assert.AreEqual(1, fixture.VisualStudio.OpenCount);
        Assert.HasCount(1, fixture.DetailsRequests);
        Assert.AreEqual(fixture.Project.Name, fixture.DetailsRequests[0].Name);
        Assert.AreEqual(
            fixture.Project.ProjectFilePath.Value,
            fixture.DetailsRequests[0].Overview.ProjectPath);
    }

    [TestMethod]
    public async Task RemoveOccursOnlyWhenExplicitMissingCommandExecutesAsync()
    {
        var project = CreateProject(ProjectType.Cpp, ProjectState.Missing);
        var fixture = CreateFixture(project);

        Assert.HasCount(1, fixture.Catalog.GetSnapshot().Projects);

        await ExecuteAsync(fixture.ViewModel.RemoveFromListCommand);

        Assert.IsEmpty(fixture.Catalog.GetSnapshot().Projects);
    }

    [TestMethod]
    public void ProjectOverviewUsesQuietUnknownsAndExactInjectedLocalTime()
    {
        var project = CreateProject(
            ProjectType.Blueprint,
            ProjectState.Broken,
            lastLaunched: null,
            engineAssociation: null,
            engineDisplayVersion: null);

        var overview = new ProjectOverviewViewModel(project, TimeZoneInfo.Utc);

        Assert.AreEqual("—", overview.EngineAssociation);
        Assert.AreEqual("—", overview.EngineDisplayVersion);
        Assert.AreEqual("—", overview.ProjectType);
        Assert.AreEqual("2026-08-28 11:00:00 +00:00", overview.LastModified);
        Assert.AreEqual("Never", overview.LastLaunched);
        Assert.AreEqual("Broken", overview.ProjectState);
    }

    private static async Task ExecuteAsync(ICommand command)
    {
        if (command is UProjectHub.App.Infrastructure.AsyncRelayCommand asyncCommand)
        {
            await asyncCommand.ExecuteAsync();
            return;
        }

        command.Execute(null);
    }

    private static Fixture CreateFixture(
        UnrealProject project,
        VisualStudioSolutionState solutionState =
            VisualStudioSolutionState.Available,
        bool withGit = false)
    {
        var catalog = new ProjectCatalog();
        catalog.Upsert(project);
        var settings = new FakeSettingsRepository(new AppSettings
        {
            ProjectUserStates =
            [
                new ProjectUserState(project.ProjectFilePath, project.IsFavorite, project.LastLaunched),
            ],
        });
        var cache = new FakeProjectCacheRepository();
        var removal = new ManagedProjectRemovalService(
            catalog,
            cache,
            settings,
            new ProjectCatalogOperationGate());
        var unreal = new FakeUnrealEditorLauncher();
        var explorer = new FakeExplorerLauncher();
        var visualStudio = new FakeVisualStudioLauncher(solutionState);
        var projectFilesGenerator = new FakeProjectFilesGenerator();
        var clipboard = new FakeClipboardService();
        var resolution = EngineResolver.Resolve("5.8",
        [
            new InstalledEngine(
                "UE 5.8",
                "5.8",
                "5.8.1",
                @"C:\UE",
                @"C:\UE\UnrealEditor.exe",
                EngineSource.Manual,
                true),
        ]);
        var actions = new ProjectActionService(
            catalog,
            new SettingsMutationService(settings),
            removal,
            unreal,
            explorer,
            visualStudio,
            clipboard,
            _ => resolution,
            projectFilesGenerator: projectFilesGenerator);
        var diagnosticStore = new ProjectDiagnosticSnapshotStore(
            new ProjectDiagnosticsService(
                new BasicProjectDiagnosticsService(new FakeClock(Now)),
                visualStudio,
                _ => true));
        diagnosticStore.Prune([project]);
        var detailsRequests = new List<ProjectDetailsViewModel>();
        var generationRequests = new List<GenerateProjectFilesViewModel>();
        var cleanupRequests = new List<ProjectCleanupViewModel>();
        var cleanupService = new FakeCleanupService(project);
        var gitService = withGit ? new FakeGitStatusService() : null;
        var gitStore = gitService is null
            ? null
            : new ProjectGitStatusStore(gitService, new ImmediateDispatcher());
        if (gitStore is not null)
        {
            _ = gitStore.UpdateCatalog([project]);
        }
        var viewModel = new ProjectContextActionsViewModel(
            project,
            actions,
            detailsRequests.Add,
            generationRequests.Add,
            cleanupRequests.Add,
            cleanupService,
            diagnostics: diagnosticStore,
            gitStatuses: gitStore,
            webUrlLauncher: withGit ? new FakeWebUrlLauncher() : null);
        return new Fixture(
            project,
            viewModel,
            catalog,
            unreal,
            explorer,
            visualStudio,
            clipboard,
            detailsRequests,
            diagnosticStore,
            projectFilesGenerator,
            generationRequests,
            cleanupService,
            cleanupRequests,
            gitStore,
            gitService);
    }

    private static UnrealProject CreateProject(
        ProjectType projectType,
        ProjectState projectState,
        DateTimeOffset? lastLaunched = null,
        string? engineAssociation = "5.8",
        string? engineDisplayVersion = "5.8.1") =>
        new(
            "Game",
            new ProjectPath(@"D:\Projects\Game\Game.uproject"),
            engineAssociation,
            engineDisplayVersion,
            projectType,
            Now.AddHours(-1),
            lastLaunched,
            IsFavorite: false,
            projectState,
            EngineResolutionState.Resolved);

    private sealed record Fixture(
        UnrealProject Project,
        ProjectContextActionsViewModel ViewModel,
        ProjectCatalog Catalog,
        FakeUnrealEditorLauncher UnrealLauncher,
        FakeExplorerLauncher Explorer,
        FakeVisualStudioLauncher VisualStudio,
        FakeClipboardService Clipboard,
        List<ProjectDetailsViewModel> DetailsRequests,
        ProjectDiagnosticSnapshotStore DiagnosticStore,
        FakeProjectFilesGenerator ProjectFilesGenerator,
        List<GenerateProjectFilesViewModel> GenerationRequests,
        FakeCleanupService CleanupService,
        List<ProjectCleanupViewModel> CleanupRequests,
        ProjectGitStatusStore? GitStore,
        FakeGitStatusService? GitService);

    private sealed class FakeSettingsRepository(AppSettings settings) : ISettingsRepository
    {
        private AppSettings _settings = settings;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProjectCacheRepository : IProjectCacheRepository
    {
        public Task<ProjectCacheDocument> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectCacheDocument());

        public Task SaveAsync(ProjectCacheDocument document, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeUnrealEditorLauncher : IUnrealEditorLauncher
    {
        public int LaunchCount { get; private set; }

        public LaunchResult Launch(UnrealProject project, EngineResolution engineResolution)
        {
            LaunchCount++;
            return LaunchResult.Succeeded(Now);
        }

        public LaunchResult LaunchNewProject(InstalledEngine engine) =>
            throw new InvalidOperationException("New-project launch was not expected.");
    }

    private sealed class FakeExplorerLauncher : IExplorerLauncher
    {
        public int FolderCount { get; private set; }

        public LaunchResult OpenProjectFolder(UnrealProject project)
        {
            FolderCount++;
            return LaunchResult.Succeeded();
        }
    }

    private sealed class FakeVisualStudioLauncher(
        VisualStudioSolutionState solutionState)
        : IVisualStudioLauncher, IVisualStudioSolutionLocator
    {
        public VisualStudioSolutionState SolutionState { get; set; } = solutionState;

        public int OpenCount { get; private set; }

        public SynchronizationContext? LastLocateContext { get; set; }

        public Action? OnLocate { get; set; }

        public bool CanOpenSolution(UnrealProject project) =>
            project.ProjectType == ProjectType.Cpp
            && SolutionState == VisualStudioSolutionState.Available;

        public VisualStudioSolutionSelection LocateSolution(
            UnrealProject project) => Locate(project);

        public VisualStudioSolutionSelection Locate(UnrealProject project)
        {
            LastLocateContext = SynchronizationContext.Current;
            OnLocate?.Invoke();
            return SolutionState switch
            {
                VisualStudioSolutionState.Available =>
                    VisualStudioSolutionSelection.Available(
                        @"D:\Projects\Game\Game.sln",
                        [@"D:\Projects\Game\Game.sln"]),
                VisualStudioSolutionState.Missing =>
                    VisualStudioSolutionSelection.Missing(),
                VisualStudioSolutionState.Multiple =>
                    VisualStudioSolutionSelection.Multiple(
                    [
                        @"D:\Projects\Game\Game.sln",
                        @"D:\Projects\Game\Tools.sln",
                    ]),
                VisualStudioSolutionState.Inaccessible =>
                    VisualStudioSolutionSelection.Inaccessible("Access denied."),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(solutionState),
                    SolutionState,
                    null),
            };
        }

        public LaunchResult OpenSolution(UnrealProject project)
        {
            OpenCount++;
            return LaunchResult.Succeeded();
        }
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public string? Text { get; private set; }

        public void SetText(string text)
        {
            Text = text;
        }
    }

    private sealed class InlineSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) =>
            callback(state);
    }

    private sealed class FakeProjectFilesGenerator : IProjectFilesGenerator
    {
        public Action? OnGenerate { get; set; }

        public int GenerateCount { get; private set; }

        public ProjectFileGenerationPreparation Prepare(
            UnrealProject project,
            InstalledEngine engine) =>
            ProjectFileGenerationPreparation.Available(
                new ProjectFileGenerationRequest(
                    project,
                    engine,
                    new ExternalProcessRequest(
                        @"C:\UE\UnrealBuildTool.exe",
                        ["-ProjectFiles"]),
                    @"D:\Projects\Game\Game.sln"));

        public Task<ProjectFileGenerationResult> GenerateAsync(
            ProjectFileGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            GenerateCount++;
            OnGenerate?.Invoke();
            return Task.FromResult(new ProjectFileGenerationResult(
                ProjectFileGenerationStatus.Succeeded,
                ExitCode: 0,
                StandardOutputTail: string.Empty,
                StandardErrorTail: string.Empty,
                ErrorMessage: null,
                VisualStudioSolutionSelection.Available(
                    request.ExpectedSolutionPath,
                    [request.ExpectedSolutionPath])));
        }
    }

    private sealed class FakeCleanupService(UnrealProject project) : IProjectCleanupService
    {
        public int CleanupCount { get; private set; }

        public Task<ProjectCleanupInspection> InspectAsync(
            UnrealProject requestedProject,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectCleanupInspection(project,
            [
                new ProjectCleanupItemInspection(
                    ProjectCleanupTargetKind.Intermediate,
                    Path.Combine(project.ProjectDirectory, "Intermediate"),
                    Exists: false,
                    CanDelete: false,
                    FileSizeBytes: 0,
                    ErrorMessage: null,
                    CandidatePaths: Array.Empty<string>()),
            ]));

        public Task<ProjectCleanupResult> CleanupAsync(
            ProjectCleanupRequest request,
            CancellationToken cancellationToken = default)
        {
            CleanupCount++;
            return Task.FromResult(new ProjectCleanupResult(
                Array.Empty<ProjectCleanupItemResult>()));
        }
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGitStatusService : IGitStatusService
    {
        public bool IncludedRemotes { get; private set; }

        public Task<GitProjectStatus> GetStatusAsync(
            string projectDirectory,
            bool includeRemotes = false,
            CancellationToken cancellationToken = default)
        {
            IncludedRemotes |= includeRemotes;
            return Task.FromResult(new GitProjectStatus(GitProjectState.Clean));
        }
    }

    private sealed class FakeWebUrlLauncher : IWebUrlLauncher
    {
        public LaunchResult Open(string url) => LaunchResult.Succeeded();
    }
}
