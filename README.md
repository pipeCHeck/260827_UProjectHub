# UProject Hub

UProject Hub is a lightweight Windows desktop browser for Unreal Engine projects. It combines the scanability of File Explorer's Details view with project-oriented search, filtering, engine resolution, and safe launch actions that are missing from Epic Games Launcher.

The MVP is implemented in C# on .NET 10 LTS using WPF (`net10.0-windows`). It has no external UI, MVVM, logging, or dependency-injection framework.

## Requirements

- Windows 10 or later
- .NET 10 SDK to build and test
- PowerShell 7 (`pwsh`) for the one-command verification script
- An Unreal Engine installation is optional; automated tests use isolated fixtures and fake OS boundaries

## Command-line workflow

Run commands from the repository root.

```powershell
# Release build
pwsh -File scripts/build.ps1

# Full solution tests
pwsh -File scripts/test.ps1

# Run the WPF application
pwsh -File scripts/run.ps1

# Restore, test, Release build, whitespace, and safety verification
pwsh -File scripts/verify.ps1
```

The equivalent direct commands are:

```powershell
dotnet build UProjectHub.sln -c Release
dotnet test UProjectHub.sln
dotnet run --project src/UProjectHub.App
```

## MVP features

- Cache-first startup followed by cancellable background metadata Refresh
- Bounded startup discovery from configured and Unreal-known roots
- Explicit recursive Rescan for newly added or deeply nested projects
- C++/Blueprint classification from `.uproject` descriptor modules
- Meaningful project activity time that excludes generated folders
- Plain and structured in-memory search
- Engine, project type, and favorites filters
- Semantic sorting, including correct Unreal version ordering such as 5.9 before 5.10
- Persistent favorites, LastLaunched, appearance, view state, search roots, and manual engine roots
- Launcher, registered source-build, and manual engine discovery
- Safe Resolved/Missing/Ambiguous/Unknown engine resolution
- Explicit UnrealEditor launch with argument-list handling
- Unreal, project folder, copy path, existing Visual Studio solution, Project Details, and Missing-only removal actions
- Explicitly confirmed Visual Studio project-file generation through the resolved engine's supported UnrealBuildTool entry point
- Low-cost basic diagnostics with quiet list priority and Overview/Diagnostics details
- Explicitly confirmed cleanup of selected generated project-root folders and a uniquely identified project solution
- Light/dark semantic themes, normal/compact density, responsive columns, and system-aware subtle motion
- Runtime English/Korean UI switching with persisted language selection
- Bounded rolling UTF-8 logs

## Versioning

The application version has one source of truth: `UProjectHubVersion` in
`src/UProjectHub.App/UProjectHub.App.csproj`. The UI appends the release suffix
and displays that build version in the Settings footer.

- Small bug or UX fix: increment the patch version (`0.1.0` → `0.1.1`).
- Feature addition or behavior change: increment the minor version (`0.1.x` → `0.2.0`).
- Stable release milestone: increment the major version (`1.0.0`).

## Refresh and Rescan

**Refresh** validates and updates projects already known to the catalog. F5 performs Refresh only and does not search for new projects.

At startup, UProject Hub additionally performs a bounded shallow discovery: each configured or Unreal-known root itself and its immediate child directories are checked for `.uproject` files. Startup never performs a full recursive Rescan.

**Rescan** is an explicit Settings action. It recursively searches the configured project roots and can find deeper or newly added projects.

## Data location

User-owned settings, disposable caches, and logs are stored below:

```text
%LOCALAPPDATA%\UProjectHub
```

The application does not use the user's real LocalAppData, Registry, Unreal installation, or processes during automated tests.

## Project mutation policy

UProject Hub does not modify `.uproject` descriptors, change `EngineAssociation`, convert engine versions, or delete user-authored project content. Generate Visual Studio Project Files is an explicitly confirmed generated-file mutation. Project Cleanup is an explicitly selected and finally confirmed destructive operation limited to the project-root `Intermediate`, `DerivedDataCache`, `.vs`, and `Binaries` folders plus one uniquely identified top-level `.sln`; it rejects reparse points and never targets `Content`, `Config`, `Source`, `Plugins`, `Saved`, or `.uproject`. “Remove from List” removes only UProject Hub's managed catalog/cache/settings entry for a Missing project.

## MVP non-goals

- Project creation, deletion, conversion, repair, or Unreal configuration editing
- Shell/batch fallbacks or unsupported UnrealBuildTool generation layouts
- Git, plugin, build, cooking, or packaging management
- Recursive drive-wide discovery or per-project `FileSystemWatcher` infrastructure
- Telemetry, analytics, or remote logging
- Visual Studio installation discovery
- Automatic selection among ambiguous matching engines

See [docs/VERIFICATION.md](docs/VERIFICATION.md) for automated evidence and the honest manual UI verification matrix. Architecture and behavior details remain in `docs/SPEC.md`, `docs/ARCHITECTURE.md`, `docs/UI.md`, and `docs/PROJECT_DISCOVERY.md`.
