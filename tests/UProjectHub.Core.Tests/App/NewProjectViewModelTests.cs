using UProjectHub.App.ViewModels;
using UProjectHub.Core.Engines;
using UProjectHub.Core.Models;
using UProjectHub.Windows.Launching;
using System.ComponentModel;
using System.Collections.Specialized;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class NewProjectViewModelTests
{
    [TestMethod]
    public void InitialStateRequiresAnExplicitEngineSelection()
    {
        var launcher = new FakeUnrealEditorLauncher();
        var viewModel = new NewProjectViewModel(
            launcher,
            new StatusBarViewModel());

        Assert.HasCount(1, viewModel.EngineOptions);
        Assert.AreEqual("Select version", viewModel.SelectedEngineOption.Label);
        Assert.IsNull(viewModel.SelectedEngine);
        Assert.IsFalse(viewModel.LaunchCommand.CanExecute(null));
    }

    [TestMethod]
    public void EngineRefreshListsOnlyUsableEnginesAndKeepsSelectionExplicit()
    {
        var launcher = new FakeUnrealEditorLauncher();
        var viewModel = new NewProjectViewModel(
            launcher,
            new StatusBarViewModel());
        var usable = CreateEngine("5.10", @"D:\UE_5.10\UnrealEditor.exe", true);
        var unusable = CreateEngine("5.8", @"D:\UE_5.8\UnrealEditor.exe", false);

        viewModel.SetEngines([unusable, usable]);

        CollectionAssert.AreEqual(
            new[] { "Select version", "Unreal Engine 5.10" },
            viewModel.EngineOptions.Select(option => option.Label).ToArray());
        Assert.IsNull(viewModel.SelectedEngine);
        Assert.IsFalse(viewModel.LaunchCommand.CanExecute(null));

        viewModel.SelectedEngineOption = viewModel.EngineOptions[1];

        Assert.AreSame(usable, viewModel.SelectedEngine);
        Assert.IsTrue(viewModel.LaunchCommand.CanExecute(null));
    }

    [TestMethod]
    public void EngineRefreshReassertsPlaceholderSelectionForTheComboBox()
    {
        var viewModel = new NewProjectViewModel(
            new FakeUnrealEditorLauncher(),
            new StatusBarViewModel());
        var changes = new List<string>();
        ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName is not null)
            {
                changes.Add(eventArgs.PropertyName);
            }
        };

        viewModel.SetEngines([
            CreateEngine("5.10", @"D:\UE_5.10\UnrealEditor.exe", true),
        ]);

        CollectionAssert.Contains(
            changes,
            nameof(NewProjectViewModel.SelectedEngineOption));
        Assert.AreEqual("Select version", viewModel.SelectedEngineOption.Label);
        Assert.IsNull(viewModel.SelectedEngine);
    }

    [TestMethod]
    public void SelectedEngineValueMapsNullToPlaceholderAndEngineToItsOption()
    {
        var viewModel = new NewProjectViewModel(
            new FakeUnrealEditorLauncher(),
            new StatusBarViewModel());
        var engine = CreateEngine(
            "5.10",
            @"D:\UE_5.10\UnrealEditor.exe",
            true);
        viewModel.SetEngines([engine]);

        viewModel.SelectedEngine = engine;

        Assert.AreEqual("Unreal Engine 5.10", viewModel.SelectedEngineOption.Label);
        Assert.IsTrue(viewModel.LaunchCommand.CanExecute(null));

        viewModel.SelectedEngine = null;

        Assert.AreEqual("Select version", viewModel.SelectedEngineOption.Label);
        Assert.IsNull(viewModel.SelectedEngine);
        Assert.IsFalse(viewModel.LaunchCommand.CanExecute(null));
    }

    [TestMethod]
    public void EngineRefreshKeepsThePlaceholderInTheCollection()
    {
        var viewModel = new NewProjectViewModel(
            new FakeUnrealEditorLauncher(),
            new StatusBarViewModel());
        var actions = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)viewModel.EngineOptions).CollectionChanged +=
            (_, eventArgs) => actions.Add(eventArgs.Action);

        viewModel.SetEngines([
            CreateEngine("5.10", @"D:\UE_5.10\UnrealEditor.exe", true),
        ]);

        CollectionAssert.DoesNotContain(
            actions,
            NotifyCollectionChangedAction.Reset);
        Assert.AreEqual("Select version", viewModel.EngineOptions[0].Label);
    }

    [TestMethod]
    public void LaunchUsesTheSelectedEngineAndReportsTheResult()
    {
        var status = new StatusBarViewModel();
        var launcher = new FakeUnrealEditorLauncher(
            LaunchResult.Succeeded());
        var viewModel = new NewProjectViewModel(launcher, status);
        var engine = CreateEngine(
            "5.10",
            @"D:\UE_5.10\UnrealEditor.exe",
            true);
        viewModel.SetEngines([engine]);
        viewModel.SelectedEngineOption = viewModel.EngineOptions[1];

        viewModel.LaunchCommand.Execute(null);

        Assert.AreSame(engine, launcher.NewProjectEngine);
        Assert.AreEqual("Unreal Editor started.", status.StatusText);
    }

    [TestMethod]
    public void FailedLaunchKeepsTheSelectionAndShowsTheFailure()
    {
        var status = new StatusBarViewModel();
        var launcher = new FakeUnrealEditorLauncher(
            LaunchResult.Failed("Editor start failed."));
        var viewModel = new NewProjectViewModel(launcher, status);
        var engine = CreateEngine(
            "5.10",
            @"D:\UE_5.10\UnrealEditor.exe",
            true);
        viewModel.SetEngines([engine]);
        viewModel.SelectedEngineOption = viewModel.EngineOptions[1];

        viewModel.LaunchCommand.Execute(null);

        Assert.AreSame(engine, viewModel.SelectedEngine);
        Assert.AreEqual("Editor start failed.", status.StatusText);
    }

    private static InstalledEngine CreateEngine(
        string version,
        string editorPath,
        bool isUsable) =>
        new(
            $"Unreal Engine {version}",
            version,
            version,
            Path.GetDirectoryName(editorPath)!,
            editorPath,
            EngineSource.Launcher,
            isUsable);

    private sealed class FakeUnrealEditorLauncher(
        LaunchResult? result = null) : IUnrealEditorLauncher
    {
        public InstalledEngine? NewProjectEngine { get; private set; }

        public LaunchResult Launch(
            UnrealProject project,
            EngineResolution engineResolution) =>
            throw new InvalidOperationException("Project launch was not expected.");

        public LaunchResult LaunchNewProject(InstalledEngine engine)
        {
            NewProjectEngine = engine;
            return result ?? LaunchResult.Succeeded();
        }
    }
}
