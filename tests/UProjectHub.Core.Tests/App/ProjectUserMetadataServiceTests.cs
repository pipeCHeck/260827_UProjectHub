using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Filtering;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Searching;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;
using UProjectHub.Core.Tests.Time;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class ProjectUserMetadataServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task AddTagNormalizesAndImmediatelyRefreshesCurrentSearchAsync()
    {
        var project = CreateProject() with { Note = "Existing note" };
        var fixture = CreateFixture(project);
        var projectList = new ProjectListViewModel();
        var search = new SearchFilterViewModel(
            projectList,
            new ProjectQueryParser(),
            new ProjectFilterService(new ProjectSearchService(new FakeClock(Now))),
            new ProjectSortService());
        search.SetSnapshot(fixture.Catalog.GetSnapshot());
        search.SearchText = "tag:client";
        Assert.IsEmpty(projectList.Rows);
        fixture.Service.CatalogChanged += search.SetSnapshot;

        var result = await fixture.Service.AddTagAsync(
            project.ProjectFilePath,
            "  Client  ");

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { "Client" },
            fixture.Catalog.GetSnapshot().Projects.Single().Tags.ToArray());
        Assert.HasCount(1, projectList.Rows);
        var state = fixture.Repository.Current.ProjectUserStates.Single();
        CollectionAssert.AreEqual(new[] { "Client" }, state.Tags.ToArray());
        Assert.AreEqual("Existing note", state.Note);
    }

    [TestMethod]
    public async Task AddTagRejectsCaseInsensitiveDuplicateWithoutSavingAsync()
    {
        var project = CreateProject() with { Tags = ["Client"] };
        var fixture = CreateFixture(project, new ProjectUserState(project.ProjectFilePath)
        {
            Tags = ["Client"],
        });

        var result = await fixture.Service.AddTagAsync(
            project.ProjectFilePath,
            "client");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, fixture.Repository.SaveCount);
        CollectionAssert.AreEqual(
            new[] { "Client" },
            fixture.Catalog.GetSnapshot().Projects.Single().Tags.ToArray());
    }

    [TestMethod]
    [DataRow("Quoted\"Tag", "double quote")]
    [DataRow("Line\nBreak", "control character")]
    [DataRow("Tabbed\tTag", "control character")]
    public async Task AddTagRejectsValuesThatCannotBeRepresentedByTagSearchAsync(
        string tag,
        string expectedReason)
    {
        var project = CreateProject();
        var fixture = CreateFixture(project);

        var result = await fixture.Service.AddTagAsync(project.ProjectFilePath, tag);

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.ErrorMessage!, expectedReason);
        Assert.AreEqual(0, fixture.Repository.SaveCount);
        Assert.IsEmpty(fixture.Catalog.GetSnapshot().Projects.Single().Tags);
    }

    [TestMethod]
    public async Task RemoveTagUsesCaseInsensitiveIdentityAndUpdatesCatalogAsync()
    {
        var project = CreateProject() with { Tags = ["Client", "VR"] };
        var fixture = CreateFixture(project, new ProjectUserState(project.ProjectFilePath)
        {
            Tags = ["Client", "VR"],
        });

        var result = await fixture.Service.RemoveTagAsync(
            project.ProjectFilePath,
            "client");

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { "VR" },
            fixture.Repository.Current.ProjectUserStates.Single().Tags.ToArray());
        CollectionAssert.AreEqual(
            new[] { "VR" },
            fixture.Catalog.GetSnapshot().Projects.Single().Tags.ToArray());
    }

    [TestMethod]
    public async Task FailedTagSaveDoesNotChangeCatalogAsync()
    {
        var project = CreateProject();
        var fixture = CreateFixture(project);
        fixture.Repository.SaveException = new IOException("settings locked");

        var result = await fixture.Service.AddTagAsync(
            project.ProjectFilePath,
            "Urgent");

        Assert.IsFalse(result.IsSuccess);
        Assert.IsEmpty(fixture.Catalog.GetSnapshot().Projects.Single().Tags);
        Assert.IsEmpty(fixture.Repository.Current.ProjectUserStates);
    }

    [TestMethod]
    public async Task FailedNoteSaveKeepsViewModelDirtyForRetryAsync()
    {
        var project = CreateProject() with { Note = "Original" };
        var fixture = CreateFixture(project, new ProjectUserState(project.ProjectFilePath)
        {
            Note = "Original",
        });
        fixture.Repository.SaveException = new IOException("settings locked");
        using var viewModel = new ProjectNotesViewModel(project, fixture.Service);
        viewModel.NoteText = "Changed";

        await ((UProjectHub.App.Infrastructure.AsyncRelayCommand)viewModel.SaveNoteCommand)
            .ExecuteAsync();

        Assert.IsTrue(viewModel.IsNoteDirty);
        Assert.IsTrue(viewModel.HasError);
        Assert.AreEqual("Original", fixture.Catalog.GetSnapshot().Projects.Single().Note);

        fixture.Repository.SaveException = null;
        await ((UProjectHub.App.Infrastructure.AsyncRelayCommand)viewModel.SaveNoteCommand)
            .ExecuteAsync();

        Assert.IsFalse(viewModel.IsNoteDirty);
        Assert.IsFalse(viewModel.HasError);
        Assert.AreEqual("Changed", fixture.Catalog.GetSnapshot().Projects.Single().Note);
    }

    [TestMethod]
    public async Task SavedNoteImmediatelyRefreshesCurrentNoteSearchAsync()
    {
        var project = CreateProject();
        var fixture = CreateFixture(project);
        var projectList = new ProjectListViewModel();
        var search = new SearchFilterViewModel(
            projectList,
            new ProjectQueryParser(),
            new ProjectFilterService(new ProjectSearchService(new FakeClock(Now))),
            new ProjectSortService());
        search.SetSnapshot(fixture.Catalog.GetSnapshot());
        search.SearchText = "note:lighting";
        Assert.IsEmpty(projectList.Rows);
        fixture.Service.CatalogChanged += search.SetSnapshot;

        var result = await fixture.Service.SaveNoteAsync(
            project.ProjectFilePath,
            "Review the LIGHTING pass.");

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, projectList.Rows);
        Assert.AreEqual(
            "Review the LIGHTING pass.",
            fixture.Repository.Current.ProjectUserStates.Single().Note);
    }

    [TestMethod]
    public void NotesViewModelUsesSharedKnownTagsForAutocompleteWithoutBlockingFreeEntry()
    {
        var project = CreateProject();
        var fixture = CreateFixture(project);
        var tagIndex = new ProjectTagIndex();
        tagIndex.Rebuild([
            project with { Tags = ["게임인재원8기", "Game Academy"] },
        ]);
        using var viewModel = new ProjectNotesViewModel(
            project,
            fixture.Service,
            tagIndex: tagIndex);

        viewModel.NewTag = "게임인재";

        CollectionAssert.AreEqual(
            new[] { "게임인재원8기" },
            viewModel.TagSuggestions.ToArray());
        Assert.IsTrue(viewModel.IsSuggestionsOpen);

        viewModel.SelectedTagSuggestion = "게임인재원8기";

        Assert.AreEqual("게임인재원8기", viewModel.NewTag);
        Assert.IsFalse(viewModel.IsSuggestionsOpen);

        viewModel.NewTag = "Entirely New Tag";

        Assert.IsEmpty(viewModel.TagSuggestions);
        Assert.IsTrue(viewModel.AddTagCommand.CanExecute(null));
    }

    private static Fixture CreateFixture(
        UnrealProject project,
        ProjectUserState? state = null)
    {
        var catalog = new ProjectCatalog();
        catalog.Upsert(project);
        var repository = new ControlledSettingsRepository(new AppSettings
        {
            ProjectUserStates = state is null ? [] : [state],
        });
        var service = new ProjectUserMetadataService(
            catalog,
            new SettingsMutationService(repository));
        return new Fixture(catalog, repository, service);
    }

    private static UnrealProject CreateProject() => new(
        "Game",
        new ProjectPath(@"D:\Projects\Game\Game.uproject"),
        "5.8",
        "5.8.1",
        ProjectType.Cpp,
        Now.AddHours(-1),
        LastLaunched: null,
        IsFavorite: false,
        ProjectState.Available,
        EngineResolutionState.Resolved);

    private sealed record Fixture(
        ProjectCatalog Catalog,
        ControlledSettingsRepository Repository,
        ProjectUserMetadataService Service);

    private sealed class ControlledSettingsRepository(AppSettings settings)
        : ISettingsRepository
    {
        public AppSettings Current { get; private set; } = settings;

        public Exception? SaveException { get; set; }

        public int SaveCount { get; private set; }

        public Task<AppSettings> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (SaveException is not null)
            {
                return Task.FromException(SaveException);
            }

            Current = settings;
            return Task.CompletedTask;
        }
    }
}
