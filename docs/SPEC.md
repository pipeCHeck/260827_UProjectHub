# UProject Hub — Product Specification

## 1. Purpose

UProject Hub is a lightweight Windows desktop application for browsing and launching Unreal Engine projects.

The product display name is **UProject Hub**. The solution name, project-name prefix, and root namespace use `UProjectHub`.

It exists because Epic Games Launcher's project list lacks practical project-management tools such as:

- useful sorting;
- filtering by Unreal Engine version;
- metadata-aware search;
- recent-modification ordering;
- project-type visibility;
- fast handling of large project collections.

The primary use case is a developer with many Unreal projects who wants to find the correct project quickly and understand its relevant metadata at a glance.

## 2. Product Principles

1. **Fast to open**  
   Cached data should appear immediately. Disk refresh happens in the background.

2. **Read-only by default**  
   The MVP must not modify Unreal project files or upgrade projects.

3. **Easy to scan**  
   The main screen is a compact vertical details list, not a thumbnail grid.

4. **Powerful without becoming complicated**  
   Common filtering is visible in the UI. Advanced search syntax is optional.

5. **Failure isolation**  
   One broken project must not break the entire project list.

6. **Codex-friendly implementation**  
   Business logic, OS integration, and UI are separated and independently testable.

7. **Design serves utility**  
   Samsung One UI 7 is a non-pixel-perfect design reference interpreted for a desktop project-management tool. Usability, information density and scanability, consistency, then visual polish and motion are the priority order; the vertical details/list experience must not become a mobile-style, card-centric interface.

### 2.1 Implementation Platform

The MVP targets `.NET 10 LTS` with the `net10.0-windows` target framework and uses C#, WPF, and `System.Text.Json`.

External dependencies must remain minimal and require clear value.

## 3. MVP Functional Requirements

### 3.1 Project Discovery

The application shall discover Unreal projects from:

- Unreal/Epic-known project information when available;
- user-configured search roots;
- persistent project search roots added by folder picker or folder drag-and-drop.

A folder added through either interaction is always stored as a project search root. Its meaning does not change based on its current contents. Discovery and Rescan search that root recursively for `.uproject` files.

The application shall not scan entire drives on every startup.

A manual "Rescan projects" action shall perform a deeper scan of configured roots.

### 3.2 Project Metadata

For each discovered project, the app shall maintain at least:

- project name;
- `.uproject` path;
- project directory;
- `EngineAssociation`;
- normalized/display engine version when possible;
- project type: C++ or Blueprint;
- last meaningful modified time;
- last launched time recorded by this app;
- favorite state;
- project state;
- resolved engine state.

### 3.3 C++ / Blueprint Classification

A project is classified as C++ when its `.uproject` descriptor contains a `Modules` array with at least one module.

A project whose `Modules` property is missing or whose `Modules` array is empty is classified as Blueprint for MVP display purposes.

The presence of a `Source` folder or other filesystem evidence must not be used as a secondary classification rule in the MVP.

The classification logic must be covered by tests.

### 3.4 Last Meaningful Modification

The app shall compute project activity from meaningful project files.

Included roots:

- `.uproject`;
- `Content/**`;
- `Config/**`;
- `Source/**`;
- `Plugins/**`.

Excluded roots:

- `Binaries/**`;
- `DerivedDataCache/**`;
- `Intermediate/**`;
- `Saved/**`;
- `.vs/**`;
- `.idea/**`;
- `.vscode/**`;
- `.git/**`.

The newest included file timestamp becomes `LastModified`.

Automatically generated files must not make an inactive project appear recently edited.

### 3.5 Search

The main search box shall search in-memory project metadata.

Default plain-text search shall include:

- project name;
- project path;
- engine display/version value;
- project type.

Search is case-insensitive.

The MVP shall support these structured filters in the search box:

- `version:<value>`
- `type:cpp`
- `type:bp`
- `path:<text>`
- `modified:<Nd>`
- `favorite:true`
- `tag:<value>`
- `note:<value>`

`tag:<value>` uses a case-insensitive exact match against individual project
tags. `note:<value>` uses a case-insensitive contains match against the saved
project note.

`modified:7d` means `LastModified` is at or after the instant exactly `7 * 24` hours before the current time.

Structured values containing spaces support double quotes, for example `path:"D:\Game Academy"`.

An unknown prefix or malformed structured token is treated as a plain-text search term. A query grammar error must not fail the complete search.

Structured terms may be combined and are interpreted as AND conditions.

Unqualified text terms also combine with structured terms.

Search must never trigger a project rescan.

### 3.6 Visible Filters

The main toolbar shall provide:

- Engine filter;
- Project Type filter;
- Tag filter populated from the current in-memory catalog;
- Favorites-only toggle.

Engine filter values should primarily reflect engine versions present in discovered projects, including versions that are currently not installed.

Visible filters combine with search using AND semantics.

The Tag filter uses case-insensitive exact tag identity, preserves the first
known display casing, and includes tags from missing catalog entries. A saved
tag selection that no longer exists in the current catalog is normalized to
All so it cannot leave the project list permanently empty.

### 3.7 Sorting

The list shall support:

- Project Name;
- Unreal Engine Version;
- Project Type;
- Last Modified;
- Last Launched.

Default sort:

- Last Modified descending.

Column-header sorting should behave like File Explorer: click once for one direction, click again to reverse.

Engine version comparison must be numeric/semantic where possible. `5.10` must sort after `5.9`.

When primary sort values are equal, project name ascending is the stable secondary order.

### 3.8 Favorites

Each project row shall provide a favorite toggle.

Favorite state is user data and must survive application restarts.

The toolbar shall provide a favorites-only filter.

### 3.9 Project Launch

Double-clicking a resolvable project shall launch the matching Unreal Editor with the `.uproject` path.

The app should launch the resolved `UnrealEditor.exe` explicitly rather than depending only on shell file association.

The app shall update `LastLaunched` when it successfully initiates a launch.

### 3.10 Engine Resolution

The application shall support:

- Epic/Launcher-installed Unreal Engines;
- registered source-build engines;
- manually added engine roots.

Possible resolution states:

- `Resolved`;
- `Missing`;
- `Ambiguous`;
- `Unknown`.

The app must not silently open a project with a different engine version when its intended engine cannot be resolved.

The MVP must not automatically rewrite `EngineAssociation`.

Numeric `EngineAssociation` values are matched by parsed major/minor version. For example, `"5.8"` matches usable engine candidates in the 5.8 family.

- exactly one usable matching candidate produces `Resolved`;
- more than one usable matching candidate produces `Ambiguous`;
- providers must not use priority to choose automatically among multiple matches;
- GUID associations require an exact GUID match;
- no association may fall back automatically to another Unreal Engine version.

### 3.11 Context Actions

Project context menu:

- Open in Unreal;
- Open Existing `.sln`;
- Generate Visual Studio Project Files;
- Open Project Folder;
- Copy Path;
- Toggle Favorite;
- Project Details;
- Tags & Notes, opening Project Details directly on that section;
- Remove from List, for missing projects.

`Open Existing .sln` is enabled only for a C++ project when one solution can be selected safely. It remains visible but disabled for Blueprint projects and when the solution is missing, ambiguous, or inaccessible. A disabled action must explain the reason in a tooltip.

`Generate Visual Studio Project Files` is enabled only for an available C++ project with exactly one resolved usable engine and a supported generation entry point in that engine installation. It always opens a confirmation surface showing the engine display/root, exact `.uproject` path, generated-file mutation, and expected solution location before execution. The operation is asynchronous and cancellable, prevents concurrent generation for the same project, reports bounded process output, and re-runs solution discovery after success.

A missing `.sln` is not by itself a project-health warning. When generation is available, it is an actionable informational state that points to `Generate Visual Studio Project Files`.

`Remove from List` removes only the missing entry from UProject Hub's managed project list and cache. It must never delete or modify project files.

Destructive project actions are excluded from the MVP.

#### Basic project diagnostics and details

Basic diagnostics are a low-cost projection of the existing `ProjectState`,
`EngineResolutionState`, and the top-level Visual Studio solution lookup for a
C++ project. They do not recursively scan project content, plugins, logs, or
storage.

The project list stays quiet for an available project with a resolved engine
and no actionable finding. Otherwise it shows only the highest-priority Error
or Warning; an actionable Info may be shown with lower emphasis when no more
important finding exists. Project and engine findings take priority over
solution findings. A missing `.sln` is an actionable Info only when Generate
Visual Studio Project Files is currently available, and is never by itself a
project-health Warning.

`Project Details` contains only an Overview section and a Diagnostics section
in this phase. Overview preserves the metadata previously shown by Project
Information. Diagnostics presents all basic findings independently so a
failure to inspect one fact does not prevent the remaining findings or another
project from being shown.

### 3.12 Keyboard Interaction

Required keyboard behavior:

- `Up` / `Down`: move list selection;
- `Enter`: open selected project;
- `Ctrl+F`: focus search;
- `F5`: refresh known projects;
- `Esc`: clear active search when appropriate or close transient UI.

`Delete` performs no destructive project action.

### 3.13 Refresh vs Rescan

**Refresh / F5**

Updates already-known projects:

- existence;
- descriptor metadata;
- meaningful modification time;
- engine resolution.

**Rescan Projects**

Searches configured roots again to discover newly created/moved projects.

The UI must distinguish these operations.

A full Rescan runs only in response to an explicit user action.

### 3.14 Startup

Startup flow:

1. read settings;
2. read cache;
3. show main UI and cached projects;
4. start background validation/refresh;
5. update rows incrementally.

The user must not wait for a full scan before interacting with cached projects.

The startup sequence is fixed for the MVP: load settings, display the cache immediately, and start background Refresh. The MVP has no option to run a full Rescan automatically at startup.

### 3.15 Missing and Broken Projects

A malformed `.uproject` or missing project path must be represented as a per-project state.

A cached project whose `.uproject` file no longer exists remains visible in the default list with the `Missing` state and a visible `Missing` status (localized as `찾을 수 없음` in Korean).

The user may remove a missing entry through `Remove from List`. This changes only UProject Hub's managed list/cache and never deletes the project directory or any project file.

The rest of the project list must continue operating.

Normal rows should have no redundant "OK" status label.

Warnings are shown only when action is needed.

## 4. Settings

User settings include at least:

- project search roots;
- manually registered engine roots;
- favorites;
- per-project tags and notes;
- theme mode: System / Light / Dark;
- row density: Normal / Compact;
- active sort;
- visible filters;
- column visibility/width state when implemented.

User settings and derived caches must be stored separately.

## 5. Storage

Default application data root:

    %LOCALAPPDATA%\UProjectHub\

Expected files:

    settings.json
    settings.json.bak
    project-cache.json
    engine-cache.json
    logs\app.log

Settings are user-owned state.

Project tags and notes are stored only in settings and never in `.uproject`
descriptors. Tags are trimmed, reject empty values, and prevent
case-insensitive duplicates. Double quotes and newline/control characters are
rejected because they cannot be represented safely by the tag search grammar.
Known-tag suggestions come only from the current in-memory catalog, prioritize
prefix matches before contains matches, and do not prevent free tag creation.
Notes use an explicit Save action. Closing Project Details with an unsaved note
requires an explicit choice to continue editing or close without saving; closing
never saves the note automatically.

All in-process settings changes must serialize the complete load-modify-save
operation through one shared mutation boundary so independent writers cannot
lose each other's updates.

Project and engine caches are disposable derived state.

Settings writes should use atomic replacement:

1. write temporary file;
2. validate serialization success;
3. replace primary file;
4. preserve a recent backup when practical.

A corrupted cache may be discarded and rebuilt.

## 6. Logging

The app shall write human-readable text logs.

Logs should record:

- startup;
- cache load result;
- refresh/rescan start and completion;
- project parse failures;
- engine-resolution warnings;
- launch failures.

Logs must avoid unnecessary sensitive information and use bounded retention/rotation.

## 7. UI Summary

The main screen is a vertical details list with:

- large page title;
- project count;
- prominent rounded search surface;
- compact filter chips;
- sortable project rows;
- project path shown as secondary text;
- calm warning states;
- settings entry;
- refresh action;
- footer/status area with visible/total project count.

See `UI.md` for detailed layout and interaction rules.

### 7.1 Motion

The MVP may use subtle motion when it communicates user interaction or a meaningful state change. Motion is optional visual feedback and must never take priority over usability, information density, scanability, keyboard operation, or input responsiveness.

UProject Hub must respect the Windows system animation preference. When system animations are disabled, non-essential custom motion must become immediate state changes while functionality and layout remain unchanged.

Search, filtering, sorting, and project-list reordering must update immediately. Their results must never wait for entrance, fade, movement, or reorder animations.

## 8. Non-Goals for MVP

The MVP shall not include:

- project deletion;
- Unreal version conversion;
- `EngineAssociation` editing;
- project-file generation without an explicit user confirmation;
- project cloning;
- project backups;
- Git integration;
- Git status;
- plugin management;
- project-size calculation;
- full project tagging system;
- Epic account/store functionality;
- live per-project file watchers;
- a thumbnail/card grid as the primary view.

These may be considered after the MVP is stable.

### 8.1 Post-MVP Operation Safety Contract

Read-only queries and project-changing operations are separate capabilities.

- Read-only queries inspect project, engine, source-control, diagnostic, or size information without changing project files.
- Generate Project Files is a generated-file mutation because it may create or replace `.sln` and generated project-file output. It requires an explicit user action and must show the selected engine, target `.uproject`, and operation before execution.
- Switch Unreal Engine Version is a descriptor mutation because it changes `EngineAssociation`. It is a separate later feature and must never select a target engine or run without explicit user selection and confirmation.
- Project Cleanup is a destructive operation available only through explicit per-item selection and a final confirmation. It is limited to the project-root `Intermediate`, `DerivedDataCache`, `.vs`, and `Binaries` folders plus one uniquely identified top-level `.sln`. Every target is recomputed and validated immediately before deletion; recursive size and deletion traversal must reject reparse points and must never follow links outside the project.

Expensive read-only work such as recursive size analysis, Git inspection, plugin dependency checks, or log/crash analysis runs only for a selected project after an explicit user request. It must not run automatically during startup or full-list loading.

## 9. Definition of Done

A feature is not complete until:

- required behavior is implemented;
- Core behavior changes have tests;
- `dotnet test` passes;
- `dotnet build` passes;
- relevant documentation still matches behavior;
- no unrelated refactors or temporary code remain.

UI changes additionally require checking:

- Light theme;
- Dark theme;
- Normal row density;
- Compact row density;
- narrow supported window width;
- keyboard interaction for affected controls.
- Windows system animations enabled and disabled when affected motion is present.
