# UProject Hub — Project and Engine Discovery

## 1. Goals

Discovery should:

- find projects reliably;
- avoid expensive drive-wide scans on every startup;
- preserve a fast startup experience;
- isolate malformed projects;
- work with Launcher-installed and source-built Unreal Engines;
- remain easy to test.

## 2. Project Sources

Projects may come from:

1. Unreal/Epic-known project data when accessible;
2. persistent project search roots added through settings, folder picker, or folder drag-and-drop;
3. previously cached known projects.

The cache is not the sole source of truth. Cached entries are validated in the background.

## 3. Search Roots

Users may configure roots such as:

    D:\Unreal
    D:\GameAcademy
    C:\Users\<user>\Documents\Unreal Projects

A project rescan recursively searches configured roots for `*.uproject`.

A folder selected through the folder picker or received through folder drag-and-drop is always persisted as a project search root. Its meaning does not depend on whether a `.uproject` currently exists directly inside it. Discovery and Rescan search within that root.

Do not automatically add an entire drive root solely because it exists.

## 4. Startup Discovery Strategy

Startup:

1. load settings;
2. load cached projects;
3. show cached projects immediately;
4. start background Refresh;
5. validate known project paths and refresh metadata in the background;
6. perform lightweight discovery from configured/known sources;
7. persist updated cache.

A full recursive Rescan is a separate, explicit user action. The MVP does not offer an automatic full-Rescan-at-startup option.

## 5. Refresh vs Rescan

### Refresh

Works on already-known projects.

Refresh:

- verifies project path;
- reparses descriptor if needed;
- recomputes activity when needed;
- updates engine resolution.

### Rescan

Searches configured roots for additional `.uproject` files.

Rescan may discover:

- newly created projects;
- projects moved into a configured root;
- previously uncached projects.

The UI should make the difference visible.

Rescan runs only in response to an explicit user action.

## 6. `.uproject` Parsing

The parser should read descriptor data through JSON.

Important fields include:

- `FileVersion`;
- `EngineAssociation`;
- `Modules`;
- `Plugins`;
- optional project description/category metadata when useful later.

A parse failure affects only that project.

Do not abort the full scan.

## 7. Project Type

For MVP:

- a `Modules` array containing at least one module → C++;
- a missing `Modules` property or an empty `Modules` array → Blueprint.

The presence of a `Source` folder or any other filesystem evidence is not a secondary project-type signal in the MVP.

Tests must cover non-empty, empty, and missing `Modules`, including a project that has a `Source` folder but no descriptor modules.

## 8. Engine Association

`EngineAssociation` may represent:

### Launcher-style association

Example:

    "5.8"

### Source-build/registered association

Example:

    "{GUID}"

### Missing or unknown value

Represent association type explicitly where helpful instead of assuming every value is a numeric version.

## 9. Meaningful Project Activity

Purpose:

Show when the project was actually worked on rather than when Unreal generated a cache/log file.

Included:

    *.uproject
    Content/**
    Config/**
    Source/**
    Plugins/**

Excluded:

    Binaries/**
    DerivedDataCache/**
    Intermediate/**
    Saved/**
    .vs/**
    .idea/**
    .vscode/**
    .git/**

`LastModified` is the newest timestamp from included meaningful files.

## 10. Activity Performance

Do not recompute all activity timestamps synchronously on every startup.

Use cached activity data.

Refresh activity in background.

The implementation may optimize future rescans using validation timestamps or directory metadata, but correctness should remain understandable and testable.

Do not introduce complex filesystem watcher infrastructure in MVP.

## 11. Installed Engine Discovery

Use provider-style discovery.

Conceptual interface:

    IEngineProvider
        ├─ LauncherEngineProvider
        ├─ SourceBuildEngineProvider
        └─ ManualEngineProvider

Each provider returns normalized engine candidates without exposing its storage details to the rest of the app.

Representative engine data:

    InstalledEngine
    - DisplayName
    - Association
    - InstallPath
    - EditorPath
    - Source

## 12. Launcher Engine Provider

This provider is responsible for locating engines installed through Epic Games Launcher.

The provider may use Epic installation metadata available on Windows.

Keep Epic-specific manifest parsing isolated to this provider so format changes do not leak into the rest of the application.

Validate that the expected editor executable exists before treating an engine as usable.

## 13. Source Build Provider

Windows source-build registration may be discovered through:

    HKEY_CURRENT_USER\SOFTWARE\Epic Games\Unreal Engine\Builds

The provider maps registered association identifiers to engine root paths.

Registry access belongs in the Windows integration layer.

Tests should use an abstraction/fake rather than modifying the real Registry.

## 14. Manual Engine Provider

Users may manually register an engine root when automatic discovery cannot find it.

Manual registration is useful for:

- source builds;
- moved installations;
- archived engines;
- non-standard install paths.

Validate the selected engine root/editor executable before saving it.

Manual engine roots are user settings, not disposable cache data.

## 15. Engine Resolution

Given:

- a project's `EngineAssociation`;
- discovered engine candidates;

`EngineResolver` produces one of:

- `Resolved`;
- `Missing`;
- `Ambiguous`;
- `Unknown`.

### Resolved

Exactly one usable matching engine is identified.

### Missing

The expected association is understood but no matching usable engine exists.

### Ambiguous

More than one usable engine candidate matches and a unique safe choice cannot be made.

### Unknown

The association cannot be interpreted reliably.

Numeric associations are parsed and matched by major/minor version. For example, `"5.8"` matches usable candidates in the 5.8 engine family.

GUID associations match only an exactly equal GUID registration.

When the same physical engine is discovered through both a Launcher numeric
association and an exact registered GUID, both association aliases are kept
without treating the single installation as two numeric-version matches.

Provider priority must not choose automatically among multiple usable matches. Do not automatically substitute an engine from another Unreal version.

## 16. Launch Behavior

When resolved, launch:

    <EngineRoot>\Engine\Binaries\Win64\UnrealEditor.exe "<Project.uproject>"

Use safe argument handling.

If process launch succeeds, record `LastLaunched`.

If the editor path does not exist, return an actionable launch error and update engine status if appropriate.

### Open in Visual Studio

This action is available only for a C++ project with an existing `.sln` file. It is hidden or disabled otherwise.

### Generate Visual Studio Project Files

This action is available only for an available C++ project with exactly one resolved usable engine and a runnable bundled UnrealBuildTool executable in that engine root. Launcher, source-build, and manual engine entries use that resolved engine's UBT directly in `-ProjectFiles` mode; `-Rocket` is added only for a Launcher engine with an installed-build marker. Executable and arguments are passed separately, and unsupported layouts are disabled instead of being routed through a constructed shell command string.

Generation is an explicit generated-file mutation. It must show the engine root and exact project path for confirmation, run asynchronously with cancellation and bounded output, prevent duplicate runs for the same project, and re-run top-level `.sln` discovery after success. It must not modify `EngineAssociation` or automatically open Unreal Editor or Visual Studio.

## 17. Missing Projects

If a cached project no longer exists:

- do not crash;
- keep it visible in the default project list;
- mark it as `Missing` and show `Missing` (localized as `찾을 수 없음` in Korean);
- keep enough information to explain the issue;
- allow the user to choose `Remove from List`.

`Remove from List` deletes only UProject Hub's managed-list/cache entry. It must never delete the project directory or any project file.

## 18. Error Isolation

Each candidate project is processed independently.

Examples:

    Project A       valid
    Project B       valid
    BrokenProject   parse warning
    Project C       valid

The result still contains A, B, and C.

One inaccessible directory should not terminate the entire rescan.

Access-denied and IO errors should be logged with enough context to diagnose the affected path.

## 19. Testing Fixtures

Tests should include small file-tree fixtures for:

- normal Blueprint project;
- normal C++ project;
- empty and missing `Modules`, including `Source` without descriptor modules;
- malformed `.uproject`;
- project with source-build GUID;
- project where `Saved` has the newest file;
- project where `Content` has the newest meaningful file;
- missing project;
- multiple usable engines matching one numeric major/minor association;
- exact and non-matching GUID associations;
- version values that expose lexical-sort bugs.

Tests must not require the user's real project directories.
