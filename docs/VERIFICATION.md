# UProject Hub Verification

This document records repeatable automated evidence separately from manual
Windows UI checks. It reflects the product and test suite verified on
2026-08-31.

Status meanings:

- `PASS` — verified by the stated automated command in the recorded run.
- `NOT MANUALLY VERIFIED` — not exercised through the live WPF UI or a real
  external installation during this run.
- `N/A` — not applicable to the current environment or fixture.

## Automated verification

The canonical command is:

```powershell
pwsh -File scripts/verify.ps1
```

| Check | Status | Evidence |
|---|---|---|
| Restore | PASS | `dotnet restore UProjectHub.sln` completed successfully. |
| Full solution tests | PASS | 545 passed, 0 failed, 0 skipped. |
| Release build | PASS | 0 warnings, 0 errors. |
| Whitespace validation | PASS | `git diff --check` returned exit code 0; Git emitted only LF-to-CRLF working-copy notices. |
| Safety-contract checks | PASS | All boundary checks listed below completed successfully. |
| One-command verification | PASS | The canonical command completed from restore through the final safety check. |

The full test suite includes automated coverage for the current product
features, including Generate Visual Studio Project Files, streaming and
bounded process output, bounded cancellation cleanup, Project Cleanup,
Tags/Notes and settings mutation serialization, and background Git Status.
These are supported features and are not treated as forbidden non-goals.

### Enforced production safety boundaries

`scripts/verify.ps1` searches production source under `src/`; generated `bin/`
and `obj/` files are excluded.

The script currently enforces all of the following:

- Production delete APIs are limited to:
  - `AtomicJsonFileWriter`, for its owned temporary settings/cache file;
  - `RollingFileLogger`, for its owned bounded log artifacts;
  - `ProjectCleanupService`, for validated cleanup targets.
- `ProjectCleanupService` may map directory cleanup only to project-root
  `Intermediate`, `DerivedDataCache`, `.vs`, and `Binaries`.
- Cleanup delete calls remain non-recursive at the final directory boundary,
  retain containment/reparse-point guards, and accept only a top-level `.sln`
  selected through the solution locator.
- `.uproject` write APIs and `EngineAssociation` mutation patterns are absent.
- Registry-related production source contains no write/delete API and does not
  open a key with `writable: true`.
- `UnrealBuildTool.exe` selection is isolated to
  `UnrealProjectFilesGenerator`, which executes through the external-process
  boundary.
- Production source contains no `cmd.exe`, PowerShell, `.bat`, or `.cmd`
  fallback.
- Process-tree termination is isolated to `ExternalProcessRunner`; its
  cancellation path retains a finite cleanup timeout and bounded wait.
- Process start calls are isolated to `ProcessLauncher` and
  `ExternalProcessRunner`.
- External telemetry and remote logging client patterns are absent.
- `ApplicationCoordinator.StartAsync` does not invoke full `RescanAsync`.
- `FileSystemWatcher` and thread-abort APIs remain absent.

These static checks complement behavioral tests. They intentionally permit
risky operations only at the named implementation boundaries rather than
allowing the same APIs across the production tree.

## Automated feature evidence

| Area | Automated evidence | Manual status this run |
|---|---|---|
| Generate Visual Studio Project Files | Engine-type command selection, argument safety, result classification, solution re-query, streaming output, cancellation, retry and ViewModel lifetime tests | NOT MANUALLY VERIFIED |
| Bounded process termination | Long-running process cancellation, heavy-output responsiveness, and bounded cleanup-wait tests | NOT MANUALLY VERIFIED |
| Project Cleanup | Disposable-project tests for fixed targets, unique `.sln`, content preservation, partial failure and reparse/junction rejection | NOT MANUALLY VERIFIED |
| Tags and Notes | Settings compatibility, shared mutation serialization, rollback/dirty state, immediate in-memory search/filter updates and interaction tests | NOT MANUALLY VERIFIED |
| Git Status | Clean/Changed/Not Repository/failure states, parent repository discovery, background concurrency, refresh races, remote sanitization and safe URL tests | NOT MANUALLY VERIFIED |
| Startup Refresh versus Rescan | Coordinator and integration tests plus the static startup call-path check | NOT MANUALLY VERIFIED |

All filesystem-destructive cleanup tests use disposable temporary projects.
They verify that non-target project content and external junction targets
survive. The verification run does not delete or modify a real user project.

## Manual Windows UI matrix

No live WPF UI interaction was performed during this verification-infrastructure
run. Automated coverage does not convert these rows into manual passes.

| Item | Status | Notes |
|---|---|---|
| Light and Dark themes | NOT MANUALLY VERIFIED | Theme resources are covered automatically; live appearance was not inspected. |
| Normal and Compact density | NOT MANUALLY VERIFIED | Density behavior is covered automatically; live rows were not inspected. |
| Narrow-window layout and scrolling | NOT MANUALLY VERIFIED | No live resize or scrolling session was performed. |
| Keyboard selection, Enter, Esc and F5 | NOT MANUALLY VERIFIED | Command routing has automated coverage; no live keyboard session was performed. |
| Right-click and overflow menus | NOT MANUALLY VERIFIED | Shared action routing is tested; menus were not opened manually. |
| Project Details tabs | NOT MANUALLY VERIFIED | Overview, Diagnostics, Tags & Notes, and Source Control were not inspected live. |
| Generate with a real Unreal Engine | NOT MANUALLY VERIFIED | No real UBT process was launched in this run. |
| Generate cancellation UI and live log | NOT MANUALLY VERIFIED | Process and ViewModel paths are automated; the window was not exercised. |
| Project Cleanup confirmation and results UI | NOT MANUALLY VERIFIED | No live cleanup window or real project was used. |
| Tag filter and autocomplete | NOT MANUALLY VERIFIED | Keyboard/mouse behavior has automated coverage; no live interaction was performed. |
| Note save and unsaved-close confirmation | NOT MANUALLY VERIFIED | ViewModel/window-close behavior is automated; no live dialog was used. |
| Git status against a real repository | NOT MANUALLY VERIFIED | Controlled and temporary fixtures were used by tests. |
| Remote repository browser open | NOT MANUALLY VERIFIED | URL policy is tested; no browser was opened. |
| Cache-first startup responsiveness | NOT MANUALLY VERIFIED | Coordinator behavior is automated; no timed live startup was observed. |

## Current verification limitations

- Static source checks guard known dangerous APIs and architectural entry
  points; behavioral tests remain the evidence for runtime path validation.
- The automated run does not require a real Unreal Engine, Visual Studio,
  network remote, or persistent user project.
- Visual polish, real external-tool behavior, and perceived responsiveness
  require a separate deliberate manual Windows UI session.
