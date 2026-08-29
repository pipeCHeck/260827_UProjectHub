# UProject Hub Revised Features Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement each phase-specific plan task-by-task. This master document is an architectural roadmap; create and approve a separate detailed implementation plan for one phase before changing product code.

**Goal:** `docs/UProjectHub_features_revised.md`의 후보 기능을 기존 UProject Hub 구조에 중복 없이 통합하되, 조회·사용자 데이터 변경·프로젝트 생성물 변경·프로젝트 파일 변경·삭제를 명확히 분리해 순차적으로 제공한다.

**Architecture:** 현재 `UProjectHub.Core`의 프로젝트 모델, 파싱, 엔진 해석, 검색/필터/정렬과 `UProjectHub.Windows`의 외부 상태 경계를 유지한다. 신규 기능은 하나의 거대한 액션 서비스가 아니라 진단, 프로젝트 파일 생성, 용량 분석, 캐시 정리, 사용자 메타데이터, Git 상태 서비스로 분리하고 `UProjectHub.App`의 프로젝트 상세 화면이 이를 조합한다. 목록에는 가장 중요한 조치 필요 상태만 조용하게 표시하고, 고비용 조회는 선택/명시적 요청 시에만 실행한다.

**Tech Stack:** .NET 10 LTS, C#, WPF, `System.Text.Json`, MSTest, Windows 파일 시스템 및 프로세스 API. Git은 별도 라이브러리 대신 설치된 `git` CLI를 읽기 전용으로 호출한다.

**Spec:** `docs/UProjectHub_features_revised.md`, `docs/SPEC.md`, `docs/ARCHITECTURE.md`, `docs/UI.md`, `docs/PROJECT_DISCOVERY.md`

## Global Constraints

- 제품 동작의 기준 문서는 `docs/SPEC.md`이며, 각 단계의 구현 전에 신규 동작과 기존 MVP 비목표의 충돌을 관련 설계 문서에서 먼저 해소한다.
- 앱 시작과 목록 로드는 캐시 우선이어야 하며, 용량·Git·고급 진단 때문에 전체 프로젝트를 자동 순회하지 않는다.
- 검색, 필터, 정렬은 계속 인메모리 모델만 사용하고 디스크 작업을 시작하지 않는다.
- 한 프로젝트 또는 한 폴더의 실패가 다른 프로젝트의 조회·실행을 막지 않는다.
- 정상 행에는 `Healthy` 또는 `Ready`를 반복 표시하지 않는다.
- C++ 판정은 계속 `.uproject`의 비어 있지 않은 `Modules`만 사용한다. `Source/`는 분류 근거가 아니라 별도의 고급 진단 사실이다.
- 외부 패키지는 명확한 가치가 없으면 추가하지 않는다.
- 제품 코드가 변경되는 각 완료 작업 단위마다 `UProjectHubVersion`을 저장소 버전 규칙에 맞춰 한 번 올린다. 이 계획 문서만 작성하는 현재 단계에서는 버전을 올리지 않는다.
- 모든 제품 코드 단계는 `dotnet test UProjectHub.sln`과 `dotnet build UProjectHub.sln`을 통과하고, 영향받는 Light/Dark, Normal/Compact, 최소 지원 폭, 키보드 동작을 실제 WPF UI에서 검증한다.
- 이 로드맵 작성 단계에서는 제품 코드 수정, 앱 버전 변경, 커밋, 푸시를 하지 않는다.

---

## 1. 현재 구조와 기능별 재사용 지점

| 기능 영역 | 현재 구조 | 재사용할 부분 | 현재 부족한 부분 |
|---|---|---|---|
| Quick Actions | `ProjectActionService`, `ProjectContextActionsViewModel`, `ProjectList.xaml`, `ProcessLauncher`, `UnrealEditorLauncher`, `ExplorerLauncher`, `VisualStudioLauncher` | 명령 가용성, 안전한 `ArgumentList`, 엔진 해석, 컨텍스트 메뉴 연결 | 완료까지 기다리는 외부 프로세스 실행, 생성 결과/취소, 확인 대화상자, 엔진 전환 경계가 없음 |
| 기존 `.sln` | `VisualStudioLauncher`가 프로젝트명 일치, 단일 솔루션, 없음/복수 상태를 판정 | 기존 선택 규칙과 테스트 | 선택 로직이 private이라 진단 및 Generate 액션이 재사용할 수 없음 |
| 기본 상태 | `ProjectState`, `EngineResolutionState`, `ProjectMetadataLoader`, `EngineResolver`, `ProjectStateMessageConverter` | Missing/Broken, 엔진 Missing/Ambiguous/Unknown, 실패 격리 | 여러 사실을 우선순위·심각도·권장 액션으로 표현하는 진단 보고서가 없음 |
| 프로젝트 상세 | `ProjectInformationWindow`, `ProjectInformationViewModel` | 선택 프로젝트의 상세 진입점, 기존 테마/모션 | 진단·용량·메모·Git을 한 ViewModel에 추가하면 비대해짐 |
| 용량 분석 | `ProjectActivityDetector`의 비동기 파일 순회, 취소 패턴, `BackgroundRefreshService`, `UiUpdateBatcher` | 파일 접근 실패 격리와 취소 방식 | 폴더별 바이트, 진행률, 부분 결과, 분석 캐시가 없음 |
| 캐시 정리 | `ProjectPath`, 로깅, `ManagedProjectRemovalService` | 경로 값 객체와 결과/로그 패턴만 재사용 | 프로젝트 파일을 삭제하지 않는 기존 removal과 목적이 다르므로 별도 삭제 경계가 필요 |
| 태그/메모 | `ProjectUserState`, `AppSettings`, `JsonSettingsRepository`, 메타데이터 병합, `ProjectQueryParser`, `ProjectSearchService` | 프로젝트 경로 기반 사용자 상태, atomic settings 저장, 인메모리 검색 | 태그/메모 필드, 정규화, 편집 UI, `tag:`/`note:` 검색이 없음 |
| Git | `ProcessRequest`/`ProcessLauncher`의 안전한 인수 처리 | 프로세스 호출 원칙 | stdout/stderr/exit code/timeout을 받는 명령 실행기와 Git 모델, remote URL 해석이 없음 |
| 고급 진단 | `UProjectParser`, `UProjectDescriptor`, `InstalledEngine.RootPath`, 앱 로그 | descriptor/엔진/파일 시스템 사실 | descriptor가 `Plugins`를 보존하지 않고, 플러그인·디스크 공간·실패 흔적 정책이 없음 |
| 파생 데이터 저장 | `project-cache.json`, `engine-cache.json`, `AppDataPaths` | disposable cache 패턴과 스키마 검증 | 온디맨드 분석 결과를 독립적으로 무효화할 캐시가 없음 |

### 현재 클래스 확장 원칙

- `UnrealProject`의 기존 핵심 필드는 유지한다. 태그/메모만 검색 파이프라인에서 필요하므로 기본값이 있는 init 속성으로 추가하고, 용량·Git·진단 전체 보고서는 별도 snapshot/store에 둔다.
- `ProjectActionService`에 Generate, Switch, cleanup, Git을 모두 넣지 않는다. 기존 열기/즐겨찾기/managed-list 액션만 유지하고 기능별 App 서비스로 분리한다.
- `ApplicationCoordinator`와 `BackgroundRefreshService`에는 시작 시 자동 실행되는 고비용 작업을 추가하지 않는다. 선택 프로젝트 작업은 프로젝트 상세 ViewModel이 전용 서비스와 cancellation token을 소유한다.
- `ProjectInformationWindow`는 단계 3에서 section 기반 `ProjectDetailsWindow`로 진화시키고 Overview, Diagnostics, Storage, Notes, Source Control ViewModel을 분리한다. 모든 섹션을 한 번에 생성하거나 자동 실행하지 않는다.
- `VisualStudioLauncher`의 솔루션 탐색은 `VisualStudioSolutionLocator`로 추출해 Quick Actions와 기본 진단이 같은 판정을 사용한다.

## 2. 작업 안전 등급

| 등급 | 작업 | 프로젝트 파일 영향 | 실행 규칙 |
|---|---|---|---|
| Read-only/query | 기본·고급 진단, 용량 분석, Git 상태, remote URL 확인 | 없음 | 선택 또는 명시적 새로고침 시 실행, 취소 가능, 실패 격리 |
| External launch | Unreal/기존 `.sln`/폴더/remote URL 열기 | Hub 자체는 변경하지 않음 | 대상과 가용성을 검증하고 안전한 argument/scheme만 사용 |
| App user-data write | 즐겨찾기, 태그, 메모, 마지막 실행 시각 | `%LOCALAPPDATA%`의 settings만 변경 | atomic 저장, 프로젝트가 Missing이어도 보존 |
| Generated-file mutation | Generate Visual Studio Project Files | `.sln`, `Intermediate/ProjectFiles` 등 생성물이 바뀔 수 있음 | 엔진·`.uproject`·작업을 미리 보여주고 사용자가 실행 버튼을 눌러야 함 |
| Project descriptor mutation | Switch Unreal Engine Version | `.uproject`의 `EngineAssociation` 변경 | 별도 단계, 현재/대상/위험/백업 정책 확인 후 1회 실행, 자동 대상 선택 금지 |
| Destructive cleanup | `Intermediate/`, `DerivedDataCache/`, `.vs/` 삭제 | 선택한 생성 폴더 삭제 | 허용 목록, exact path, 개별 선택, 최종 확인, 프로젝트 루트 밖 거부 |

삭제 서비스에는 임의 경로 문자열을 전달하지 않는다. `ProjectCacheFolderKind` 같은 허용 enum에서 프로젝트의 즉시 하위 exact path를 생성하고, 정규화된 결과가 선택 프로젝트 디렉터리 내부의 기대 경로와 정확히 일치할 때만 실행한다. reparse point/junction은 순회하지 않으며, `Saved`, `Binaries`, `Content`, `Config`, `Source`, `Plugins`, `.uproject`는 API 수준에서도 선택할 수 없게 한다.

## 3. 진단 상태 설계

기존 `ProjectState`와 `EngineResolutionState`를 대체하지 않고 다음 projection을 추가한다.

- `ProjectDiagnosticReport`: 프로젝트 경로, 계산 시각, basic/advanced 수준, finding 목록, 부분 실패 목록.
- `ProjectDiagnosticFinding`: 안정적인 code, `Info`/`Warning`/`Error`, 사용자 메시지, 차단 여부, 선택적 `SuggestedAction`.
- `SuggestedAction`: OpenSettings, GenerateProjectFiles, SelectEngine, OpenFolder, Retry 등 UI 명령에 매핑 가능한 enum.
- 행 대표 상태: Error 우선, 그 다음 Warning, 그 다음 actionable Info. 같은 심각도는 고정된 code 우선순위로 결정한다.

`.sln` 미존재는 C++ 프로젝트 자체의 손상으로 보지 않는다. 엔진이 유일하게 해석되어 Generate가 가능한 경우 `Info + Actionable(GenerateProjectFiles)`로 표시하고, 경고 아이콘 대신 상세 화면/컨텍스트 메뉴의 조용한 조치 항목으로 노출한다. 엔진 미설치 때문에 생성도 불가능하면 행 대표 상태는 `.sln`이 아니라 엔진 경고가 된다. 복수 `.sln`은 현재 자동 선택이 불가능하므로 Warning으로 두고 향후 명시적 솔루션 선택 UI를 별도 범위로 판단한다.

### 기본 진단

- 프로젝트 파일/디렉터리 존재와 `ProjectState`.
- `.uproject` 파싱 성공 여부.
- 현재 `EngineResolutionState`와 resolved engine의 `IsUsable`/editor path.
- C++ 프로젝트의 솔루션 가용성: available, missing-actionable, multiple-ambiguous, inaccessible.
- 위 사실만 사용하며 재귀 플러그인/로그/용량 스캔을 하지 않는다.

### 고급 진단

- enabled plugin dependency의 project/engine 설치 여부.
- C++ descriptor module과 `Source/` 존재의 불일치 사실. 프로젝트 유형 판정은 바꾸지 않는다.
- 프로젝트 드라이브의 여유 공간 정책.
- `Saved/Crashes` 및 최신 로그에서 찾은 최근 실패 증거. 이를 확정적인 “마지막 실행 실패”로 과장하지 않는다.
- 엔진 association과 설치 후보의 상세 비교. 다른 버전으로 자동 대체하지 않는다.

## 4. 기능 의존 관계

```text
문서/안전 계약
├─ 액션 capability + 완료 대기형 process runner
│  ├─ Quick Actions 정리
│  ├─ Generate Project Files ──> .sln actionable Info
│  ├─ Git CLI 상태
│  └─ Engine Switch 사전 검증
├─ Basic Diagnostics ──────────> Advanced Diagnostics
├─ Project Size Analysis ──────> Cache Cleanup estimate/result refresh
├─ Settings mutation gate
│  └─ Project user metadata ───> Tags + Notes ──> Search
└─ Project Details shell
   ├─ Diagnostics
   ├─ Storage/Cleanup
   ├─ Tags/Notes
   └─ Git/Remote
```

## 5. 추천 구현 순서

사용자가 제안한 `Quick Actions → 기본 진단 → 용량 → 정리 → 태그/메모 → Git → 고급 진단`은 읽기 기능이 먼저 나오고, 용량 결과를 정리에 재사용한다는 점에서 대체로 적절하다. 다만 Quick Actions 안의 `Switch Unreal Engine Version`은 가장 위험한 프로젝트 descriptor 변경이므로 첫 단계에 함께 넣지 않는 편이 안전하다. 추천 순서는 다음과 같다.

1. 문서 및 안전 계약
2. Quick Actions 정리와 공통 capability/프로세스 기반
3. Generate Visual Studio Project Files
4. 기본 프로젝트 상태 진단과 Project Details shell
5. 프로젝트 용량 분석
6. 프로젝트 캐시 정리
7. 태그/메모와 검색
8. 간단한 Git 상태와 일반 remote URL 열기
9. Switch Unreal Engine Version
10. 필요한 고급 진단을 항목별로 추가

이 순서는 기존 제안을 보존하면서 descriptor 변경만 뒤로 분리한다. 각 번호는 별도의 구현 계획과 리뷰 경계로 취급한다.

## 6. 단계별 명확한 범위

### Phase 0: 명세와 안전 계약

**문서:** `docs/SPEC.md`, `docs/ARCHITECTURE.md`, `docs/UI.md`, `docs/PROJECT_DISCOVERY.md`, `README.md`, `AGENTS.md`

- [ ] MVP 비목표를 삭제하지 말고 “post-MVP explicit operations” 절을 추가해 신규 기능의 허용 범위를 구분한다.
- [ ] Read-only, app user-data, generated-file mutation, descriptor mutation, destructive cleanup의 확인/취소/로그 규칙을 문서화한다.
- [ ] Generate와 Switch가 서로 다른 영향 등급임을 명시한다.
- [ ] `.sln` missing은 기본적으로 actionable Info라는 UI 규칙을 추가한다.
- [ ] `Reveal .uproject` 제거 후 Copy Path, Favorite, Information, Missing-only Remove가 계속 남는지 확정한다. 추천안은 이 보조 액션들을 유지하는 것이다.

**검증:** 문서 간 Non-goal, context menu, safety 문구가 모순되지 않는지 교차 검토한다. 이 단계만으로는 앱 버전을 올리지 않는다.

### Phase 1: Quick Actions 구조 정리

**기존 확장:** `ProjectActionService`, `ProjectContextActionsViewModel`, `ProjectList.xaml`, `VisualStudioLauncher`, localization resources.

**신규 구조 제안:** `UProjectHub.Windows/Launching/VisualStudioSolutionLocator.cs`, `UProjectHub.App/Services/ProjectActionAvailabilityService.cs`.

- [ ] 메뉴 명칭을 Unreal에서 열기, 기존 `.sln` 열기, 프로젝트 폴더 열기로 정리하고 `Reveal .uproject`를 제거한다.
- [ ] Copy Path, Favorite, Project Information, Missing-only Remove는 보조 액션으로 유지한다.
- [ ] 솔루션 탐색 결과를 `Available(path)`, `Missing`, `Multiple(paths)`, `Inaccessible(error)`로 모델링해 launcher와 진단이 공유한다.
- [ ] Blueprint/없는 `.sln`/복수 `.sln`의 disabled reason을 tooltip 또는 상세 설명으로 제공한다.
- [ ] 기존 double-click, Enter, favorite, missing removal 동작을 변경하지 않는다.

**자동 테스트:** 프로젝트명 일치 우선, 대소문자, 단일/0개/복수 `.sln`, 접근 실패, Blueprint 비활성화, context command 가용성, Reveal 명령 제거, 기존 액션 회귀.

**실제 UI 검증:** 우클릭과 overflow 메뉴가 동일한지, disabled reason이 이해되는지, Light/Dark와 두 density에서 메뉴가 잘리지 않는지, 키보드 선택과 Enter가 유지되는지 확인한다.

### Phase 2: Generate Visual Studio Project Files

**신규 구조 제안:** `IExternalProcessRunner`/`ExternalProcessRunner`, `ProjectFileGenerationRequest`, `ProjectFileGenerationResult`, `IProjectFilesGenerator`, `UnrealProjectFilesGenerator`, `GenerateProjectFilesViewModel`, 확인 대화상자.

- [ ] C++ + Available + 유일한 usable engine일 때만 capability를 활성화한다.
- [ ] 실행 전에 engine display/root, exact `.uproject` path, 생성 작업, 예상 생성 위치를 표시한다.
- [ ] shell command 문자열이 아니라 executable과 `ArgumentList`를 분리한다.
- [ ] 완료까지 비동기로 기다리고 cancel, exit code, stdout/stderr의 제한된 tail, 시작 실패를 결과로 반환한다.
- [ ] 같은 프로젝트에 대한 중복 Generate 실행을 막되 다른 읽기 작업은 막지 않는다.
- [ ] 성공 후 `VisualStudioSolutionLocator`를 다시 실행해 액션 가용성과 기본 진단을 갱신한다.
- [ ] Unreal 버전별 공식 생성 진입점의 호환성 매트릭스를 먼저 fixture/설치 환경으로 검증한다. 한 방식이 Launcher/source/manual engine을 모두 안전하게 지원하지 않으면 engine provider별 strategy로 분리한다.

**자동 테스트:** 요청 인수, C++/엔진 조건, 취소, 시작 실패, non-zero exit, output 제한, 중복 실행, 성공 후 `.sln` 재탐색. 실제 Unreal 설치 대신 fake runner를 사용한다.

**실제 UI 검증:** 최소 한 Launcher 엔진과 가능한 경우 source/manual 엔진에서 확인 화면, 진행 중 UI 응답, 취소, 성공/실패 메시지, 생성 후 기존 `.sln` 열기 활성화를 확인한다.

### Phase 3: 기본 상태 진단과 Project Details shell

**신규 구조 제안:** `UProjectHub.Core/Diagnostics/ProjectDiagnosticFinding.cs`, `BasicProjectDiagnosticsService.cs`, App의 `ProjectDiagnosticSnapshotStore`, `ProjectDetailsWindow`, `OverviewViewModel`, `DiagnosticsViewModel`.

- [ ] 기존 `ProjectState`/`EngineState`를 입력으로 사용하고 중복 상태 enum을 만들지 않는다.
- [ ] 기본 진단은 refresh 결과와 top-level solution lookup만 사용한다.
- [ ] 행에는 가장 중요한 Error/Warning만 표시하고 actionable Info는 과도한 경고 색 없이 tooltip/details로 연결한다.
- [ ] `.sln` missing은 Generate 가능 시 actionable Info로 검증한다.
- [ ] 기존 Project Information 내용을 Overview 섹션으로 이동하고 진단 섹션을 독립 ViewModel로 둔다.
- [ ] 한 finding provider의 예외를 `Unknown/partial failure` finding으로 격리한다.

**자동 테스트:** finding severity/priority, missing/broken/engine states, `.sln` info, engine warning 우선, 정상 프로젝트의 무표시, provider failure isolation, localization key 존재.

**실제 UI 검증:** 정상 행이 조용한지, 경고가 행 전체를 지배하지 않는지, info와 warning 아이콘이 혼동되지 않는지, 상세 진입/키보드/스크롤, 네 가지 테마·density·폭 조합을 확인한다.

### Phase 4: 온디맨드 프로젝트 용량 분석

**신규 구조 제안:** `ProjectSizeAnalyzer`, `ProjectSizeAnalysis`, `ProjectFolderSize`, `ProjectSizeAnalysisIssue`, `IProjectFileTree`, `project-analysis-cache.json` repository, `StorageViewModel`.

- [ ] 선택 프로젝트의 Storage 섹션에서 사용자가 Analyze를 눌렀을 때만 실행한다.
- [ ] total과 `Content`, `DerivedDataCache`, `Intermediate`, `Saved`, `Binaries`, `Source`, `Plugins`, `.git`, `Other`를 구분한다.
- [ ] reparse point/junction을 따라가지 않고 파일 길이 합계를 checked `long`으로 계산한다.
- [ ] 접근 실패를 폴더/파일 issue로 수집하고 `Partial` 결과를 계속 표시한다.
- [ ] 진행률은 처리 항목/현재 경로/누적 용량처럼 total 사전 순회가 필요 없는 형태로 제공한다.
- [ ] 취소 시 마지막 완전 결과는 보존하고 취소된 부분 결과를 영구 캐시에 덮어쓰지 않는다.
- [ ] 결과, 계산 시각, 완료/부분 상태를 별도 disposable cache에 저장하고 UI에 cached 시각을 표시한다.
- [ ] 자동 invalidation을 위해 전체 트리를 다시 순회하지 않는다. 수동 재분석과 cleanup 후 강제 재분석을 사용한다.

**자동 테스트:** 폴더 분류, Other, 큰 파일/overflow 방지, 접근 거부 부분 결과, 취소, reparse point 무시, 캐시 schema/손상/호환성, startup에서 analyzer 미호출.

**실제 UI 검증:** 큰 disposable fixture에서 UI 응답, progress, cancel, cached timestamp, partial issue 표시, 긴 경로, 크기 단위 포맷을 확인한다.

### Phase 5: 프로젝트 캐시 정리

**신규 구조 제안:** Core의 `ProjectCacheFolderKind`, `CacheCleanupPlan`, `CacheCleanupResult`; Windows의 `ProjectCacheCleanupPlanner`, `ProjectCacheCleaner`; App의 `CacheCleanupViewModel`과 2단계 확인 대화상자.

- [ ] 대상은 `Intermediate`, `DerivedDataCache`, `.vs` enum으로만 생성한다.
- [ ] 분석 결과를 예상 확보 logical size로 재사용하되 stale 표시와 재분석 버튼을 제공한다.
- [ ] 1차 화면에서 폴더별 exact path/size를 개별 선택하고, 2차 최종 확인에서 삭제 영향과 다음 실행 지연을 표시한다.
- [ ] root containment, exact immediate child, case-insensitive canonical path, reparse point 정책을 실행 직전에 다시 검증한다.
- [ ] editor/build 사용 여부는 best-effort preflight로 안내하고, 확정할 수 없으면 삭제 중 sharing/access failure를 폴더별 결과로 반환한다.
- [ ] 취소는 다음 폴더 시작을 중단한다. 이미 삭제된 폴더를 복구된 것처럼 표시하지 않는다.
- [ ] 완료 후 폴더별 성공/실패와 삭제된 logical bytes를 표시하고 용량 분석을 다시 실행한다.

**자동 테스트:** 세 허용 폴더만 계획 가능, 프로젝트 루트/상위/형제/Content/Saved/Binaries 거부, path traversal와 junction 거부, 개별 선택, 부분 실패, 취소 경계, cleanup 후 analysis invalidation. 삭제 테스트는 임시 fixture만 사용한다.

**실제 UI 검증:** 복구 가능한 disposable 프로젝트 복사본에서만 수행한다. 선택 0개, stale estimate, 확인 취소, 일부 잠긴 폴더, 성공 후 size 갱신, 경고 문구와 기본 버튼 안전성을 확인한다.

### Phase 6: 태그와 메모

**기존 확장:** `ProjectUserState`, `AppSettings`, `JsonSettingsRepository`, `ProjectMetadataLoader`, `ApplicationCoordinator.RestoreCatalog`, `ProjectQueryParser`, `ProjectSearchService`, `SearchFilterViewModel`.

**신규 구조 제안:** process-wide `SettingsMutationService`, `ProjectUserMetadataService`, `TagTerm`, `NoteTerm`, details의 `NotesViewModel`.

- [ ] `ProjectUserState`에 tags와 note를 추가하고 null/old settings를 정상화한다.
- [ ] favorite/LastLaunched/tags/note가 동시 저장에서 서로 덮어쓰이지 않도록 모든 settings load-modify-save를 하나의 mutation gate로 직렬화한다.
- [ ] 태그는 trim, 빈 값 거부, case-insensitive 중복 제거, 원래 표시 casing 유지 규칙을 적용한다.
- [ ] `tag:value`는 case-insensitive exact tag, `note:value`는 case-insensitive contains로 검색한다.
- [ ] 첨부 문서 예시와 기존 `version:` 문법을 함께 만족하도록 `engine:`을 `version:`의 alias로 추가하고 `version:`은 유지한다.
- [ ] missing 프로젝트도 path-keyed user state를 유지한다.
- [ ] Normal 행에는 최대 소수 태그와 `+N`, Compact 행에는 태그를 숨기고 상세에서 전체를 표시한다.
- [ ] 메모 편집은 명시적 Save 또는 짧은 debounce 후 atomic 저장 중 하나로 고정한다. 추천은 명시적 Save로 실패와 저장 시점을 분명히 하는 것이다.

**자동 테스트:** old JSON 호환, null 정규화, 태그 중복/casing, note 저장, missing 보존, favorite/LastLaunched와 동시 mutation, `tag:`/`note:`/`engine:` 조합 AND 검색, malformed fallback.

**실제 UI 검증:** 태그 입력/삭제/overflow, 긴 메모, 저장 실패 안내, 재시작 보존, missing 프로젝트, Normal/Compact 목록 밀도, 실시간 검색을 확인한다.

### Phase 7: 간단한 Git 상태와 일반 remote URL

**신규 구조 제안:** `UProjectHub.Windows/Git/GitStatusService.cs`, `GitStatusSnapshot`, `RemoteRepository`, `RemoteRepositoryUrlResolver`, `IExternalProcessRunner` 재사용, App의 `SourceControlViewModel`, 안전한 `IUriLauncher`.

- [ ] 프로젝트 선택 후 Source Control 섹션이 열리거나 사용자가 Refresh를 누를 때만 `git`을 실행한다.
- [ ] `git -C <project> rev-parse --show-toplevel`로 상위 저장소에 포함된 프로젝트도 인식한다.
- [ ] porcelain status로 staged, unstaged, untracked 중 하나라도 있으면 Changed로 표시한다.
- [ ] NotRepository, Clean, Changed, Failed, GitUnavailable을 구분하고 timeout/cancel을 지원한다.
- [ ] remote는 GitHub에 한정하지 않고 이름, fetch URL, 선택적 browser URL로 모델링한다.
- [ ] `http`/`https` browser URL만 직접 연다. SSH/scp remote는 host/path를 안전하게 변환할 수 있는 표준형만 preview 가능한 HTTPS URL로 변환하고, 불확실한 형식은 비활성 reason과 raw remote를 표시한다.
- [ ] `origin`을 우선하되 여러 remote가 있고 origin이 없으면 자동으로 임의 선택하지 않고 사용자가 선택하게 한다.
- [ ] commit, push, pull, fetch, checkout, branch 변경은 제공하지 않는다.

**자동 테스트:** non-repo, clean, staged/unstaged/untracked, 상위 repo, git 없음, timeout/cancel/non-zero exit, origin/복수 remote, HTTPS/SSH/scp/invalid URL, scheme allowlist, startup 무호출.

**실제 UI 검증:** 임시 Git 저장소와 GitHub가 아닌 HTTPS remote 예시에서 상태/새로고침/URL preview/open을 확인하고, 네트워크 접근 없이도 상태 조회가 완료되는지 확인한다.

### Phase 8: Switch Unreal Engine Version

이 기능은 Quick Actions 명칭에는 포함되지만 descriptor mutation이므로 독립 계획 두 개로 분할한다.

#### Phase 8A: 전환 방식 검증

- [ ] Launcher numeric association, registered source GUID, manual engine의 target association 표현을 fixture로 정리한다.
- [ ] 공식 UnrealVersionSelector의 silent switch가 지원 engine 유형과 버전에서 어떤 파일을 변경하고 어떤 exit/result를 주는지 disposable project로 검증한다.
- [ ] 공식 도구가 모든 지원 유형을 신뢰성 있게 처리하지 못하면, unknown JSON property를 보존하고 backup+atomic replace를 사용하는 제한된 `EngineAssociation` 변경 방식의 장단점을 문서화한다.
- [ ] 검증 결과로 한 가지 방식 또는 provider별 strategy를 확정한 뒤 Phase 8B 세부 계획을 작성한다.

#### Phase 8B: 사용자 확인형 전환

- [ ] 현재 association, target engine display/association/root, 호환성 경고, backup 위치, 실행 작업을 표시한다.
- [ ] target을 자동 선택하지 않고 usable engine 목록에서 사용자가 명시적으로 선택한다.
- [ ] 현재와 같은 association, unavailable/ambiguous target, missing project를 거부한다.
- [ ] 확인 체크와 실행 버튼 뒤에만 descriptor mutation을 수행한다.
- [ ] 실패 시 원본 보존 또는 검증된 backup 복원을 보장하고 결과를 로그/화면에 표시한다.
- [ ] 성공 후 descriptor 재파싱, engine resolution, basic diagnostics, cache를 갱신한다. 프로젝트 콘텐츠 변환이나 Editor 자동 실행은 하지 않는다.

**자동 테스트:** numeric/GUID target, 같은 target 거부, 명시적 선택, 확인 전 무변경, backup/atomic failure, malformed descriptor, cancel, 성공 후 refresh, 다른 JSON property 보존.

**실제 UI 검증:** disposable project 복사본에서만 현재/대상/경고/취소/성공/실패/backup을 확인한다. 전환 직후 Unreal Editor를 자동으로 열지 않는지 확인한다.

### Phase 9: 고급 진단을 세 개의 독립 작업으로 분할

#### Phase 9A: 플러그인 의존성

- [ ] `UProjectDescriptor`에 enabled plugin reference를 보존한다.
- [ ] project `Plugins`와 resolved engine plugin roots에서 `.uplugin` 이름을 찾되 시작/목록 로드 때 자동 재귀 스캔하지 않는다.
- [ ] 확실한 Missing과 접근 실패/판정 불가를 구분한다.

#### Phase 9B: 디스크 여유 공간

- [ ] project path의 volume을 안전하게 해석하고 free/total bytes를 읽는다.
- [ ] 고정 warning threshold를 제품 문서에 명시한 뒤 적용하고, volume 정보 판정 실패를 다른 진단과 격리한다.

#### Phase 9C: 최근 실패 증거와 Source 불일치

- [ ] `Saved/Crashes`와 최신 제한 개수 로그의 명확한 실패 marker만 읽고 시간/근거 경로를 표시한다.
- [ ] C++ modules인데 `Source/`가 없으면 분류를 바꾸지 않고 advanced finding으로만 표시한다.
- [ ] 오래되거나 모호한 흔적은 Warning이 아니라 Info/Unknown으로 낮춘다.

**공통 자동 테스트:** provider별 fixture, 접근 실패 격리, cancellation, report merge/priority, basic-only 실행에서 advanced provider 미호출.

**공통 실제 UI 검증:** Run advanced diagnostics의 명시적 실행, 진행/취소, 부분 결과, 근거 표시, 오래된 결과 시각, 행 대표 상태가 과도하게 변하지 않는지 확인한다.

## 7. 단계별 문서·테스트·UI 완료 기준

각 제품 코드 단계는 다음 순서로 닫는다.

1. 해당 동작의 source-of-truth 문서와 non-goal/safety 문구를 수정한다.
2. Core 정책과 Windows 외부 경계에 실패하는 fixture/fake 기반 테스트를 먼저 추가한다.
3. 최소 구현 후 해당 테스트를 실행한다.
4. App ViewModel/command/localization/presentation resource 테스트를 추가한다.
5. `dotnet test UProjectHub.sln`을 실행한다.
6. `dotnet build UProjectHub.sln`을 실행한다.
7. 기능별 실제 UI 검증과 공통 Light/Dark, Normal/Compact, narrow width, keyboard 검증을 수행한다.
8. 임시 파일, 실제 프로젝트 경로, debug output이 남지 않았는지 확인한다.
9. `UProjectHubVersion`을 작업 성격에 맞춰 한 번 올리고 표시 버전을 확인한다.
10. 해당 단계가 독립적으로 사용 가능하고 다음 단계 없이도 회귀가 없는 상태에서만 완료한다.

## 8. 위험 요소와 애매한 요구사항

| 항목 | 위험/애매함 | 추천 결정 |
|---|---|---|
| Quick Actions 목록 | 다섯 주요 액션만 남기는지, Copy/Favorite/Information/Remove도 제거하는지 불명확 | `Reveal .uproject`만 제거하고 안전한 보조 액션은 유지 |
| `.sln` 없음 | 경고로 표시하면 정상적인 생성 전 C++ 프로젝트를 과도하게 문제시함 | Generate 가능 시 actionable Info, 엔진 문제는 별도 Warning/Error |
| Generate 진입점 | UE 버전·Launcher/source/manual 구성에 따라 실행 파일/인수가 다를 수 있음 | 구현 전 호환성 매트릭스, provider별 strategy 허용 |
| 복수 `.sln` | 현재 자동 선택 불가 | Warning + disabled reason; 솔루션 선택 UI는 별도 작은 단계로 분리 가능 |
| Switch 방식 | 공식 selector와 직접 JSON 변경의 변경 범위/호환성이 다름 | Phase 8A 검증을 의무화하고 첫 Quick Actions 단계에서 제외 |
| 용량 total | `.git`, 접근 불가 파일, junction, sparse/hardlink의 물리 용량 의미가 다름 | junction 미순회, logical file bytes와 partial 여부를 명시 |
| 실제 확보 용량 | 삭제 전 합계와 실제 볼륨 free-space 증가는 압축/동시 작업 때문에 다름 | 결과를 “삭제된 logical bytes”로 명명하고 필요 시 volume delta를 보조 표시 |
| 분석 캐시 stale | subtree 변경을 값싸게 완전 감지할 수 없음 | cached timestamp + 수동 재분석, cleanup 후 강제 재분석 |
| 사용 중 프로세스 | 모든 Unreal/VS/build lock을 사전 판정하기 어려움 | best-effort 안내 + 폴더별 sharing/access failure, 강제 종료 기능 없음 |
| 태그/메모 한도 | 자유 입력의 길이/개수/저장 시점이 미정 | 상세 계획에서 합리적 상한과 명시적 Save를 문서화 |
| 검색 `engine:` | 기존 표준은 `version:`, 첨부 예시는 `engine:` | `engine:` alias 추가, `version:` 유지 |
| Git remote | origin이 없거나 SSH URL이 web URL과 다를 수 있음 | raw remote와 optional browser URL 분리, 불확실하면 열기 비활성화 |
| Git dirty 의미 | staged/unstaged/untracked/submodule 포함 범위가 미정 | 초기에는 staged+unstaged+untracked를 Changed, submodule 세부는 제외 |
| 플러그인 누락 | engine/project/marketplace plugin과 버전·플랫폼 조건 때문에 오탐 가능 | 확실한 enabled reference만 검사하고 Unknown을 별도 표현 |
| 드라이브 부족 기준 | 절대/비율 임계치가 미정 | Phase 9B 명세에서 고정 정책을 먼저 승인 |
| 마지막 실패 | 로그/crash 흔적은 마지막 실행의 성공 여부를 완전히 증명하지 못함 | “최근 실패 증거”로 명명하고 근거/시각 표시 |
| 설정 동시 저장 | 현재 여러 서비스가 load-modify-save하면 태그 도입 후 lost update 위험 | 공용 settings mutation gate를 Phase 6에 도입 |

## 9. 추가 분할 권고

- Quick Actions는 `메뉴/가용성`, `Generate`, `Switch` 세 계획으로 분리한다.
- Project Details shell과 basic diagnostics는 함께 만들되 Storage, Notes, Source Control 섹션은 각 단계에서 lazy 추가한다.
- 태그/메모는 공통 persistence 작업 뒤 `태그+검색`, `메모+검색` 두 reviewable task로 나눈다.
- 캐시 정리는 `안전한 plan 생성`, `삭제 executor`, `확인 UI와 재분석` 세 task로 나눈다.
- Git은 `명령 실행/상태`, `remote URL`, `UI` 세 task로 나눈다.
- 고급 진단은 플러그인, 디스크 공간, 실패 흔적을 한 릴리스에 묶지 않는다. 기본 진단 사용 경험에서 실제 필요성이 확인된 항목만 추가한다.
- 각 phase-specific plan은 정확한 파일, 공개 interface, fixture, failing test, 최소 구현, 검증 명령, 버전 bump 한 번을 포함해야 한다.

## 10. 최종 추천

현재 코드 구조상 사용자가 제안한 순서는 대체로 맞다. 가장 중요한 조정은 `Switch Unreal Engine Version`을 초기 Quick Actions에서 분리하고, Generate와 cleanup 전에 공통 안전/외부 프로세스 경계를 세우는 것이다. `.sln` 없음은 손상 경고가 아니라 “C++ 프로젝트 파일을 생성할 수 있음”이라는 actionable Info로 설계한다. 기본 진단은 기존 `ProjectState`와 `EngineResolutionState`를 조합하는 저비용 projection으로 시작하고, 플러그인·디스크·실패 흔적은 사용자가 요청할 때만 실행되는 독립 advanced provider로 늦춘다.

다음 구현 단계로는 Phase 0 문서 정합성과 Phase 1 Quick Actions 구조 정리만 별도 세부 계획으로 작성하는 것이 가장 작은 안전한 범위다.
