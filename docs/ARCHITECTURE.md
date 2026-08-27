# UProject Hub — Architecture

## 1. Architectural Goal

The codebase should be easy for both humans and Codex to understand and modify.

The primary design rule is separation of:

- pure project-management logic;
- Windows/Unreal environment integration;
- WPF presentation.

The UI must not become the location where project parsing, search rules, file traversal, or engine resolution are implemented.

## 2. Solution Structure

Planned structure:

    UProjectHub/
    ├─ AGENTS.md
    ├─ README.md
    ├─ UProjectHub.sln
    ├─ Directory.Build.props
    │
    ├─ docs/
    │  ├─ SPEC.md
    │  ├─ ARCHITECTURE.md
    │  ├─ UI.md
    │  └─ PROJECT_DISCOVERY.md
    │
    ├─ src/
    │  ├─ UProjectHub.Core/
    │  ├─ UProjectHub.Windows/
    │  └─ UProjectHub.App/
    │
    ├─ tests/
    │  └─ UProjectHub.Core.Tests/
    │
    └─ scripts/
       ├─ build.ps1
       ├─ test.ps1
       └─ run.ps1

Exact internal folders may evolve, but project boundaries should remain stable.

All projects target `.NET 10 LTS`; Windows/WPF projects use `net10.0-windows`. The solution name, project-name prefix, and root namespace use `UProjectHub`.

## 3. Project Responsibilities

### 3.1 UProjectHub.Core

Pure application/domain logic.

Expected areas:

    Models/
    Discovery/
    Parsing/
    Activity/
    Searching/
    Filtering/
    Sorting/
    Settings/
    Cache/

Examples of responsibilities:

- project models;
- `.uproject` descriptor parsing;
- query parsing;
- in-memory project matching;
- filter rules;
- semantic engine-version comparison;
- meaningful modification calculation policy;
- cache/settings serialization models.

Core must not reference WPF.

Core should avoid direct Windows Registry or process-launch calls.

### 3.2 UProjectHub.Windows

Windows and Unreal-installation integration.

Expected areas:

    Engines/
    Registry/
    Launching/
    Storage/

Responsibilities:

- Epic/Launcher engine discovery;
- source-build registry discovery;
- manually registered engine validation;
- Unreal Editor path resolution;
- Windows Registry access;
- Explorer launch;
- process launch;
- opening an existing Visual Studio `.sln` for an applicable C++ project;
- local application-data path resolution.

The Windows layer must not generate Visual Studio project files in the MVP.

### 3.3 UProjectHub.App

WPF presentation and application lifecycle.

Expected areas:

    Views/
    ViewModels/
    Controls/
    Converters/
    Services/
    Themes/

Responsibilities:

- main window;
- settings UI;
- binding;
- user commands;
- background refresh orchestration;
- display formatting;
- theme resources;
- row selection and input behavior.

Business rules should be delegated to Core/Windows services.

## 4. Model Direction

A representative project model may contain:

    UnrealProject
    - Name
    - ProjectFilePath
    - ProjectDirectory
    - EngineAssociation
    - EngineDisplayVersion
    - ProjectType
    - LastModified
    - LastLaunched
    - IsFavorite
    - ProjectState
    - EngineState

Do not treat this as a required final class signature. It defines the information the app needs.

## 5. Data Flow

Typical startup:

    SettingsRepository
          │
          ├─────────────┐
          ▼             ▼
    ProjectCache    EngineCache
          │             │
          └──────┬──────┘
                 ▼
          MainViewModel
                 │
                 ▼
            MainWindow
                 │
                 │ immediate cached display
                 ▼
        BackgroundRefreshService
                 │
        ┌────────┴─────────┐
        ▼                  ▼
    Project refresh    Engine refresh
        │                  │
        └────────┬─────────┘
                 ▼
          incremental model update

Search/filter/sort occur after project data is in memory.

## 6. Discovery Boundaries

Recommended conceptual components:

    ProjectDiscoveryService
    UProjectParser
    ProjectActivityDetector
    ProjectSearchService
    ProjectQueryParser
    ProjectFilterService
    ProjectSortService

Keep these responsibilities separate.

`ProjectDiscoveryService` finds candidate `.uproject` files.

Folders received from the folder picker or folder drag-and-drop are persisted unchanged as project search roots. `ProjectDiscoveryService` recursively searches those roots; it does not reinterpret a root based on its current contents.

`UProjectParser` understands descriptor data. It classifies a project as C++ only when `Modules` contains at least one item; a missing or empty `Modules` array means Blueprint. Filesystem `Source` evidence is not part of MVP classification.

`ProjectActivityDetector` determines meaningful modification time.

Search/filter/sort operate on project models and do not scan the disk.

`ProjectQueryParser` supports double-quoted values with spaces. Unknown prefixes and malformed structured tokens produce plain-text terms rather than a failed query. `modified:Nd` is evaluated as an exact rolling `N * 24`-hour window from the current instant.

## 7. Engine Boundaries

Recommended conceptual components:

    IEngineProvider
        ├─ LauncherEngineProvider
        ├─ SourceBuildEngineProvider
        └─ ManualEngineProvider

    EngineDiscoveryService
    EngineResolver
    UnrealEditorLauncher

Providers discover installed/registered engine candidates.

`EngineResolver` matches a project's `EngineAssociation` to usable engine candidates. Numeric associations match by parsed major/minor version, while GUID associations require exact GUID equality. One usable match is `Resolved`; multiple usable matches are `Ambiguous`. Provider priority must not select among multiple matches, and another Unreal Engine version must never be used as an automatic fallback.

`UnrealEditorLauncher` launches the selected editor and project.

This isolates format changes in Epic metadata or Registry rules.

## 8. External-State Abstractions

Only introduce interfaces where they isolate unstable/external state or materially improve testability.

Good examples:

- filesystem access when deterministic testing requires it;
- clock/time provider for relative-date and time-window tests;
- Registry provider;
- process launcher;
- engine provider.

Avoid abstracting every internal class.

Do not create layers such as factories/managers/providers without a concrete boundary need.

## 9. MVVM

Use MVVM, but keep it practical.

ViewModels:

- expose view state;
- expose commands;
- coordinate application services;
- do not implement descriptor parsing;
- do not recursively enumerate project files;
- do not contain search grammar;
- do not directly read Registry values.

Code-behind should be limited to presentation-only behavior that is awkward or inappropriate to bind, and should never become a business-logic escape hatch.

## 10. Concurrency

Long-running operations:

- project-root scans;
- project activity traversal;
- engine discovery;
- cache validation;

must run outside the UI thread.

Use cancellation for background refresh so application shutdown stays fast.

Cached list interaction must remain available while refresh is running.

Project updates should be applied incrementally and safely to UI-bound collections.

## 11. Storage Architecture

Separate:

### Settings

User-owned, not safely disposable.

Examples:

- roots;
- favorites;
- theme;
- density;
- sort state.

### Cache

Derived and disposable.

Examples:

- parsed project metadata;
- calculated activity time;
- last validation timestamps;
- discovered engine data.

Serialization should use `System.Text.Json` unless a clear requirement changes this decision.

No database is required for MVP.

## 12. Theme Architecture

Presentation values belong in centralized resource dictionaries.

Recommended shape:

    Themes/
    ├─ Colors.xaml
    ├─ Typography.xaml
    ├─ Spacing.xaml
    ├─ Buttons.xaml
    ├─ DataGrid.xaml
    ├─ Light.xaml
    └─ Dark.xaml

Use semantic resources such as:

- WindowBackground;
- SurfaceBackground;
- SurfaceHover;
- SurfaceSelected;
- TextPrimary;
- TextSecondary;
- Accent;
- Warning;
- Divider.

Do not hard-code visual values repeatedly in page XAML.

## 13. Testing Architecture

Core behavior is the primary automated-test target.

Tests should run without:

- Unreal Engine installed;
- Epic Games Launcher running;
- real user projects.

Use small fixture projects.

Recommended fixture coverage:

    Fixtures/Projects/
    ├─ UE58Cpp/
    ├─ UE57Blueprint/
    ├─ BrokenProject/
    └─ SourceBuild/

Important regression tests:

- non-empty, empty, and missing `Modules` C++ / Blueprint classification without `Source` fallback;
- malformed descriptor isolation;
- 5.9 vs 5.10 sorting;
- exclusion of `Saved` from activity;
- inclusion of `Content` from activity;
- structured query combinations, quoted values, rolling `modified:Nd`, and malformed-token fallback;
- missing project visibility and manager-list removal without filesystem deletion;
- missing engine state;
- numeric major/minor engine matching, ambiguous candidates, and exact GUID/source-build association resolution.

## 14. Dependency Policy

Start with:

- .NET 10 LTS targeting `net10.0-windows`;
- C#;
- WPF;
- `System.Text.Json`.

Add external packages only when they provide clear value that would otherwise require significant, fragile custom code.

Do not pull in a large UI theme framework solely to mimic One UI.

The visual system should remain understandable from local XAML resources.

## 15. Change Discipline

When adding a feature:

1. identify its owning project/layer;
2. keep interfaces narrow;
3. add/update tests for Core behavior;
4. avoid unrelated refactors;
5. update docs when public behavior changes.

If a task requires changing multiple architectural boundaries, stop and reassess before implementing it as one large change.
