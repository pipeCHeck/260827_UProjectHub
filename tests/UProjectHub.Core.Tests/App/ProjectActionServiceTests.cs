using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Engines;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Filtering;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Searching;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;
using UProjectHub.Core.Tests.Time;
using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ProjectActionServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 10, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task FailedUnrealLaunch_IsLoggedWithoutChangingActionSemantics()
    {
        var project = CreateProject();
        var fixture = CreateFixture(
            project,
            unrealLaunchResult: LaunchResult.Failed("editor unavailable"));

        var result = await fixture.Service.OpenProjectAsync(project);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(1, fixture.UnrealLauncher.LaunchCount);
        Assert.IsTrue(fixture.Logger.Messages.Any(message =>
            message.Contains("editor unavailable", StringComparison.Ordinal)));
        Assert.IsEmpty(fixture.Settings.SaveCalls);
    }

    [TestMethod]
    public async Task ToggleFavoritePersistsUserStatePreservesSettingsAndPublishesOneCatalogChangeAsync()
    {
        var project = CreateProject(isFavorite: false, lastLaunched: Now.AddDays(-1));
        var fixture = CreateFixture(project, settings: CreateSettings(
            project,
            isFavorite: false,
            lastLaunched: Now.AddDays(-1)));
        var snapshots = new List<ProjectCatalogSnapshot>();
        fixture.Service.CatalogChanged += snapshots.Add;

        var result = await fixture.Service.ToggleFavoriteAsync(project);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, fixture.Settings.SaveCalls);
        var saved = fixture.Settings.Current;
        Assert.HasCount(1, saved.ProjectUserStates);
        Assert.IsTrue(saved.ProjectUserStates[0].IsFavorite);
        Assert.AreEqual(Now.AddDays(-1), saved.ProjectUserStates[0].LastLaunched);
        AssertSettingsPreferencesPreserved(fixture.InitialSettings, saved);
        Assert.IsTrue(GetCatalogProject(fixture.Catalog).IsFavorite);
        Assert.HasCount(1, snapshots);

        var secondResult = await fixture.Service.ToggleFavoriteAsync(
            GetCatalogProject(fixture.Catalog));

        Assert.IsTrue(secondResult.IsSuccess);
        Assert.IsFalse(fixture.Settings.Current.ProjectUserStates[0].IsFavorite);
        Assert.HasCount(2, snapshots);
    }

    [TestMethod]
    public async Task FavoriteAndLaunchHistoryWritesPreserveTagsAndNoteAsync()
    {
        var project = CreateProject() with
        {
            Tags = ["Client", "VR"],
            Note = "Do not overwrite this note.",
        };
        var fixture = CreateFixture(project);

        var favoriteResult = await fixture.Service.ToggleFavoriteAsync(project);
        var launchResult = await fixture.Service.OpenProjectAsync(
            GetCatalogProject(fixture.Catalog));

        Assert.IsTrue(favoriteResult.IsSuccess);
        Assert.IsTrue(launchResult.IsSuccess);
        var state = fixture.Settings.Current.ProjectUserStates.Single();
        CollectionAssert.AreEqual(new[] { "Client", "VR" }, state.Tags.ToArray());
        Assert.AreEqual("Do not overwrite this note.", state.Note);
        var current = GetCatalogProject(fixture.Catalog);
        CollectionAssert.AreEqual(new[] { "Client", "VR" }, current.Tags.ToArray());
        Assert.AreEqual("Do not overwrite this note.", current.Note);
    }

    [TestMethod]
    public async Task FavoriteSaveFailureDoesNotMutateCatalogOrPublishCatalogChangeAsync()
    {
        var project = CreateProject(isFavorite: false);
        var fixture = CreateFixture(project);
        fixture.Settings.SaveException = new IOException("settings unavailable");
        var catalogChangedCount = 0;
        fixture.Service.CatalogChanged += _ => catalogChangedCount++;

        var result = await fixture.Service.ToggleFavoriteAsync(project);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(GetCatalogProject(fixture.Catalog).IsFavorite);
        Assert.AreEqual(0, catalogChangedCount);
    }

    [TestMethod]
    public async Task UnfavoriteImmediatelyReappliesFavoritesOnlyViewAsync()
    {
        var project = CreateProject(isFavorite: true);
        var fixture = CreateFixture(
            project,
            settings: CreateSettings(project, isFavorite: true));
        var projectList = new ProjectListViewModel();
        var search = new SearchFilterViewModel(
            projectList,
            new ProjectQueryParser(),
            new ProjectFilterService(new ProjectSearchService(new FakeClock(Now))),
            new ProjectSortService());
        var main = new MainViewModel(
            new StatusBarViewModel(),
            projectList: projectList,
            searchFilter: search);
        fixture.Service.CatalogChanged += main.SetProjects;
        main.SetProjects(fixture.Catalog.GetSnapshot());
        search.FavoritesOnly = true;
        Assert.AreEqual(1, projectList.VisibleCount);

        var result = await fixture.Service.ToggleFavoriteAsync(project);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, main.ProjectCount);
        Assert.AreEqual(0, projectList.VisibleCount);
        Assert.AreEqual("Showing 0 of 1", projectList.ShowingCountText);
    }

    [TestMethod]
    public async Task SuccessfulLaunchPersistsTimestampPreservesFavoriteAndPublishesOneChangeAsync()
    {
        var previousLaunch = Now.AddDays(-3);
        var project = CreateProject(isFavorite: true, lastLaunched: previousLaunch);
        var fixture = CreateFixture(
            project,
            settings: CreateSettings(project, isFavorite: true, lastLaunched: previousLaunch),
            unrealLaunchResult: LaunchResult.Succeeded(Now));
        var catalogChangedCount = 0;
        fixture.Service.CatalogChanged += _ => catalogChangedCount++;

        var result = await fixture.Service.OpenProjectAsync(project);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, fixture.UnrealLauncher.LaunchCount);
        Assert.AreSame(fixture.Resolution, fixture.UnrealLauncher.LastResolution);
        var state = fixture.Settings.Current.ProjectUserStates.Single();
        Assert.IsTrue(state.IsFavorite);
        Assert.AreEqual(Now, state.LastLaunched);
        Assert.AreEqual(Now, GetCatalogProject(fixture.Catalog).LastLaunched);
        Assert.AreEqual("5.8", GetCatalogProject(fixture.Catalog).EngineAssociation);
        Assert.AreEqual(1, catalogChangedCount);
    }

    [TestMethod]
    public async Task SuccessfulLaunchReappliesActiveLastLaunchedSortAsync()
    {
        var launchedProject = CreateProject(lastLaunched: Now.AddDays(-4));
        var otherProject = launchedProject with
        {
            Name = "Other",
            ProjectFilePath = new ProjectPath(@"D:\Projects\Other\Other.uproject"),
            LastLaunched = Now.AddDays(-1),
        };
        var fixture = CreateFixture(
            launchedProject,
            unrealLaunchResult: LaunchResult.Succeeded(Now));
        fixture.Catalog.Upsert(otherProject);
        var projectList = new ProjectListViewModel();
        var search = new SearchFilterViewModel(
            projectList,
            new ProjectQueryParser(),
            new ProjectFilterService(new ProjectSearchService(new FakeClock(Now))),
            new ProjectSortService());
        search.ApplySettings(new AppSettings
        {
            ActiveSort = new ProjectSortDefinition(
                ProjectSortColumn.LastLaunched,
                SortDirection.Descending),
        });
        var main = new MainViewModel(
            new StatusBarViewModel(),
            projectList: projectList,
            searchFilter: search);
        fixture.Service.CatalogChanged += main.SetProjects;
        main.SetProjects(fixture.Catalog.GetSnapshot());
        CollectionAssert.AreEqual(
            new[] { "Other", "Game" },
            projectList.Rows.Select(row => row.Name).ToArray());

        var result = await fixture.Service.OpenProjectAsync(launchedProject);

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { "Game", "Other" },
            projectList.Rows.Select(row => row.Name).ToArray());
    }

    [TestMethod]
    public async Task LaunchFailureOrPersistenceFailureDoesNotPublishCatalogChangeAsync()
    {
        var project = CreateProject(lastLaunched: Now.AddDays(-2));
        var failedLaunch = CreateFixture(
            project,
            unrealLaunchResult: LaunchResult.Failed("launch failed"));
        var failedLaunchChanges = 0;
        failedLaunch.Service.CatalogChanged += _ => failedLaunchChanges++;

        var failedLaunchResult = await failedLaunch.Service.OpenProjectAsync(project);

        Assert.IsFalse(failedLaunchResult.IsSuccess);
        Assert.HasCount(0, failedLaunch.Settings.SaveCalls);
        Assert.AreEqual(Now.AddDays(-2), GetCatalogProject(failedLaunch.Catalog).LastLaunched);
        Assert.AreEqual(0, failedLaunchChanges);

        var failedSave = CreateFixture(
            project,
            unrealLaunchResult: LaunchResult.Succeeded(Now));
        failedSave.Settings.SaveException = new IOException("settings unavailable");
        var failedSaveChanges = 0;
        failedSave.Service.CatalogChanged += _ => failedSaveChanges++;

        var failedSaveResult = await failedSave.Service.OpenProjectAsync(project);

        Assert.IsFalse(failedSaveResult.IsSuccess);
        Assert.AreEqual(1, failedSave.UnrealLauncher.LaunchCount);
        Assert.AreEqual(Now.AddDays(-2), GetCatalogProject(failedSave.Catalog).LastLaunched);
        Assert.AreEqual(0, failedSaveChanges);
    }

    [TestMethod]
    public async Task UnavailableProjectsAndResolutionsDoNotCallLauncherOrPublishChangesAsync()
    {
        foreach (var project in new[]
                 {
                     CreateProject(projectState: ProjectState.Missing),
                     CreateProject(projectState: ProjectState.Broken),
                 })
        {
            var fixture = CreateFixture(project);
            var changes = 0;
            fixture.Service.CatalogChanged += _ => changes++;

            var result = await fixture.Service.OpenProjectAsync(project);

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(0, fixture.UnrealLauncher.LaunchCount);
            Assert.AreEqual(0, changes);
        }

        var available = CreateProject();
        var matchingEngine = new InstalledEngine(
            "UE 5.8 A",
            "5.8",
            "5.8.1",
            @"C:\UE\A",
            @"C:\UE\A\UnrealEditor.exe",
            EngineSource.Manual,
            true);
        var resolutions = new[]
        {
            EngineResolver.Resolve("5.8", []),
            EngineResolver.Resolve("5.8",
            [
                matchingEngine,
                matchingEngine with
                {
                    DisplayName = "UE 5.8 B",
                    RootPath = @"C:\UE\B",
                    EditorPath = @"C:\UE\B\UnrealEditor.exe",
                },
            ]),
            EngineResolver.Resolve(null, []),
        };
        foreach (var resolution in resolutions)
        {
            var unresolved = CreateFixture(available, resolution: resolution);
            var unresolvedChanges = 0;
            unresolved.Service.CatalogChanged += _ => unresolvedChanges++;

            var unresolvedResult = await unresolved.Service.OpenProjectAsync(available);

            Assert.IsFalse(unresolvedResult.IsSuccess);
            Assert.AreEqual(0, unresolved.UnrealLauncher.LaunchCount);
            Assert.AreEqual(0, unresolvedChanges);
        }
    }

    [TestMethod]
    public void FolderCopyAndVisualStudioDelegateWithoutChangingCatalog()
    {
        var project = CreateProject(projectType: ProjectType.Cpp);
        var fixture = CreateFixture(project);
        var catalogChangedCount = 0;
        fixture.Service.CatalogChanged += _ => catalogChangedCount++;

        var folder = fixture.Service.OpenProjectFolder(project);
        var copy = fixture.Service.CopyProjectPath(project);
        var visualStudio = fixture.Service.OpenInVisualStudio(project);

        Assert.IsTrue(folder.IsSuccess);
        Assert.IsTrue(copy.IsSuccess);
        Assert.IsTrue(visualStudio.IsSuccess);
        Assert.AreSame(project, fixture.Explorer.LastFolderProject);
        Assert.AreEqual(project.ProjectFilePath.Value, fixture.Clipboard.Text);
        Assert.AreSame(project, fixture.VisualStudio.LastOpenedProject);
        Assert.AreEqual(0, catalogChangedCount);
    }

    [TestMethod]
    public void GeneratePreparationRequiresAvailableCppProjectAndUniqueUsableEngine()
    {
        var generator = new FakeProjectFilesGenerator();
        var cpp = CreateProject(projectType: ProjectType.Cpp);
        var available = CreateFixture(cpp, projectFilesGenerator: generator);

        var preparation = available.Service.PrepareProjectFileGeneration(cpp);

        Assert.IsTrue(preparation.CanGenerate);
        Assert.IsNotNull(preparation.Request);
        Assert.AreEqual(1, generator.PrepareCount);

        var blueprint = CreateProject(projectType: ProjectType.Blueprint);
        var blueprintFixture = CreateFixture(
            blueprint,
            projectFilesGenerator: generator);
        Assert.IsFalse(blueprintFixture.Service
            .PrepareProjectFileGeneration(blueprint).CanGenerate);

        var missing = CreateProject(
            projectType: ProjectType.Cpp,
            projectState: ProjectState.Missing);
        var missingFixture = CreateFixture(
            missing,
            projectFilesGenerator: generator);
        Assert.IsFalse(missingFixture.Service
            .PrepareProjectFileGeneration(missing).CanGenerate);

        var unresolved = CreateFixture(
            cpp,
            resolution: EngineResolver.Resolve("5.8", []),
            projectFilesGenerator: generator);
        Assert.IsFalse(unresolved.Service
            .PrepareProjectFileGeneration(cpp).CanGenerate);

        var unusableEngine = CreateResolvedEngine(isUsable: false);
        var unusable = CreateFixture(
            cpp,
            resolution: unusableEngine,
            projectFilesGenerator: generator);
        Assert.IsFalse(unusable.Service
            .PrepareProjectFileGeneration(cpp).CanGenerate);
    }

    [TestMethod]
    public void GeneratorSpecificUnavailabilityIsPreserved()
    {
        var project = CreateProject(projectType: ProjectType.Cpp);
        var generator = new FakeProjectFilesGenerator
        {
            Preparation = ProjectFileGenerationPreparation.Unavailable(
                "UnrealBuildTool is unavailable."),
        };
        var fixture = CreateFixture(
            project,
            projectFilesGenerator: generator);

        var preparation = fixture.Service.PrepareProjectFileGeneration(project);

        Assert.IsFalse(preparation.CanGenerate);
        Assert.AreEqual(
            "UnrealBuildTool is unavailable.",
            preparation.UnavailableReason);
    }

    [TestMethod]
    public async Task ExplicitGenerateDelegatesPreparedRequestOnlyWhenInvokedAsync()
    {
        var project = CreateProject(projectType: ProjectType.Cpp);
        var generator = new FakeProjectFilesGenerator();
        var fixture = CreateFixture(
            project,
            projectFilesGenerator: generator);
        var preparation = fixture.Service.PrepareProjectFileGeneration(project);

        Assert.AreEqual(0, generator.GenerateCount);

        var result = await fixture.Service.GenerateProjectFilesAsync(
            preparation.Request!);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, generator.GenerateCount);
        Assert.AreSame(preparation.Request, generator.LastRequest);
    }

    [TestMethod]
    public async Task MissingRemovalPublishesOnceWhileRejectedRemovalPublishesNeverAndTouchesNoFilesAsync()
    {
        using var workspace = TemporaryWorkspace.Create();
        var missing = CreateProject(
            projectPath: workspace.ProjectPath,
            projectState: ProjectState.Missing);
        var missingFixture = CreateFixture(missing);
        var missingChanges = 0;
        missingFixture.Service.CatalogChanged += _ => missingChanges++;

        var removed = await missingFixture.Service.RemoveMissingAsync(missing);

        Assert.IsTrue(removed.IsSuccess);
        Assert.IsEmpty(missingFixture.Catalog.GetSnapshot().Projects);
        Assert.AreEqual(1, missingChanges);
        Assert.IsTrue(File.Exists(workspace.ProjectPath.Value));
        Assert.IsTrue(File.Exists(workspace.KeepFilePath));

        var available = CreateProject(projectState: ProjectState.Available);
        var availableFixture = CreateFixture(available);
        var rejectedChanges = 0;
        availableFixture.Service.CatalogChanged += _ => rejectedChanges++;

        var rejected = await availableFixture.Service.RemoveMissingAsync(available);

        Assert.IsFalse(rejected.IsSuccess);
        Assert.HasCount(1, availableFixture.Catalog.GetSnapshot().Projects);
        Assert.AreEqual(0, rejectedChanges);
    }

    private static Fixture CreateFixture(
        UnrealProject project,
        AppSettings? settings = null,
        LaunchResult? unrealLaunchResult = null,
        EngineResolution? resolution = null,
        IProjectFilesGenerator? projectFilesGenerator = null)
    {
        var catalog = new ProjectCatalog();
        catalog.Upsert(project);
        var settingsRepository = new FakeSettingsRepository(
            settings ?? CreateSettings(project, project.IsFavorite, project.LastLaunched));
        var settingsMutations = new SettingsMutationService(settingsRepository);
        var cacheRepository = new FakeProjectCacheRepository();
        var removal = new ManagedProjectRemovalService(
            catalog,
            cacheRepository,
            settingsMutations);
        var unrealLauncher = new FakeUnrealEditorLauncher(
            unrealLaunchResult ?? LaunchResult.Succeeded(Now));
        var explorer = new FakeExplorerLauncher();
        var visualStudio = new FakeVisualStudioLauncher(canOpen: true);
        var clipboard = new FakeClipboardService();
        var logger = new RecordingLogger();
        var resolved = resolution ?? CreateResolvedEngine();
        var service = new ProjectActionService(
            catalog,
            settingsMutations,
            removal,
            unrealLauncher,
            explorer,
            visualStudio,
            clipboard,
            _ => resolved,
            logger,
            projectFilesGenerator);

        return new Fixture(
            service,
            catalog,
            settingsRepository,
            unrealLauncher,
            explorer,
            visualStudio,
            clipboard,
            logger,
            resolved,
            settingsRepository.Current);
    }

    private static EngineResolution CreateResolvedEngine(bool isUsable = true)
    {
        var engine = new InstalledEngine(
            "Unreal Engine 5.8",
            "5.8",
            "5.8.1",
            @"C:\UE\5.8",
            @"C:\UE\5.8\Engine\Binaries\Win64\UnrealEditor.exe",
            EngineSource.Launcher,
            IsUsable: isUsable);
        return EngineResolver.Resolve("5.8", [engine]);
    }

    private static AppSettings CreateSettings(
        UnrealProject project,
        bool isFavorite = false,
        DateTimeOffset? lastLaunched = null) =>
        new()
        {
            ProjectSearchRoots = [@"D:\Projects"],
            ManualEngineRoots = [@"D:\UE"],
            ProjectUserStates =
            [
                new ProjectUserState(project.ProjectFilePath, isFavorite, lastLaunched)
                {
                    Tags = project.Tags,
                    Note = project.Note,
                },
            ],
            ThemeMode = ThemeMode.Dark,
            RowDensity = RowDensity.Compact,
            ActiveSort = new ProjectSortDefinition(
                ProjectSortColumn.Name,
                SortDirection.Descending),
            VisibleFilters = new VisibleFilterState(
                Engine: "5.8",
                ProjectType: ProjectType.Cpp,
                FavoritesOnly: false),
            ColumnLayout = [new ColumnLayoutState("Name", true, 320)],
        };

    private static void AssertSettingsPreferencesPreserved(
        AppSettings expected,
        AppSettings actual)
    {
        CollectionAssert.AreEqual(expected.ProjectSearchRoots.ToArray(), actual.ProjectSearchRoots.ToArray());
        CollectionAssert.AreEqual(expected.ManualEngineRoots.ToArray(), actual.ManualEngineRoots.ToArray());
        Assert.AreEqual(expected.ThemeMode, actual.ThemeMode);
        Assert.AreEqual(expected.RowDensity, actual.RowDensity);
        Assert.AreEqual(expected.ActiveSort, actual.ActiveSort);
        Assert.AreEqual(expected.VisibleFilters, actual.VisibleFilters);
        CollectionAssert.AreEqual(expected.ColumnLayout.ToArray(), actual.ColumnLayout.ToArray());
    }

    private static UnrealProject GetCatalogProject(ProjectCatalog catalog) =>
        catalog.GetSnapshot().Projects.Single();

    private static UnrealProject CreateProject(
        ProjectPath? projectPath = null,
        bool isFavorite = false,
        DateTimeOffset? lastLaunched = null,
        ProjectType projectType = ProjectType.Cpp,
        ProjectState projectState = ProjectState.Available) =>
        new(
            "Game",
            projectPath ?? new ProjectPath(@"D:\Projects\Game\Game.uproject"),
            "5.8",
            "5.8.1",
            projectType,
            Now.AddDays(-1),
            lastLaunched,
            isFavorite,
            projectState,
            EngineResolutionState.Resolved);

    private sealed record Fixture(
        ProjectActionService Service,
        ProjectCatalog Catalog,
        FakeSettingsRepository Settings,
        FakeUnrealEditorLauncher UnrealLauncher,
        FakeExplorerLauncher Explorer,
        FakeVisualStudioLauncher VisualStudio,
        FakeClipboardService Clipboard,
        RecordingLogger Logger,
        EngineResolution Resolution,
        AppSettings InitialSettings);

    private sealed class FakeSettingsRepository(AppSettings settings) : ISettingsRepository
    {
        public AppSettings Current { get; private set; } = settings;

        public Exception? SaveException { get; set; }

        public List<AppSettings> SaveCalls { get; } = [];

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            if (SaveException is not null)
            {
                return Task.FromException(SaveException);
            }

            SaveCalls.Add(settings);
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProjectCacheRepository : IProjectCacheRepository
    {
        private ProjectCacheDocument _document = new();

        public Task<ProjectCacheDocument> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_document);

        public Task SaveAsync(ProjectCacheDocument document, CancellationToken cancellationToken = default)
        {
            _document = document;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnrealEditorLauncher(LaunchResult result) : IUnrealEditorLauncher
    {
        public int LaunchCount { get; private set; }

        public EngineResolution? LastResolution { get; private set; }

        public LaunchResult Launch(UnrealProject project, EngineResolution engineResolution)
        {
            LaunchCount++;
            LastResolution = engineResolution;
            return result;
        }

        public LaunchResult LaunchNewProject(InstalledEngine engine) =>
            throw new InvalidOperationException("New-project launch was not expected.");
    }

    private sealed class FakeExplorerLauncher : IExplorerLauncher
    {
        public UnrealProject? LastFolderProject { get; private set; }

        public LaunchResult OpenProjectFolder(UnrealProject project)
        {
            LastFolderProject = project;
            return LaunchResult.Succeeded();
        }
    }

    private sealed class FakeVisualStudioLauncher(bool canOpen) : IVisualStudioLauncher
    {
        public UnrealProject? LastOpenedProject { get; private set; }

        public bool CanOpenSolution(UnrealProject project) => canOpen;

        public VisualStudioSolutionSelection LocateSolution(
            UnrealProject project) =>
            canOpen
                ? VisualStudioSolutionSelection.Available(
                    @"D:\Projects\Game\Game.sln",
                    [@"D:\Projects\Game\Game.sln"])
                : VisualStudioSolutionSelection.Missing();

        public LaunchResult OpenSolution(UnrealProject project)
        {
            LastOpenedProject = project;
            return canOpen
                ? LaunchResult.Succeeded()
                : LaunchResult.Failed("No solution available.");
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

    private sealed class FakeProjectFilesGenerator : IProjectFilesGenerator
    {
        public ProjectFileGenerationPreparation? Preparation { get; set; }

        public int PrepareCount { get; private set; }

        public int GenerateCount { get; private set; }

        public ProjectFileGenerationRequest? LastRequest { get; private set; }

        public ProjectFileGenerationPreparation Prepare(
            UnrealProject project,
            InstalledEngine engine)
        {
            PrepareCount++;
            return Preparation ?? ProjectFileGenerationPreparation.Available(
                new ProjectFileGenerationRequest(
                    project,
                    engine,
                    new ExternalProcessRequest(
                        @"C:\UE\UnrealBuildTool.exe",
                        ["-ProjectFiles"]),
                    @"D:\Projects\Game\Game.sln"));
        }

        public Task<ProjectFileGenerationResult> GenerateAsync(
            ProjectFileGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            GenerateCount++;
            LastRequest = request;
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

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Messages { get; } = [];

        public void Info(string message) => Messages.Add(message);

        public void Warning(string message) => Messages.Add(message);

        public void Error(string message) => Messages.Add(message);

        public void Error(string message, Exception exception) =>
            Messages.Add($"{message} {exception.Message}");
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string path, ProjectPath projectPath, string keepFilePath)
        {
            Path = path;
            ProjectPath = projectPath;
            KeepFilePath = keepFilePath;
        }

        public string Path { get; }

        public ProjectPath ProjectPath { get; }

        public string KeepFilePath { get; }

        public static TemporaryWorkspace Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "UProjectHub.Tests",
                "ProjectActionService",
                Guid.NewGuid().ToString("N"));
            var content = System.IO.Path.Combine(path, "Content");
            Directory.CreateDirectory(content);
            var projectFile = System.IO.Path.Combine(path, "Game.uproject");
            var keepFile = System.IO.Path.Combine(content, "Keep.uasset");
            File.WriteAllText(projectFile, "{}");
            File.WriteAllText(keepFile, "keep");
            return new TemporaryWorkspace(path, new ProjectPath(projectFile), keepFile);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
