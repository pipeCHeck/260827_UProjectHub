using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.App.Views;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Models;
using UProjectHub.Core.Paths;
using UProjectHub.Core.Settings;

namespace UProjectHub.Core.Tests.App;

[TestClass]
[DoNotParallelize]
public sealed class ProjectDetailsCloseTests
{
    [STATestMethod]
    public void DirtyNotePreventsProjectDetailsFromClosing()
    {
        using var fixture = CreateFixture("Saved note");
        fixture.Window.Show();
        fixture.Notes.NoteText = "Unsaved edit";

        fixture.Window.Close();

        Assert.IsTrue(fixture.Window.IsVisible);
    }

    [STATestMethod]
    public void CleanNoteClosesWithoutShowingConfirmation()
    {
        using var fixture = CreateFixture("Saved note");
        fixture.Window.Show();

        fixture.Window.Close();

        Assert.IsFalse(fixture.Window.IsVisible);
    }

    [STATestMethod]
    [DataRow(CloseRoute.CloseButton)]
    [DataRow(CloseRoute.Escape)]
    [DataRow(CloseRoute.WindowChrome)]
    [DataRow(CloseRoute.AltF4)]
    public void EveryCloseRouteUsesTheSameDirtyNoteConfirmation(CloseRoute route)
    {
        using var fixture = CreateFixture("Saved note");
        fixture.Window.Show();
        fixture.Notes.NoteText = "Unsaved edit";

        RequestClose(fixture.Window, route);

        Assert.IsTrue(fixture.Window.IsVisible);
        Assert.AreEqual(
            Visibility.Visible,
            Confirmation(fixture.Window).Visibility);

        RequestClose(fixture.Window, route);

        Assert.IsTrue(fixture.Window.IsVisible);
        Assert.AreEqual(
            Visibility.Visible,
            Confirmation(fixture.Window).Visibility);
    }

    [STATestMethod]
    public void ContinueEditingDismissesConfirmationAndKeepsTheDirtyNote()
    {
        using var fixture = CreateFixture("Saved note");
        fixture.Window.Show();
        fixture.Notes.NoteText = "Unsaved edit";
        fixture.Window.Close();

        FindButton(fixture.Window, "ContinueEditingButton").RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent));

        Assert.IsTrue(fixture.Window.IsVisible);
        Assert.IsTrue(fixture.Notes.IsNoteDirty);
        Assert.AreEqual(
            Visibility.Collapsed,
            Confirmation(fixture.Window).Visibility);
    }

    [STATestMethod]
    public void CloseWithoutSavingClosesAfterExactlyOneConfirmation()
    {
        using var fixture = CreateFixture("Saved note");
        fixture.Window.Show();
        fixture.Notes.NoteText = "Unsaved edit";
        fixture.Window.Close();

        FindButton(fixture.Window, "CloseWithoutSavingButton").RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent));

        Assert.IsFalse(fixture.Window.IsVisible);
        Assert.IsTrue(fixture.Notes.IsNoteDirty);
    }

    [STATestMethod]
    public void SuccessfulSaveDismissesConfirmationAndAllowsNormalClose()
    {
        using var fixture = CreateFixture("Saved note");
        fixture.Window.Show();
        fixture.Notes.NoteText = "Saved after prompt";
        fixture.Window.Close();
        Assert.AreEqual(
            Visibility.Visible,
            Confirmation(fixture.Window).Visibility);

        ((AsyncRelayCommand)fixture.Notes.SaveNoteCommand)
            .ExecuteAsync()
            .GetAwaiter()
            .GetResult();

        Assert.IsFalse(fixture.Notes.IsNoteDirty);
        Assert.AreEqual(
            Visibility.Collapsed,
            Confirmation(fixture.Window).Visibility);

        fixture.Window.Close();

        Assert.IsFalse(fixture.Window.IsVisible);
    }

    private static void RequestClose(
        ProjectDetailsWindow window,
        CloseRoute route)
    {
        switch (route)
        {
            case CloseRoute.CloseButton:
                FindButton(window, "CloseButton").RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                break;
            case CloseRoute.Escape:
                window.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window),
                    Environment.TickCount,
                    Key.Escape)
                {
                    RoutedEvent = Keyboard.KeyDownEvent,
                });
                break;
            // Window chrome and Alt+F4 are both translated by WPF into the
            // same Window.Close/OnClosing boundary that the product owns.
            case CloseRoute.WindowChrome:
            case CloseRoute.AltF4:
                window.Close();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(route));
        }
    }

    private static Grid Confirmation(ProjectDetailsWindow window) =>
        Assert.IsInstanceOfType<Grid>(
            window.FindName("UnsavedNoteCloseConfirmation"));

    private static Button FindButton(ProjectDetailsWindow window, string name) =>
        Assert.IsInstanceOfType<Button>(window.FindName(name));

    private static Fixture CreateFixture(string note)
    {
        var project = new UnrealProject(
            "Game",
            new ProjectPath(@"D:\Projects\Game\Game.uproject"),
            "5.8",
            "5.8.1",
            ProjectType.Cpp,
            DateTimeOffset.UnixEpoch,
            LastLaunched: null,
            IsFavorite: false,
            ProjectState.Available,
            EngineResolutionState.Resolved)
        {
            Note = note,
        };
        var catalog = new ProjectCatalog();
        catalog.Upsert(project);
        var repository = new MemorySettingsRepository(new AppSettings
        {
            ProjectUserStates =
            [
                new ProjectUserState(project.ProjectFilePath) { Note = note },
            ],
        });
        var metadata = new ProjectUserMetadataService(
            catalog,
            new SettingsMutationService(repository));
        var notes = new ProjectNotesViewModel(project, metadata);
        var details = new ProjectDetailsViewModel(
            new ProjectOverviewViewModel(project),
            new ProjectDiagnosticsViewModel(new ProjectDiagnosticReport(
                project.ProjectFilePath,
                DateTimeOffset.UnixEpoch,
                [])),
            notes,
            ProjectDetailsSection.TagsAndNotes);
        return new Fixture(
            new ProjectDetailsWindow(details),
            details,
            notes);
    }

    private sealed class Fixture(
        ProjectDetailsWindow window,
        ProjectDetailsViewModel details,
        ProjectNotesViewModel notes) : IDisposable
    {
        public ProjectDetailsWindow Window { get; } = window;

        public ProjectNotesViewModel Notes { get; } = notes;

        public void Dispose()
        {
            if (Window.IsVisible)
            {
                Notes.NoteText = "Saved note";
                Window.Close();
            }

            details.Dispose();
        }
    }

    private sealed class MemorySettingsRepository(AppSettings settings)
        : ISettingsRepository
    {
        private AppSettings _settings = settings;

        public Task<AppSettings> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings);

        public Task SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    public enum CloseRoute
    {
        CloseButton,
        Escape,
        WindowChrome,
        AltF4,
    }
}
