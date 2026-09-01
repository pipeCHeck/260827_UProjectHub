using System.ComponentModel;
using UProjectHub.App.Infrastructure;
using UProjectHub.App.ViewModels;

namespace UProjectHub.Core.Tests.App;

[TestClass]
public sealed class MainViewModelTests
{
    [TestMethod]
    public void InitialState_DescribesAnEmptyProjectShell()
    {
        var viewModel = CreateViewModel();

        Assert.AreEqual("UProject Hub", viewModel.Title);
        Assert.AreEqual(0, viewModel.ProjectCount);
        Assert.AreEqual("0 projects", viewModel.ProjectCountText);
        Assert.AreEqual("Ready", viewModel.StatusBar.StatusText);
        Assert.IsFalse(viewModel.StatusBar.IsOperationActive);
    }

    [TestMethod]
    public void SetProjectCount_RaisesCountAndDerivedTextNotifications()
    {
        var viewModel = CreateViewModel();
        var changes = RecordPropertyChanges(viewModel);

        viewModel.SetProjectCount(3);

        Assert.AreEqual(3, viewModel.ProjectCount);
        Assert.AreEqual("3 projects", viewModel.ProjectCountText);
        CollectionAssert.AreEqual(
            new[] { nameof(MainViewModel.ProjectCount), nameof(MainViewModel.ProjectCountText) },
            changes);
    }

    [TestMethod]
    public void SetProjectCount_WithSameValue_DoesNotRaiseNotifications()
    {
        var viewModel = CreateViewModel();
        viewModel.SetProjectCount(2);
        var changes = RecordPropertyChanges(viewModel);

        viewModel.SetProjectCount(2);

        Assert.IsEmpty(changes);
    }

    [TestMethod]
    public void SetProjectCount_WithNegativeValue_IsRejected()
    {
        var viewModel = CreateViewModel();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => viewModel.SetProjectCount(-1));
    }

    [TestMethod]
    public void StatusBar_ChangesRaiseOnlyTheirOwnNotifications()
    {
        var statusBar = new StatusBarViewModel();
        var changes = RecordPropertyChanges(statusBar);

        statusBar.SetStatus("Refreshing projects");
        statusBar.SetOperationActive(true);

        Assert.AreEqual("Refreshing projects", statusBar.StatusText);
        Assert.IsTrue(statusBar.IsOperationActive);
        CollectionAssert.AreEqual(
            new[] { nameof(StatusBarViewModel.StatusText), nameof(StatusBarViewModel.IsOperationActive) },
            changes);
    }

    [TestMethod]
    public void RelayCommand_ExecutesAndHonorsCanExecute()
    {
        var executionCount = 0;
        var canExecute = false;
        var command = new RelayCommand(() => executionCount++, () => canExecute);
        var canExecuteChangedCount = 0;
        command.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

        Assert.IsFalse(command.CanExecute(null));
        canExecute = true;
        command.RaiseCanExecuteChanged();
        Assert.IsTrue(command.CanExecute(null));

        command.Execute(null);

        Assert.AreEqual(1, executionCount);
        Assert.AreEqual(1, canExecuteChangedCount);
    }

    [TestMethod]
    public async Task AsyncRelayCommand_BlocksReentryAndRestoresCanExecuteChanged()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionCount = 0;
        var command = new AsyncRelayCommand(async () =>
        {
            executionCount++;
            started.SetResult();
            await release.Task;
        });
        var canExecuteStates = new List<bool>();
        command.CanExecuteChanged += (_, _) => canExecuteStates.Add(command.CanExecute(null));

        var firstExecution = command.ExecuteAsync();
        await started.Task;

        Assert.IsTrue(command.IsExecuting);
        Assert.IsFalse(command.CanExecute(null));

        await command.ExecuteAsync();
        Assert.AreEqual(1, executionCount);

        release.SetResult();
        await firstExecution;

        Assert.IsFalse(command.IsExecuting);
        Assert.IsTrue(command.CanExecute(null));
        CollectionAssert.AreEqual(new[] { false, true }, canExecuteStates);
    }

    [TestMethod]
    public void SettingsCommand_WithoutCallback_IsDisabled()
    {
        var viewModel = CreateViewModel();

        Assert.IsFalse(viewModel.SettingsCommand.CanExecute(null));
    }

    [TestMethod]
    public void SettingsCommand_WithCallback_ExecutesInjectedAction()
    {
        var executionCount = 0;
        var viewModel = CreateViewModel(() => executionCount++);

        Assert.IsTrue(viewModel.SettingsCommand.CanExecute(null));
        viewModel.SettingsCommand.Execute(null);

        Assert.AreEqual(1, executionCount);
    }

    private static MainViewModel CreateViewModel(Action? settingsAction = null)
    {
        return new MainViewModel(new StatusBarViewModel(), settingsAction);
    }

    private static List<string> RecordPropertyChanges(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName is not null)
            {
                changes.Add(eventArgs.PropertyName);
            }
        };

        return changes;
    }
}
