using System.Windows.Input;
using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Settings;
using UProjectHub.Windows.Launching;

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
        Assert.IsFalse(fixture.ViewModel.RemoveFromListCommand.CanExecute(null));
        Assert.IsNotNull(fixture.ViewModel.OpenProjectFolderCommand);
        Assert.IsNotNull(fixture.ViewModel.RevealProjectFileCommand);
        Assert.IsNotNull(fixture.ViewModel.CopyPathCommand);
        Assert.IsNotNull(fixture.ViewModel.ToggleFavoriteCommand);
        Assert.IsNotNull(fixture.ViewModel.ProjectInformationCommand);
        Assert.AreEqual("Add to Favorites", fixture.ViewModel.ToggleFavoriteLabel);
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
    public async Task CommandsDelegateToOneActionServiceAndInformationUsesPresentationCallbackAsync()
    {
        var fixture = CreateFixture(CreateProject(ProjectType.Cpp, ProjectState.Available));

        await ExecuteAsync(fixture.ViewModel.ToggleFavoriteCommand);
        await ExecuteAsync(fixture.ViewModel.OpenProjectCommand);
        fixture.ViewModel.OpenProjectFolderCommand.Execute(null);
        fixture.ViewModel.RevealProjectFileCommand.Execute(null);
        fixture.ViewModel.CopyPathCommand.Execute(null);
        fixture.ViewModel.OpenInVisualStudioCommand.Execute(null);
        fixture.ViewModel.ProjectInformationCommand.Execute(null);

        Assert.AreEqual(1, fixture.UnrealLauncher.LaunchCount);
        Assert.AreEqual(1, fixture.Explorer.FolderCount);
        Assert.AreEqual(1, fixture.Explorer.RevealCount);
        Assert.AreEqual(fixture.Project.ProjectFilePath.Value, fixture.Clipboard.Text);
        Assert.AreEqual(1, fixture.VisualStudio.OpenCount);
        Assert.HasCount(1, fixture.InformationRequests);
        Assert.AreEqual(fixture.Project.Name, fixture.InformationRequests[0].Name);
        Assert.AreEqual(fixture.Project.ProjectFilePath.Value, fixture.InformationRequests[0].ProjectPath);
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
    public void ProjectInformationUsesQuietUnknownsAndExactInjectedLocalTime()
    {
        var project = CreateProject(
            ProjectType.Blueprint,
            ProjectState.Broken,
            lastLaunched: null,
            engineAssociation: null,
            engineDisplayVersion: null);

        var information = new ProjectInformationViewModel(project, TimeZoneInfo.Utc);

        Assert.AreEqual("—", information.EngineAssociation);
        Assert.AreEqual("—", information.EngineDisplayVersion);
        Assert.AreEqual("—", information.ProjectType);
        Assert.AreEqual("2026-08-28 11:00:00 +00:00", information.LastModified);
        Assert.AreEqual("Never", information.LastLaunched);
        Assert.AreEqual("Broken", information.ProjectState);
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

    private static Fixture CreateFixture(UnrealProject project)
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
        var removal = new ManagedProjectRemovalService(catalog, cache, settings);
        var unreal = new FakeUnrealEditorLauncher();
        var explorer = new FakeExplorerLauncher();
        var visualStudio = new FakeVisualStudioLauncher();
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
            settings,
            removal,
            unreal,
            explorer,
            visualStudio,
            clipboard,
            _ => resolution);
        var informationRequests = new List<ProjectInformationViewModel>();
        var viewModel = new ProjectContextActionsViewModel(
            project,
            actions,
            informationRequests.Add);
        return new Fixture(
            project,
            viewModel,
            catalog,
            unreal,
            explorer,
            visualStudio,
            clipboard,
            informationRequests);
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
        List<ProjectInformationViewModel> InformationRequests);

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

        public int RevealCount { get; private set; }

        public LaunchResult OpenProjectFolder(UnrealProject project)
        {
            FolderCount++;
            return LaunchResult.Succeeded();
        }

        public LaunchResult RevealProjectFile(UnrealProject project)
        {
            RevealCount++;
            return LaunchResult.Succeeded();
        }
    }

    private sealed class FakeVisualStudioLauncher : IVisualStudioLauncher
    {
        public int OpenCount { get; private set; }

        public bool CanOpenSolution(UnrealProject project) => project.ProjectType == ProjectType.Cpp;

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
}
