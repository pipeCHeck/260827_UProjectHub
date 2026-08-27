# UProject Hub

A Windows desktop project browser for Unreal Engine projects.

The product display name is **UProject Hub**. The solution, project-name prefix, and root namespace use `UProjectHub`.

The goal is to provide the project-management experience missing from Epic Games Launcher: useful sorting, filtering, search, engine-version visibility, project activity information, and fast launching.

## Product Direction

The main list combines:

- File Explorer "Details" view: sortable columns and compact scanning.
- Unity Hub: project-centered vertical rows with path and metadata.
- Samsung One UI 7-inspired visual language: rounded geometry, clear hierarchy, calm layered surfaces, restrained translucency or blur where appropriate, and subtle interaction motion.

One UI 7 is interpreted for a desktop project-management tool rather than reproduced pixel for pixel. The priority order remains usability, information density and scanability, consistency, then visual polish and motion; the vertical details/list layout must not become a mobile-style, card-centric interface.

## MVP

The MVP will:

- discover `.uproject` files from known/user-configured locations;
- treat folders added through the folder picker or drag-and-drop as persistent project search roots;
- show project name, path, engine association, project type, and last meaningful modification time;
- search across project metadata;
- filter by engine version and project type;
- sort by name, Unreal version, last modified, and last launched;
- support favorites;
- launch projects using the resolved Unreal Editor;
- open project folders;
- cache project metadata for fast startup;
- refresh data in the background;
- display missing/broken project and engine states without crashing the entire list;
- keep missing cached projects visible until the user removes only their manager/cache entry.

The MVP will not modify Unreal projects.

## Documentation

- `AGENTS.md` — instructions and guardrails for Codex.
- `docs/SPEC.md` — product behavior and MVP contract.
- `docs/ARCHITECTURE.md` — code boundaries and responsibilities.
- `docs/UI.md` — layout, visual language, and interactions.
- `docs/PROJECT_DISCOVERY.md` — project and engine discovery rules.

## Planned Stack

- C#
- .NET 10 LTS
- `net10.0-windows`
- WPF
- MVVM
- `System.Text.Json`
- Markdown documentation
- JSON settings/cache storage

External dependencies should remain minimal unless they provide clear value.
