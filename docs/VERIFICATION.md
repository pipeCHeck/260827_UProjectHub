# UProject Hub MVP Verification

This document separates repeatable automated evidence from manual Windows UI checks. A manual item is marked `PASS` only when it was directly exercised during the recorded Task 28 run. Automated coverage does not silently turn an unperformed visual check into a manual pass.

Status meanings:

- `PASS` — directly verified by the stated automated command or manual observation.
- `NOT MANUALLY VERIFIED` — covered statically or by tests where noted, but not exercised through the live UI in this verification run.
- `N/A` — not applicable to the current environment or fixture.
- `PENDING` — an automated command has not yet completed.

## Automated verification

| Check | Status | Evidence |
|---|---|---|
| Task 28 fixture workflow | PASS | 1 passed, 0 failed: `dotnet test UProjectHub.sln --filter "FullyQualifiedName~MvpWorkflowTests"` |
| Full solution tests | PASS | 324 passed, 0 failed, 0 skipped: `dotnet test UProjectHub.sln` |
| Release build | PASS | 0 warnings, 0 errors: `dotnet build UProjectHub.sln -c Release` |
| One-command verification | PASS | Restore, 324 tests, Release build, diff, and all safety checks passed: `pwsh -File scripts/verify.ps1` |
| Whitespace validation | PASS | `git diff --check` returned exit code 0; only Git's LF-to-CRLF working-copy notices were emitted. |
| Production forbidden-pattern checks | PASS | `scripts/verify.ps1` scopes deletes, Registry writes, project generation, process termination, `.uproject` writes, association mutation, startup Rescan, watchers, and telemetry. |

The integration fixture uses temp-local settings/caches, copied project descriptors, fake installed engines, and a fake process boundary. It verifies cache-first publish, Available/Missing/Broken isolation, bounded startup discovery, explicit recursive Rescan, in-memory query/filter/sort, favorite and LastLaunched persistence, engine resolution, safe launch, Missing-only removal, and byte-for-byte project fixture preservation.

The safety script reports two intentional production delete sites rather than treating them as project-destructive false positives:

- `AtomicJsonFileWriter` deletes only its own temporary atomic-write file.
- `RollingFileLogger` deletes only bounded log backups/active log artifacts that it owns.

## Manual UI matrix

### Appearance and layout

| Item | Status | Notes |
|---|---|---|
| Light theme | NOT MANUALLY VERIFIED | Theme resources and switching have automated tests. |
| Dark theme | NOT MANUALLY VERIFIED | Theme resources and switching have automated tests. |
| Normal density | NOT MANUALLY VERIFIED | Density resource switching has automated tests. |
| Compact density | NOT MANUALLY VERIFIED | Density resource switching has automated tests. |
| Wide columns | NOT MANUALLY VERIFIED | Responsive behavior is statically/test verified. |
| Medium columns | NOT MANUALLY VERIFIED | Last Launched should hide first. |
| Narrow/minimum supported width | NOT MANUALLY VERIFIED | Project, Engine, and Last Modified must remain readable. |
| Windows animations enabled | NOT MANUALLY VERIFIED | Effective 90/140/180 ms tokens have automated tests. |
| Windows animations disabled | NOT MANUALLY VERIFIED | Effective durations become zero; activity rotation is gated. |

### Project list states

| Item | Status | Notes |
|---|---|---|
| Available project row | NOT MANUALLY VERIFIED | Integration test validates catalog/presentation state. |
| Missing project row and quiet warning | NOT MANUALLY VERIFIED | Integration test validates state and retention. |
| Broken project isolation | NOT MANUALLY VERIFIED | Malformed fixture is isolated by integration test. |
| C++ classification | NOT MANUALLY VERIFIED | Fixture has a non-empty Modules array. |
| Blueprint classification | NOT MANUALLY VERIFIED | Fixture omits Modules. |
| Resolved engine state | NOT MANUALLY VERIFIED | Fake engine workflow is automated. |
| Missing engine state | NOT MANUALLY VERIFIED | Fake engine workflow is automated. |
| Ambiguous engine state | NOT MANUALLY VERIFIED | Two usable 5.10 candidates are automated. |

### Keyboard and context interactions

| Item | Status | Notes |
|---|---|---|
| Ctrl+F focuses search | NOT MANUALLY VERIFIED | View-only routing exists. |
| Esc clears search only | NOT MANUALLY VERIFIED | ViewModel/routing tests exist. |
| Up/Down selection | NOT MANUALLY VERIFIED | WPF DataGrid default navigation. |
| Enter opens selected resolved project | NOT MANUALLY VERIFIED | Action routing and fake process are tested. |
| Delete is a no-op | NOT MANUALLY VERIFIED | Explicit Missing-only Remove remains separate. |
| F5 performs Refresh only | NOT MANUALLY VERIFIED | Coordinator regression verifies no discovery/Rescan. |
| Favorite button | NOT MANUALLY VERIFIED | Persistence/catalog update is automated. |
| Right-click context menu | NOT MANUALLY VERIFIED | Shares the context-actions ViewModel. |
| Overflow menu | NOT MANUALLY VERIFIED | Shares the same context-actions ViewModel. |
| Copy Path | NOT MANUALLY VERIFIED | Clipboard boundary has unit coverage. |
| Open Folder | NOT MANUALLY VERIFIED | Explorer request has unit coverage. |
| Reveal `.uproject` | NOT MANUALLY VERIFIED | Explicit `explorer.exe /select,` request is tested. |
| Conditional Visual Studio action | NOT MANUALLY VERIFIED | Existing top-level `.sln` rules are tested. |
| Project Information | NOT MANUALLY VERIFIED | Read-only ViewModel/window wiring exists. |
| Missing-only Remove from List | NOT MANUALLY VERIFIED | Integration test confirms project files remain unchanged. |

### Settings

| Item | Status | Notes |
|---|---|---|
| Add/remove search root | NOT MANUALLY VERIFIED | Repository operation tests exist. |
| Add empty root | NOT MANUALLY VERIFIED | Empty folders remain valid persistent roots. |
| Folder drag/drop | NOT MANUALLY VERIFIED | Files are ignored; folders use the same add path. |
| Duplicate root suppression | NOT MANUALLY VERIFIED | Canonical case-insensitive identity is tested. |
| Valid manual engine | NOT MANUALLY VERIFIED | Standard `Build.version` and editor validation is tested. |
| Invalid manual engine diagnostic | NOT MANUALLY VERIFIED | Invalid roots do not persist. |
| System/Light/Dark setting | NOT MANUALLY VERIFIED | Save-then-apply behavior is tested. |
| Normal/Compact setting | NOT MANUALLY VERIFIED | Save-then-apply behavior is tested. |
| Explicit Rescan | NOT MANUALLY VERIFIED | Integration test verifies deeper discovery only after Rescan. |

### Startup, logs, motion, and performance

| Item | Status | Notes |
|---|---|---|
| Cache-first rows visible | NOT MANUALLY VERIFIED | Integration test blocks Refresh and observes cached rows first. |
| No blocking startup overlay | NOT MANUALLY VERIFIED | Coordinator design is asynchronous; live UI not observed yet. |
| Background Refresh status lifecycle | NOT MANUALLY VERIFIED | Coordinator/status tests exist. |
| No startup full Rescan | NOT MANUALLY VERIFIED | Integration and static checks verify call count/path. |
| `app.log` creation | NOT MANUALLY VERIFIED | Rolling logger tests use temp storage. |
| Log rotation/retention | NOT MANUALLY VERIFIED | Automated bounded rotation tests exist. |
| Refresh/Rescan/launch failure diagnostics | NOT MANUALLY VERIFIED | Logger call-site tests exist. |
| Button press micro-feedback | NOT MANUALLY VERIFIED | Centralized Fast duration is consumed. |
| Filter chip micro-feedback | NOT MANUALLY VERIFIED | Centralized motion resource is consumed. |
| Favorite micro-feedback | NOT MANUALLY VERIFIED | Visual feedback does not delay mutation. |
| Operation indicator only while active | NOT MANUALLY VERIFIED | State and animation-preference gating are tested. |
| Disabled animations preserve functionality | NOT MANUALLY VERIFIED | Effective zero-duration behavior is tested. |
| No search/filter/sort/reorder entrance animation | NOT MANUALLY VERIFIED | Static XAML review and immediate ViewModel tests cover this. |
| 1,000-row virtualization/scrolling | NOT MANUALLY VERIFIED | DataGrid recycling/virtualization is static; live scrolling was not exercised. |

## Live application smoke

| Check | Status | Evidence |
|---|---|---|
| Process starts without startup exception | PASS | `dotnet run --project src/UProjectHub.App --no-build` remained running for 10 seconds without startup output/exception, then was intentionally stopped with Ctrl+C. |
| Full interactive UI matrix | NOT MANUALLY VERIFIED | Requires deliberate Windows UI session with representative real or test data. |

## Read-only evidence

The Task 28 workflow snapshots every copied fixture file before user-state actions and compares the file set and bytes afterward. Successful launch history, favorite changes, and Missing removal affect only temp-local settings/cache/catalog state. The copied `.uproject` descriptors, `Content` markers, and Missing-project marker remain unchanged. No real LocalAppData, Registry, Unreal Editor, Explorer, Visual Studio, or user project is used by the automated workflow.
