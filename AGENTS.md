# UProject Hub — Codex Instructions

## Product Goal

Build a lightweight Windows desktop app named **UProject Hub** that replaces the weak "My Projects" experience in Epic Games Launcher for Unreal Engine projects.

Use `UProjectHub` for the solution name, project-name prefix, and root namespace.

The app should feel like:

- Windows File Explorer "Details" view for information density and sorting.
- Unity Hub project list for project-oriented navigation.
- Samsung One UI as a visual reference for spacing, surfaces, rounded controls, typography hierarchy, and quiet states.

One UI is a design reference, not a pixel-perfect target. Usability, scanability, and information density always take priority over visual imitation.

## Source of Truth

When behavior or implementation conflicts, use this order:

1. `docs/SPEC.md`
2. Relevant detailed design document in `docs/`
3. Existing tests
4. Existing implementation

Do not silently change product behavior because current code happens to work differently from the spec.

## Required Reading

Before changing behavior:

- Read `docs/SPEC.md`.

Before changing architecture or project boundaries:

- Read `docs/ARCHITECTURE.md`.

Before changing visible UI, interaction, spacing, styling, or layout:

- Read `docs/UI.md`.

Before changing project discovery, Unreal version detection, project metadata parsing, or activity detection:

- Read `docs/PROJECT_DISCOVERY.md`.

## Architecture Rules

- Keep business logic out of WPF Views and code-behind.
- `UProjectHub.Core` must not reference WPF.
- Windows-specific functionality belongs in `UProjectHub.Windows`.
- UI-specific functionality belongs in `UProjectHub.App`.
- Prefer small, focused classes with one clear responsibility.
- Do not introduce abstractions unless they isolate OS/external state or provide clear testability value.
- Avoid multipurpose service classes.
- Avoid large `MainWindow.xaml.cs` or `MainViewModel.cs` files that accumulate unrelated logic.
- Prefer composition over global static state.

## Technology Rules

- Target `.NET 10 LTS` with `net10.0-windows`.
- Use C# and WPF.
- Use `System.Text.Json` for JSON serialization.
- Keep external dependencies minimal and add one only when it provides clear value.

## Safety Rules

The MVP is read-only with respect to Unreal projects.

Do not:

- modify `.uproject` files;
- change `EngineAssociation`;
- delete project files;
- delete `Saved`, `Intermediate`, `Binaries`, or cache folders;
- upgrade projects to another Unreal Engine version;
- rewrite project configuration.

Any future destructive or mutating feature must be specified separately and require explicit user action.

## Performance Rules

- Show cached project data immediately on startup.
- Refresh project metadata in the background.
- Never block the main UI while scanning project roots.
- Do not recursively scan entire drives on every launch.
- Search/filter/sort must operate on in-memory project models, not trigger disk scans.
- Do not attach one `FileSystemWatcher` per project in the MVP.
- Avoid repeatedly traversing large `Content` trees unless a refresh actually requires it.

## UI Rules

- Main project presentation is a vertical list/details view.
- Do not add a card/grid view as the default experience.
- Avoid dense spreadsheet styling and strong grid lines.
- Avoid decorative UI that harms scanning speed.
- Normal states should remain visually quiet.
- Warnings should be noticeable without dominating the row.
- Use centralized theme resources; do not scatter literal colors, corner radii, or spacing values across Views.
- Light and dark themes must share the same semantic design tokens.
- Do not trade information density for large decorative cards.
- Motion must communicate interaction or state, not decorate.
- Motion must never reduce scanability or input responsiveness.
- Do not animate list reordering caused by search, filter, or sort.
- Do not add full-list entrance animations.
- Avoid animations of layout properties such as width, height, margin, grid length, or layout position.
- Centralize motion durations and easing in `Themes/Motion.xaml`; do not hard-code them per control.
- Respect the Windows system animation preference.
- When system animations are disabled, non-essential motion must become immediate state changes without changing functionality or layout.

## Testing Rules

When changing Core behavior:

- Add or update tests.
- Prefer fixture-based tests over requiring a real Unreal Engine installation.
- Test edge cases such as version 5.9 vs 5.10 sorting.
- A malformed `.uproject` must not abort the whole scan.

Before considering a task complete:

1. Run `dotnet test`.
2. Run `dotnet build`.
3. Check that the implementation still matches the relevant design docs.
4. Remove temporary/debug code and unrelated edits.

## Intended Commands

Build:

    dotnet build

Test:

    dotnet test

Run:

    dotnet run --project src/UProjectHub.App

If the actual project path changes, update this document and `README.md` together.

## Scope Discipline

Prefer small tasks.

Good:

> Implement `.uproject` parsing for EngineAssociation and Modules. Add tests. Do not change UI.

Good:

> Add Engine-version sorting using semantic numeric comparison. Add a 5.9 vs 5.10 regression test.

Avoid:

> Build discovery, UI, theming, settings, engine detection, and launch behavior all at once.

Do not perform unrelated refactors during feature work.
