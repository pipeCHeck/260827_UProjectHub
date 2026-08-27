using System.Windows;
using UProjectHub.App.Services;
using UProjectHub.App.ViewModels;
using UProjectHub.App.Views;
using UProjectHub.Core.Cache;
using UProjectHub.Core.Catalog;
using UProjectHub.Core.Engines;
using UProjectHub.Core.Filtering;
using UProjectHub.Core.Models;
using UProjectHub.Core.Searching;
using UProjectHub.Core.Settings;
using UProjectHub.Core.Sorting;
using UProjectHub.Core.Storage;
using UProjectHub.Core.Time;
using UProjectHub.Windows.Launching;
using UProjectHub.Windows.Storage;

namespace UProjectHub.App.Composition;

public sealed class AppBootstrapper
{
    public MainViewModel Build()
    {
        var statusBar = new StatusBarViewModel();
        var clock = new SystemClock();
        var paths = new LocalAppDataPathProvider().GetPaths();
        var writer = new AtomicJsonFileWriter();
        var settingsRepository = new JsonSettingsRepository(
            paths.SettingsFile,
            writer);
        var projectCacheRepository = new JsonProjectCacheRepository(
            paths.ProjectCacheFile,
            writer);
        var catalog = new ProjectCatalog();
        var removalService = new ManagedProjectRemovalService(
            catalog,
            projectCacheRepository,
            settingsRepository);
        var processLauncher = new ProcessLauncher();
        var projectActions = new ProjectActionService(
            catalog,
            settingsRepository,
            removalService,
            new UnrealEditorLauncher(processLauncher, clock),
            new ExplorerLauncher(processLauncher),
            new VisualStudioLauncher(processLauncher),
            new WpfClipboardService(),
            _ => EngineResolver.Resolve(
                rawAssociation: null,
                Array.Empty<InstalledEngine>()));
        var projectList = new ProjectListViewModel(project =>
            new ProjectContextActionsViewModel(
                project,
                projectActions,
                ShowProjectInformation));
        var searchService = new ProjectSearchService(clock);
        var searchFilter = new SearchFilterViewModel(
            projectList,
            new ProjectQueryParser(),
            new ProjectFilterService(searchService),
            new ProjectSortService());

        return new MainViewModel(
            statusBar,
            projectList: projectList,
            searchFilter: searchFilter,
            projectActions: projectActions);
    }

    private static void ShowProjectInformation(
        ProjectInformationViewModel viewModel)
    {
        var window = new ProjectInformationWindow(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };
        _ = window.ShowDialog();
    }
}
