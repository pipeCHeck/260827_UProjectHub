# UProject Hub — UI Specification

## 1. Design Direction

The UI combines three references:

- **Windows File Explorer Details view** for sorting and scanability;
- **Unity Hub project list** for project-centered rows with path/metadata;
- **Samsung One UI 7** for rounded geometry, clear visual hierarchy, calm layered surfaces, restrained translucency or blur where appropriate, and smooth but subtle interaction motion.

One UI 7 is not a pixel-perfect target. Interpret it for a desktop project-management tool while preserving the vertical details/list experience. Do not adopt mobile-scale whitespace or turn the project list into a card-centric layout.

Priority order:

1. usability;
2. information density and scanability;
3. consistency;
4. visual polish and motion.

If a One UI 7-inspired treatment makes the project list slower to scan or harder to operate, choose the more usable design.

## 2. Main Window

Conceptual layout:

    Unreal Projects                                      [Settings]

    28 projects

    [ Search projects...                                ]

    [ Engine: All ] [ Type: All ] [ Favorites ]   [Refresh]

    ★   Project                   Engine   Type       Last Modified
    ----------------------------------------------------------------

    ★   ue260826_SMB              5.8.2    C++        18 min ago
        D:\Unreal\ue260826_SMB

        SMOcopy                   5.8.1    C++        Yesterday
        D:\Study\SMOcopy

        Day03                     5.8.2    Blueprint  Aug 25
        D:\Study\Day03

    ----------------------------------------------------------------
    Showing 3 of 28                                  Last Modified ↓

The design should feel like a refined desktop utility, not a game launcher storefront.

## 3. Window Chrome and Header

The main content starts with:

- large page title: `Unreal Projects`;
- smaller project-count subtitle;
- settings action aligned away from the title.

Do not add a traditional `File / Edit / View / Tools` menu bar unless a future feature specifically requires it.

## 4. Search

Search is a primary interaction and should receive strong visual prominence.

Recommended characteristics:

- full/large width;
- approximately 44–48 px high in Normal density;
- rounded surface;
- search icon;
- placeholder text;
- clear button appears when text exists.

Examples:

    [ Search projects...                              ]

    [ version:5.8 type:cpp                         × ]

    [ path:"D:\Game Academy" modified:7d          × ]

`modified:7d` uses a rolling window beginning exactly `7 * 24` hours before the current instant. Double quotes group structured values containing spaces.

Unknown prefixes and malformed structured tokens behave as plain-text terms. Search remains usable and evaluates the resulting terms with normal AND semantics instead of showing a query failure.

`Ctrl+F` focuses search.

`Esc` clears search when search owns the current transient state.

Search results update from in-memory data while typing.

## 5. Filter Controls

Common filters use compact rounded chips/buttons rather than visually heavy default ComboBoxes.

Examples:

    [ Engine: All ▾ ]
    [ Type: All ▾ ]
    [ ☆ Favorites ]

Active filters should be visible but not brightly colored.

Avoid a separate large filter panel for MVP.

## 6. Project List

The main list is vertical and details-oriented.

Do not use a thumbnail/card grid as the primary screen.

Recommended visible information:

- favorite;
- project;
- engine;
- type;
- last modified;
- row actions/status as needed.

Project path is secondary text under project name.

Example:

    ★  ue260826_SMB              5.8.2   [ C++ ]       18 min ago    ⋮
       D:\Unreal\ue260826_SMB

## 7. Row Density

Two supported density modes:

### Normal

Target row height: approximately 56–64 px.

Shows project name and path comfortably.

### Compact

Target row height: approximately 42–48 px.

Designed for users with large project collections.

Density is a user setting.

Do not make Normal density excessively spacious.

## 8. Selection and Hover

Avoid the classic strongly colored full-row DataGrid selection.

Use:

- subtle hover surface;
- slightly stronger selected surface;
- rounded row/selection treatment when practical;
- clear text contrast.

Selection must remain obvious in both Light and Dark themes.

## 9. Dividers and Grid Lines

Avoid spreadsheet-style vertical grid lines.

Horizontal separators should be subtle or represented through spacing/surfaces.

Column alignment still needs to be visually precise.

The list should retain DataGrid/ListView efficiency without looking like Excel.

## 10. Project Type

Project type may be displayed as a small low-emphasis badge:

    [ C++ ]
    [ Blueprint ]

The badge exists for quick classification, not decoration.

Do not use saturated colors that make the list visually noisy.

## 11. Warnings

Normal projects show no redundant "Healthy" status.

Problems are shown only when actionable.

Examples:

    OldProject        5.6.1   Blueprint   3 months ago     ⚠

or secondary text:

    OldProject
    ⚠ Connected Unreal Engine could not be found

A cached project whose `.uproject` file is absent remains in the default list and shows:

    MissingProject
    ⚠ Missing

The `Missing` label may be localized as `찾을 수 없음` in Korean.

Warnings should:

- be visible;
- remain calm;
- provide a tooltip or detail action;
- not turn the entire row bright red.

## 12. Relative Time

Internally store exact timestamps.

Display human-friendly values such as:

- Just now;
- 18 min ago;
- Today 11:42;
- Yesterday 16:20;
- Aug 21;
- 2025-12-03.

Hover/tooltip may show the exact local timestamp.

## 13. Sorting

Sortable column headers behave similarly to File Explorer.

Examples:

    Engine ▲
    Engine ▼

Default:

    Last Modified ↓

Sort state should survive restart through settings.

## 14. Context Menu and Row Actions

Single click:

- select row.

Double click:

- open project.

Favorite icon:

- toggle favorite without opening project.

Overflow button (`⋮`) and right-click use the same context actions:

    Open in Unreal
    Open Existing .sln
    Generate Visual Studio Project Files
    Open Project Folder
    -------------------
    Copy Path
    Toggle Favorite
    -------------------
    Project Details
    Project Cleanup
    Remove from List

`Open Existing .sln` is enabled only for a C++ project when one solution can be selected safely. For Blueprint, missing, multiple, or inaccessible solution states, keep the action visible but disabled and show a concise reason in a tooltip. A missing `.sln` is an actionable informational state when generation is available, not a project-health warning.

`Generate Visual Studio Project Files` remains visible but is enabled only for an available C++ project with one resolved usable engine whose installation exposes a supported generation entry point. Before starting, a confirmation window shows the engine, engine root, exact `.uproject` path, expected `.sln` path, and that generated files may be created or replaced. While running, the window stays responsive, offers cancellation, and prevents a duplicate run for that project. Completion shows success, cancellation, or failure details; success immediately refreshes `Open Existing .sln` availability.

`Remove from List` is shown only for a missing project. It removes the entry from UProject Hub's managed list/cache and never deletes the project directory or files.

`Project Cleanup` is enabled only for an available project. Its separate window lists only `Intermediate`, `DerivedDataCache`, `.vs`, the project-root `Binaries`, and a uniquely identified top-level `.sln`, with exact paths, current existence, and on-demand sizes. The first three existing safe generated folders are selected by default; `Binaries` and `.sln` are not. Selecting `Binaries` warns that a rebuild may be required, and selecting `.sln` explains that Generate Visual Studio Project Files can recreate it. Deletion requires a second confirmation view showing the selected exact paths. Results remain per item so one failure does not hide or stop other item outcomes.

### 14.1 Project Details

`Project Details` replaces the former Project Information dialog and provides
three keyboard-accessible, scrollable sections:

- **Overview** contains the existing project path, engine, type, state,
  favorite, and timestamp fields.
- **Diagnostics** contains low-cost basic findings ordered by severity and
  stable priority.
- **Tags & Notes** edits user-owned project tags and the project note. Tag
  changes are explicit add/remove actions. Notes remain visibly unsaved until
  the user chooses Save.

Do not add empty Storage, Source Control, or advanced-diagnostic tabs in this
phase. Normal projects have no `Healthy` or `Ready` row label. The list
shows only its primary Error or Warning, or a lower-emphasis actionable Info
when no problem is present. Engine problems take precedence over solution
findings. A generatable missing `.sln` uses the informational treatment and
points to Generate Visual Studio Project Files.

Normal density may show up to three compact tag labels plus a remaining-count
indicator in a project row. Compact density hides row tags. Full tag and note
editing remains in Project Details.

## 15. Keyboard

Required:

- `Up` / `Down`: change selected project;
- `Enter`: open selected project;
- `Ctrl+F`: focus search;
- `F5`: refresh known projects;
- `Esc`: clear transient search/menu state.

`Delete` does not delete projects.

## 16. Empty States

No projects discovered:

    No Unreal projects found.

    Add a project search root or rescan configured locations.

No search results:

    No projects match these filters.

    [ Reset search and filters ]

These are distinct states.

## 17. Background Refresh State

Do not block the entire window with a loading overlay during normal refresh.

Use a quiet status indicator:

    Checking projects…

Rows may update incrementally.

Cached projects remain interactive.

Startup always loads settings, displays cached projects immediately, and begins background Refresh. The MVP has no startup option for a full Rescan; Rescan runs only through an explicit user action.

## 18. Responsive Desktop Behavior

This is a desktop development tool, not a mobile layout.

When width decreases:

1. hide/deprioritize low-value optional columns;
2. preserve project name, engine, and last modified;
3. enforce a sensible minimum window width.

Suggested progression:

Wide:

    Project | Engine | Type | Last Modified | Last Launched

Medium:

    Project | Engine | Type | Last Modified

Narrow:

    Project | Engine | Last Modified

Do not turn the list into stacked mobile cards.

## 19. Settings UI

Settings use a vertical One UI 7-inspired grouping style.

Example:

    Settings

    Projects
    ---------------------------
    Search locations

    D:\Unreal
    D:\Study

    [ + Add search root ]    [ Rescan projects ]

    Appearance
    ---------------------------
    Theme
    [ System ▾ ]

    Row density
    (●) Normal
    ( ) Compact

Settings should remain simple and avoid a complex tree/navigation structure unless the app grows substantially.

The folder picker and folder drag-and-drop always add a persistent project search root. The app does not change that meaning based on whether the selected folder currently contains a `.uproject` directly. Discovery and explicit Rescan search within the root.

## 20. Themes

Support semantic Light and Dark theme resources.

Default theme setting:

    System

One UI 7-inspired traits:

- clean neutral surfaces;
- comfortable but not wasteful spacing;
- rounded search/filter controls;
- strong hierarchy between primary and secondary text;
- restrained accent color;
- calm warning states.

Do not copy Samsung assets or attempt exact visual reproduction.

## 21. Motion & Animation

### 21.1 Purpose and Priority

Motion exists only to:

- provide short feedback for user input;
- make a state change perceptible;
- keep small UI transitions from feeling unnecessarily rigid;
- reference the soft interaction character of One UI 7 in a restrained way.

Priority order:

1. usability;
2. information density and scanability;
3. consistency;
4. motion and visual polish.

Remove motion when it slows project navigation, searching, filtering, sorting, scrolling, or keyboard interaction, or when it makes information harder to read.

### 21.2 Motion Tokens

Centralize duration and easing resources in `Themes/Motion.xaml`.

Recommended duration tokens:

- Fast: approximately 90 ms;
- Normal: approximately 140 ms;
- Slow: approximately 180 ms.

Use an Ease-Out family for the primary easing token. Exact WPF easing types may be selected during implementation, but controls must not hard-code duration or easing values locally.

### 21.3 Permitted Motion

**Hover and selection**

Project rows, buttons, and filter chips may use short 90–140 ms transitions for background, border, or foreground state. Selection must remain immediately identifiable.

**Press feedback**

Clickable buttons and filter chips may use a very small `RenderTransform` scale response, such as `1.0 → 0.98 → 1.0`. Do not use bounce or elastic easing.

**Favorite feedback**

The favorite star may use a short scale or opacity micro-interaction so the toggle result is perceptible. It must not delay the favorite state change.

**Refresh and Rescan feedback**

A small refresh indicator or icon may rotate only while Refresh or Rescan is active. It must stop immediately when the operation ends, must not introduce a full-window loading animation, and must not block interaction with the cached list.

**Dialogs and small surfaces**

Settings and Project Information surfaces may use a very short opacity or subtle scale transition. Do not add prominent window zoom or long opening/closing animations.

### 21.4 Immediate List Operations

Search, filter, and sort results update immediately. Project rows must not wait for animation before appearing in their correct result set or order.

Do not use:

- full project-list entrance animation;
- staggered project-row entrance;
- per-row fade-in for search results;
- row-position movement during sorting;
- row rearrangement during filtering;
- project-list reorder animation;
- continuously moving decorative animation;
- Light/Dark full-window crossfade;
- scroll-linked decorative effects;
- parallax;
- bounce or elastic animation;
- large zoom transitions.

### 21.5 Performance and Layout

Prefer compositor/render-adjacent properties:

- `Opacity`;
- brush/color transitions;
- `RenderTransform`.

Avoid animating layout properties:

- `Width`;
- `Height`;
- `Margin`;
- `GridLength`;
- layout position.

Motion must preserve DataGrid/ListView virtualization and must not create per-row animation work that degrades scrolling in large project collections.

### 21.6 Windows Animation Preference

UProject Hub follows the Windows system animation preference. The WPF implementation should observe `SystemParameters.ClientAreaAnimation` and respond to preference changes for the running application.

When system animations are disabled:

- non-essential custom motion becomes an immediate state change;
- all functionality remains available;
- layout remains identical;
- Refresh/Rescan state remains visible without relying on movement.

The MVP does not provide a separate Animations On/Off setting.

## 22. WPF Implementation Guidance

Prefer using a built-in WPF list/DataGrid foundation for:

- virtualization;
- keyboard navigation;
- scrolling;
- column behavior;
- selection.

Then replace default visual styling through local XAML styles/templates.

Do not build an entire table control from scratch solely for appearance.

Theme resources should be centralized under `Themes/`.

Avoid large all-in-one `MainWindow.xaml` files. Extract reusable controls when that improves clarity, for example:

    Controls/
    ├─ ProjectList.xaml
    ├─ SearchBox.xaml
    └─ FilterChip.xaml

Do not split trivial one-use markup into tiny controls without a readability benefit.
