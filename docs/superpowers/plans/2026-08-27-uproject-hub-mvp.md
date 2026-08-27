# UProject Hub MVP Implementation Plan

- 작성일: 2026-08-27
- 상태: 구현 실행 준비 완료
- 범위: `docs/SPEC.md`에 정의된 MVP 전체
- 구현 플랫폼: C# / .NET 10 LTS / `net10.0-windows` / WPF / MVVM
- 제품 프로젝트: `UProjectHub.Core`, `UProjectHub.Windows`, `UProjectHub.App`
- 테스트 프로젝트: `UProjectHub.Core.Tests`

이 문서는 다음 문서를 Source of Truth 순서대로 소비한다.

1. `docs/SPEC.md`
2. `docs/ARCHITECTURE.md`
3. `docs/UI.md`
4. `docs/PROJECT_DISCOVERY.md`
5. `AGENTS.md`
6. `README.md`

이 계획을 작성하는 단계에서는 `.sln`, `.csproj`, `.cs`, `.xaml`을 생성하지 않는다. 아래 파일 경로는 각 Task를 실행할 때 생성하거나 수정할 정확한 대상이다.

## 1. 실행 전제와 공통 완료 규칙

### 도구 전제

Task 1을 시작하기 전에 다음 조건을 만족해야 한다.

- `dotnet --list-sdks`에 `10.0.400` 또는 그 이후의 .NET 10 SDK 패치가 표시된다.
- Visual Studio 2026 18.0 이상과 .NET Desktop Development workload가 설치되어 있다.
- 저장소 작업 트리가 깨끗하다.

전제가 충족되지 않으면 저장소 파일을 변경하기 전에 도구를 설치하고 다시 확인한다.

### 공통 TDD 순서

Core 동작을 추가하는 모든 Task는 다음 순서를 지킨다.

1. fixture 또는 단위 테스트를 먼저 작성한다.
2. 해당 테스트만 실행해 의도한 이유로 실패하는지 확인한다.
3. 테스트를 만족하는 최소 구현을 작성한다.
4. 해당 테스트를 다시 실행해 통과를 확인한다.
5. `dotnet test UProjectHub.sln`을 실행한다.
6. `dotnet build UProjectHub.sln`을 실행한다.
7. 관련 설계 문서와 `git diff --check`를 확인한다.
8. Task 범위만 하나의 커밋으로 남길 수 있는 상태에서 종료한다.

WPF 또는 Windows 통합 Task도 가능한 순수 로직은 `UProjectHub.Core.Tests`에서 fake/fixture로 검증하고, OS 또는 시각 동작은 명시된 수동 확인을 추가한다.

설계 문서의 단일 테스트 project 구조를 유지하기 위해 Windows/App adapter test도 `UProjectHub.Core.Tests`의 `Windows/`, `App/` 하위 폴더에 둔다. 이 테스트 project가 제품 assembly의 참조 방향을 바꾸지는 않으며 Core 회귀 테스트가 여전히 주 대상이다.

### 전역 아키텍처 규칙

- 참조 방향은 `App -> Windows -> Core` 및 `App -> Core`만 허용한다.
- `Core`는 WPF, Registry, `Process.Start`, `%LOCALAPPDATA%`를 알지 못한다.
- `Windows`는 WPF View/ViewModel을 알지 못한다.
- WPF code-behind는 `InitializeComponent`, drag/drop 및 focus 같은 presentation event 전달만 담당한다.
- 검색, 필터, 정렬은 메모리의 `UnrealProject` 컬렉션만 소비한다.
- Motion resource와 system-preference adapter는 `UProjectHub.App`에만 두며 Core/Windows domain service에 animation 상태를 전달하지 않는다.
- 검색, 필터, 정렬, list reorder의 data state는 Motion을 기다리지 않고 즉시 반영한다.
- 재귀 파일 탐색, 엔진 검색, cache 검증은 UI thread 밖에서 실행하고 `CancellationToken`을 받는다.
- 프로젝트 파일을 수정하거나 삭제하는 API는 제품 코드에 만들지 않는다.
- 런타임 외부 패키지는 추가하지 않는다. 테스트 인프라에는 SDK 템플릿이 선택한 MSTest 패키지만 사용한다.

## 2. 고정 타입 및 인터페이스 계약

Task 사이의 이름과 책임은 아래 계약을 사용한다.

| 소유 프로젝트 | 타입 | 계약 |
|---|---|---|
| Core | `UnrealProject` | 표시·검색·정렬에 필요한 프로젝트 snapshot |
| Core | `ProjectType` | `Cpp`, `Blueprint` |
| Core | `ProjectState` | `Available`, `Missing`, `Broken` |
| Core | `EngineResolutionState` | `Resolved`, `Missing`, `Ambiguous`, `Unknown` |
| Core | `InstalledEngine` | association, display version, root/editor path, source, usable 여부 |
| Core | `EngineResolution` | 상태, 유일한 resolved candidate 또는 matching candidates |
| Core | `UProjectDescriptor` | `FileVersion`, `EngineAssociation`, `Modules`의 파싱 결과 |
| Core | `IUProjectParser` / `UProjectParser` | 한 `.uproject`를 예외 전파 없이 `UProjectParseResult`로 변환 |
| Core | `ProjectClassifier` | descriptor의 `Modules`만으로 `ProjectType` 결정 |
| Core | `EngineVersion` / `EngineVersionComparer` | 숫자형 Unreal 버전 파싱 및 semantic 비교 |
| Core | `ProjectQueryParser` | 검색 문자열을 `ProjectQuery`와 `ProjectQueryTerm` 목록으로 변환 |
| Core | `ProjectSearchService` | plain/structured term을 프로젝트 snapshot에 적용 |
| Core | `ProjectFilterService` | visible filter를 AND 방식으로 적용 |
| Core | `ProjectSortService` | stable secondary name ordering을 포함한 정렬 |
| Core | `IClock` / `SystemClock` | rolling `modified:Nd` 및 상대 시간의 현재 시각 경계 |
| Core | `ProjectActivityDetector` | meaningful file의 최신 UTC timestamp 계산 |
| Core | `ISettingsRepository` | `AppSettings` 로드 및 atomic 저장 |
| Core | `IProjectCacheRepository` | `ProjectCacheDocument` 로드 및 교체 저장 |
| Core | `IEngineCacheRepository` | `EngineCacheDocument` 로드 및 교체 저장 |
| Core | `ProjectCatalog` | canonical path 기반의 메모리 프로젝트 집합 |
| Core | `ManagedProjectRemovalService` | Missing 항목의 manager data만 제거 |
| Core | `ProjectRootScanner` | configured root에서 `.uproject` candidate 탐색 |
| Core | `ProjectMetadataLoader` | parser/classifier/activity를 조합해 `UnrealProject` 생성 |
| Core | `ProjectRefreshService` | known project 검증 및 incremental update 생산 |
| Core | `ProjectRescanService` | 명시적 root scan과 catalog upsert 수행 |
| Core | `EngineResolver` | numeric major/minor 및 exact GUID 해석 |
| Core | `IAppLogger` | `Info`, `Warning`, `Error`의 좁은 logging 경계 |
| Windows | `ILocalAppDataPathProvider` / `LocalAppDataPathProvider` | `%LOCALAPPDATA%\UProjectHub` 파일 경로 제공 |
| Windows | `IUnrealKnownProjectRootProvider` | Unreal 설정에서 known project root 제공 |
| Windows | `IEngineProvider` | normalized `InstalledEngine` 후보 제공 |
| Windows | `LauncherEngineProvider` | Epic Launcher 설치 metadata 처리 |
| Windows | `SourceBuildEngineProvider` | HKCU Unreal Builds 등록 처리 |
| Windows | `ManualEngineProvider` | settings의 manual root 검증 |
| Windows | `EngineDiscoveryService` | provider 오류를 격리하고 물리적으로 같은 editor path를 중복 제거 |
| Windows | `IProcessLauncher` / `ProcessLauncher` | 안전한 argument list로 process 시작 |
| Windows | `UnrealEditorLauncher` | resolved editor로 `.uproject` 실행 |
| Windows | `ExplorerLauncher` | folder 열기 및 `.uproject` reveal |
| Windows | `VisualStudioLauncher` | 기존 `.sln`만 열기 |
| Windows | `RollingFileLogger` | bounded human-readable text log |
| App | `AppBootstrapper` | 수동 composition root; DI package 없음 |
| App | `ApplicationCoordinator` | cache-first startup, refresh/rescan, shutdown cancellation |
| App | `MainViewModel` | 화면 영역 ViewModel 조합 |
| App | `ProjectListViewModel` / `ProjectRowViewModel` | list state와 row command 노출 |
| App | `SearchFilterViewModel` | 검색·visible filter·sort state 노출 |
| App | `SettingsViewModel` | root/manual engine/theme/density 설정 편집 |
| App | `ProjectActionService` | context action을 Core/Windows 서비스에 위임 |
| App | `ThemeService` | semantic resource dictionary와 density 적용 |
| App | `MotionService` | centralized motion token과 Windows animation preference를 App presentation에 적용 |
| App | `BackgroundRefreshService` | 장시간 작업과 짧은 Dispatcher update 분리 |

## 3. Task 계획

### Task 1. Solution 및 project scaffold

**목표:** 참조 방향과 빌드 설정이 고정된 최소 실행 가능 solution을 만든다.

**생성 파일**

- `global.json`
- `Directory.Build.props`
- `UProjectHub.sln`
- `src/UProjectHub.Core/UProjectHub.Core.csproj`
- `src/UProjectHub.Windows/UProjectHub.Windows.csproj`
- `src/UProjectHub.App/UProjectHub.App.csproj`
- `src/UProjectHub.App/App.xaml`
- `src/UProjectHub.App/App.xaml.cs`
- `src/UProjectHub.App/MainWindow.xaml`
- `src/UProjectHub.App/MainWindow.xaml.cs`
- `tests/UProjectHub.Core.Tests/UProjectHub.Core.Tests.csproj`
- `tests/UProjectHub.Core.Tests/SolutionSmokeTests.cs`
- `scripts/build.ps1`
- `scripts/test.ps1`
- `scripts/run.ps1`

**소비:** .NET 10 SDK, WPF workload, 문서의 project boundary.

**제공:** `App -> Core`, `App -> Windows`, `Windows -> Core`, test -> Core/Windows 참조 구조. 모든 project는 `net10.0-windows`; `UseWPF`는 App에만 설정한다.

**실행 순서**

1. `global.json`을 SDK `10.0.400`, `rollForward: latestPatch`로 고정한다.
2. nullable, implicit usings, warnings-as-errors를 `Directory.Build.props`에 설정한다.
3. SDK 템플릿으로 solution/project를 만들고 template business code는 남기지 않는다.
4. minimal WPF window와 solution smoke test만 유지한다.

**검증**

- `dotnet restore UProjectHub.sln` → restore 성공.
- `dotnet test UProjectHub.sln` → smoke test 통과.
- `dotnet build UProjectHub.sln` → 경고와 오류 0.
- `dotnet list src/UProjectHub.App/UProjectHub.App.csproj reference`와 `dotnet list src/UProjectHub.Windows/UProjectHub.Windows.csproj reference` → 역방향 참조 없음.

**커밋 경계:** scaffold와 build/test/run scripts만 포함한다.

### Task 2. Core project/engine model과 path identity

**목표:** 이후 subsystem이 공유하는 불변 snapshot과 Windows path identity 규칙을 고정한다.

**생성 파일**

- `src/UProjectHub.Core/Models/UnrealProject.cs`
- `src/UProjectHub.Core/Models/ProjectType.cs`
- `src/UProjectHub.Core/Models/ProjectState.cs`
- `src/UProjectHub.Core/Models/EngineResolutionState.cs`
- `src/UProjectHub.Core/Models/EngineSource.cs`
- `src/UProjectHub.Core/Models/InstalledEngine.cs`
- `src/UProjectHub.Core/Paths/ProjectPath.cs`
- `tests/UProjectHub.Core.Tests/Models/UnrealProjectTests.cs`
- `tests/UProjectHub.Core.Tests/Paths/ProjectPathTests.cs`

**소비:** Task 1 project structure.

**제공:** `UnrealProject`, enum 4종, `InstalledEngine`, canonical/ordinal-ignore-case `ProjectPath`.

**TDD 및 검증**

1. 동일 경로의 slash/case/relative 표현이 같은 identity가 되는 테스트를 작성한다.
2. `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ProjectPathTests"` → 타입 부재로 실패.
3. 최소 model/path 구현 후 같은 명령 → 통과.
4. `dotnet test UProjectHub.sln` 및 `dotnet build UProjectHub.sln` → 전체 통과, 경고 0.

**커밋 경계:** domain model과 path identity만 포함한다.

### Task 3. `.uproject` parser와 C++/Blueprint 판정

**목표:** JSON descriptor를 안전하게 읽고 `Modules` 규칙만으로 project type을 결정한다.

**생성 파일**

- `src/UProjectHub.Core/Parsing/IUProjectParser.cs`
- `src/UProjectHub.Core/Parsing/UProjectParser.cs`
- `src/UProjectHub.Core/Parsing/UProjectDescriptor.cs`
- `src/UProjectHub.Core/Parsing/UProjectModule.cs`
- `src/UProjectHub.Core/Parsing/UProjectParseResult.cs`
- `src/UProjectHub.Core/Parsing/ProjectClassifier.cs`
- `tests/UProjectHub.Core.Tests/Parsing/UProjectParserTests.cs`
- `tests/UProjectHub.Core.Tests/Parsing/ProjectClassifierTests.cs`
- `tests/UProjectHub.Core.Tests/Fixtures/Projects/Cpp/Cpp.uproject`
- `tests/UProjectHub.Core.Tests/Fixtures/Projects/BlueprintEmptyModules/BlueprintEmptyModules.uproject`
- `tests/UProjectHub.Core.Tests/Fixtures/Projects/BlueprintMissingModules/BlueprintMissingModules.uproject`
- `tests/UProjectHub.Core.Tests/Fixtures/Projects/BlueprintSourceOnly/BlueprintSourceOnly.uproject`
- `tests/UProjectHub.Core.Tests/Fixtures/Projects/BlueprintSourceOnly/Source/Marker.txt`
- `tests/UProjectHub.Core.Tests/Fixtures/Projects/Malformed/Malformed.uproject`

**소비:** `ProjectType`.

**제공:** `IUProjectParser.ParseAsync`, `UProjectParseResult`, `ProjectClassifier.Classify`.

**회귀 사례**

- `Modules` 1개 이상 → `Cpp`.
- 빈 `Modules` → `Blueprint`.
- `Modules` 미존재 → `Blueprint`.
- `Source` 폴더가 있어도 `Modules` 미존재 → `Blueprint`.
- malformed JSON → exception 대신 실패 result.

**TDD 및 검증**

1. 위 fixture 테스트를 먼저 작성한다.
2. `dotnet test UProjectHub.sln --filter "FullyQualifiedName~UProjectParserTests|FullyQualifiedName~ProjectClassifierTests"` → parser/classifier 부재로 실패.
3. `System.Text.Json` 최소 구현 후 같은 명령 → 5개 회귀 사례 통과.
4. 전체 `dotnet test`와 `dotnet build` → 통과.

**커밋 경계:** descriptor parsing과 classification만 포함한다.

### Task 4. Unreal engine version parsing과 semantic comparison

**목표:** 숫자 버전의 비교와 표시 정규화를 lexical sort 없이 제공한다.

**생성 파일**

- `src/UProjectHub.Core/Versions/EngineVersion.cs`
- `src/UProjectHub.Core/Versions/EngineVersionComparer.cs`
- `tests/UProjectHub.Core.Tests/Versions/EngineVersionTests.cs`
- `tests/UProjectHub.Core.Tests/Versions/EngineVersionComparerTests.cs`

**소비:** raw engine display/association string.

**제공:** `EngineVersion.TryParse`, major/minor/patch 값, `EngineVersionComparer`.

**TDD 및 검증**

1. `5.9 < 5.10`, `5.8 < 5.8.1`, numeric/non-numeric/null ordering 테스트를 작성한다. 인식 불가 값은 ascending에서 numeric 뒤, null은 마지막으로 고정한다.
2. focused test 실행 → 실패.
3. component-wise numeric comparison 구현 후 focused test → 통과.
4. 전체 test/build → 통과.

**커밋 경계:** version value와 comparer만 포함한다.

### Task 5. Structured query parser

**목표:** 검색 문자열을 복구 가능한 query term으로 tokenizing/parsing한다.

**생성 파일**

- `src/UProjectHub.Core/Searching/ProjectQuery.cs`
- `src/UProjectHub.Core/Searching/ProjectQueryTerm.cs`
- `src/UProjectHub.Core/Searching/ProjectQueryParser.cs`
- `tests/UProjectHub.Core.Tests/Searching/ProjectQueryParserTests.cs`

**소비:** raw search text.

**제공:** `ProjectQueryParser.Parse(string)`, `PlainTextTerm`, `VersionTerm`, `ProjectTypeTerm`, `PathTerm`, `ModifiedWithinTerm`, `FavoriteTerm`.

**회귀 사례**

- `version:5.8`, `type:cpp`, `type:bp`, `path:Game`, `modified:7d`, `favorite:true`.
- `path:"D:\Game Academy"`를 하나의 path term으로 처리.
- unknown prefix와 invalid known token을 원문 그대로 plain-text term으로 fallback.
- 한 token의 오류가 나머지 term parsing을 중단하지 않음.

**TDD 및 검증**

1. 회귀 테스트 작성 후 `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ProjectQueryParserTests"` → 실패.
2. quote-aware tokenizer와 prefix parser 최소 구현.
3. focused test → 통과.
4. 전체 test/build → 통과.

**커밋 경계:** query syntax model/parser만 포함한다.

### Task 6. In-memory search와 visible filter

**목표:** parsed query와 toolbar filter를 disk 접근 없이 AND 방식으로 적용한다.

**생성 파일**

- `src/UProjectHub.Core/Time/IClock.cs`
- `src/UProjectHub.Core/Time/SystemClock.cs`
- `src/UProjectHub.Core/Searching/ProjectSearchService.cs`
- `src/UProjectHub.Core/Filtering/ProjectFilter.cs`
- `src/UProjectHub.Core/Filtering/ProjectFilterService.cs`
- `tests/UProjectHub.Core.Tests/Time/FakeClock.cs`
- `tests/UProjectHub.Core.Tests/Searching/ProjectSearchServiceTests.cs`
- `tests/UProjectHub.Core.Tests/Filtering/ProjectFilterServiceTests.cs`

**소비:** `UnrealProject`, `ProjectQuery`, `IClock`.

**제공:** `ProjectSearchService.Matches`, `ProjectFilterService.Matches`, immutable `ProjectFilter`.

**회귀 사례**

- plain text가 name/path/version/type을 case-insensitive 검색.
- `version:`, `type:`, `path:`, `favorite:` 각각 및 조합.
- `modified:7d`가 현재 시각에서 정확히 168시간 경계 포함.
- quote-aware parser 결과인 `path:"D:\Game Academy"`가 해당 경로 project를 실제로 match.
- unknown/malformed token이 `PlainTextTerm`으로 동일 plain-text matcher를 통과.
- visible Engine/Type/Favorites filter와 query가 AND로 결합.
- search/filter가 filesystem service를 소비하지 않음.

**TDD 및 검증**

1. fake clock 기반 테스트 작성 후 focused test → 실패.
2. matcher/filter 최소 구현 후 focused test → 통과.
3. 전체 test/build → 통과.

**커밋 경계:** in-memory matching과 filter만 포함한다.

### Task 7. Project sorting

**목표:** 모든 SPEC sort column과 stable secondary order를 구현한다.

**생성 파일**

- `src/UProjectHub.Core/Sorting/ProjectSortColumn.cs`
- `src/UProjectHub.Core/Sorting/SortDirection.cs`
- `src/UProjectHub.Core/Sorting/ProjectSortDefinition.cs`
- `src/UProjectHub.Core/Sorting/ProjectSortService.cs`
- `tests/UProjectHub.Core.Tests/Sorting/ProjectSortServiceTests.cs`

**소비:** `UnrealProject`, `EngineVersionComparer`.

**제공:** `ProjectSortService.Sort(IEnumerable<UnrealProject>, ProjectSortDefinition)`.

**회귀 사례**

- Name, Engine Version, Project Type, Last Modified, Last Launched 양방향.
- `5.10`이 `5.9` 뒤에 정렬.
- primary 값 동일 시 project name ascending.
- 기본값 Last Modified descending.

**TDD 및 검증**

1. 정렬 회귀 테스트 작성 → focused test 실패.
2. stable comparer chain 구현 → focused test 통과.
3. 전체 test/build → 통과.

**커밋 경계:** sort state와 service만 포함한다.

### Task 8. Meaningful project activity detection

**목표:** 포함/제외 규칙을 지키며 최신 meaningful UTC timestamp를 계산한다.

**생성 파일**

- `src/UProjectHub.Core/Activity/ProjectActivityDetector.cs`
- `src/UProjectHub.Core/Activity/ProjectActivityPolicy.cs`
- `tests/UProjectHub.Core.Tests/Activity/ProjectActivityDetectorTests.cs`
- `tests/UProjectHub.Core.Tests/Fixtures/Activity/ActivityProject/ActivityProject.uproject`
- `tests/UProjectHub.Core.Tests/Fixtures/Activity/ActivityProject/Content/Asset.uasset`
- `tests/UProjectHub.Core.Tests/Fixtures/Activity/ActivityProject/Config/DefaultGame.ini`
- `tests/UProjectHub.Core.Tests/Fixtures/Activity/ActivityProject/Source/Game.cpp`
- `tests/UProjectHub.Core.Tests/Fixtures/Activity/ActivityProject/Plugins/TestPlugin/Content/PluginAsset.uasset`
- `tests/UProjectHub.Core.Tests/Fixtures/Activity/ActivityProject/Saved/Logs/Latest.log`
- `tests/UProjectHub.Core.Tests/Fixtures/Activity/ActivityProject/Intermediate/Generated.txt`

**소비:** project file path, cancellation token.

**제공:** `ProjectActivityDetector.GetLastModifiedUtcAsync`.

**회귀 사례**

- `Saved`와 `Intermediate`가 가장 새로워도 결과에 영향 없음.
- `Content` 변경은 결과에 반영.
- `.uproject`, Config, Source, Plugins 포함.
- excluded directory segment 및 reparse point를 재귀 진입하지 않음.

**TDD 및 검증**

1. temp copy의 timestamp를 명시적으로 설정하는 fixture 테스트 작성 → 실패.
2. enumeration policy 최소 구현 → focused test 통과.
3. 전체 test/build → 통과.

**커밋 경계:** activity policy/detector와 fixture만 포함한다.

### Task 9. User settings model과 atomic persistence

**목표:** disposable cache와 분리된 user-owned settings를 안전하게 저장한다.

**생성 파일**

- `src/UProjectHub.Core/Settings/AppSettings.cs`
- `src/UProjectHub.Core/Settings/ThemeMode.cs`
- `src/UProjectHub.Core/Settings/RowDensity.cs`
- `src/UProjectHub.Core/Settings/VisibleFilterState.cs`
- `src/UProjectHub.Core/Settings/ColumnLayoutState.cs`
- `src/UProjectHub.Core/Settings/ProjectUserState.cs`
- `src/UProjectHub.Core/Settings/ISettingsRepository.cs`
- `src/UProjectHub.Core/Settings/JsonSettingsRepository.cs`
- `src/UProjectHub.Core/Storage/AtomicJsonFileWriter.cs`
- `tests/UProjectHub.Core.Tests/Settings/JsonSettingsRepositoryTests.cs`

**소비:** `ProjectSortDefinition`, `ProjectPath`, `System.Text.Json`.

**제공:** roots, manual engine roots, favorites/last-launched, theme, density, sort, visible filters, column state를 가진 `AppSettings`; atomic temp/validate/replace/backup 저장.

**TDD 및 검증**

1. default load, round-trip, corrupt primary + valid backup, atomic replacement 실패 시 primary 보존 테스트 작성 → 실패.
2. repository 최소 구현 → focused test 통과.
3. settings와 cache type이 서로 참조하지 않는지 review.
4. 전체 test/build → 통과.

**커밋 경계:** settings schema와 persistence만 포함한다.

### Task 10. Project/engine cache persistence

**목표:** 즉시 시작에 필요한 disposable derived cache를 별도 문서로 저장한다.

**생성 파일**

- `src/UProjectHub.Core/Cache/ProjectCacheEntry.cs`
- `src/UProjectHub.Core/Cache/ProjectCacheDocument.cs`
- `src/UProjectHub.Core/Cache/EngineCacheEntry.cs`
- `src/UProjectHub.Core/Cache/EngineCacheDocument.cs`
- `src/UProjectHub.Core/Cache/IProjectCacheRepository.cs`
- `src/UProjectHub.Core/Cache/IEngineCacheRepository.cs`
- `src/UProjectHub.Core/Cache/JsonProjectCacheRepository.cs`
- `src/UProjectHub.Core/Cache/JsonEngineCacheRepository.cs`
- `tests/UProjectHub.Core.Tests/Cache/ProjectCacheRepositoryTests.cs`
- `tests/UProjectHub.Core.Tests/Cache/EngineCacheRepositoryTests.cs`

**소비:** Core models, `AtomicJsonFileWriter`.

**제공:** cache load/save 계약, schema version, corrupt cache를 empty document로 폐기하는 동작.

**TDD 및 검증**

1. round-trip, schema mismatch, corrupt JSON, partial project entry 테스트 작성 → 실패.
2. cache repository 최소 구현 → focused test 통과.
3. settings의 favorite/last-launched가 derived cache에만 존재하지 않는지 확인.
4. 전체 test/build → 통과.

**커밋 경계:** 두 cache schema/repository만 포함한다.

### Task 11. In-memory catalog, Missing 상태, Remove from List

**목표:** cache snapshot을 catalog로 만들고 Missing 항목을 유지하며 manager data만 제거한다.

**생성 파일**

- `src/UProjectHub.Core/Catalog/ProjectCatalog.cs`
- `src/UProjectHub.Core/Catalog/ProjectCatalogSnapshot.cs`
- `src/UProjectHub.Core/Catalog/ManagedProjectRemovalService.cs`
- `tests/UProjectHub.Core.Tests/Catalog/ProjectCatalogTests.cs`
- `tests/UProjectHub.Core.Tests/Catalog/ManagedProjectRemovalServiceTests.cs`

**소비:** `ProjectPath`, `UnrealProject`, settings/cache repositories.

**제공:** canonical-path upsert/mark-missing/remove, immutable snapshot, `RemoveMissingAsync`.

**회귀 사례**

- cached path 부재 시 `Missing`으로 기본 snapshot에 남음.
- `RemoveMissingAsync`가 catalog/cache와 path-scoped user state만 제거.
- Missing record가 가리키는 경로에 테스트 파일을 만들어도 실제 파일/폴더는 삭제되지 않음.
- Available 항목 제거 요청은 거부 result를 반환.

**TDD 및 검증**

1. 위 테스트 작성 → focused test 실패.
2. filesystem 삭제 API를 전혀 주입받지 않는 service 구현 → focused test 통과.
3. 전체 test/build와 `rg -n "System.IO|File\.|Directory\." src/UProjectHub.Core/Catalog/ManagedProjectRemovalService.cs` 확인 → filesystem 접근 없음.

**커밋 경계:** catalog와 비파괴 removal만 포함한다.

### Task 12. Project root scan과 metadata loading

**목표:** configured root를 안전하게 검색하고 candidate별 metadata를 독립 처리한다.

**생성 파일**

- `src/UProjectHub.Core/Discovery/ProjectCandidate.cs`
- `src/UProjectHub.Core/Discovery/ProjectDiscoveryIssue.cs`
- `src/UProjectHub.Core/Discovery/IProjectDirectoryEnumerator.cs`
- `src/UProjectHub.Core/Discovery/SystemProjectDirectoryEnumerator.cs`
- `src/UProjectHub.Core/Discovery/ProjectRootScanner.cs`
- `src/UProjectHub.Core/Discovery/ProjectMetadataLoader.cs`
- `src/UProjectHub.Core/Discovery/ProjectDiscoveryService.cs`
- `tests/UProjectHub.Core.Tests/Discovery/ProjectRootScannerTests.cs`
- `tests/UProjectHub.Core.Tests/Discovery/ProjectDiscoveryServiceTests.cs`
- `tests/UProjectHub.Core.Tests/Discovery/FakeProjectDirectoryEnumerator.cs`
- `tests/UProjectHub.Core.Tests/Fixtures/Discovery/MixedRoot/Valid/Valid.uproject`
- `tests/UProjectHub.Core.Tests/Fixtures/Discovery/MixedRoot/Broken/Broken.uproject`
- `tests/UProjectHub.Core.Tests/Fixtures/Discovery/MixedRoot/Nested/Nested.uproject`

**소비:** parser, classifier, activity detector, `ProjectUserState`, cancellation token.

**제공:** `ScanAsync`, `LoadAsync`, `DiscoverAsync`와 candidate별 project/issue 결과 stream. `ProjectMetadataLoader`는 settings의 path-scoped user state를 merge하고, 없으면 기본 favorite/last-launched 값을 사용한다.

**회귀 사례**

- recursive `.uproject` 검색 및 canonical 중복 제거.
- reparse point/drive-wide implicit scan 금지.
- malformed `.uproject`가 `Broken` issue/row가 되고 valid project 결과를 중단하지 않음.
- inaccessible directory 오류가 전체 scan을 중단하지 않음.

**TDD 및 검증**

1. mixed fixture 테스트 작성 → focused test 실패.
2. `EnumerationOptions.IgnoreInaccessible`와 per-candidate exception boundary 구현 → focused test 통과.
3. 전체 test/build → 통과.

**커밋 경계:** candidate discovery와 metadata loading만 포함한다.

### Task 13. Refresh와 explicit Rescan orchestration

**목표:** known project 검증과 새 candidate 발견을 명확히 분리한다.

**생성 파일**

- `src/UProjectHub.Core/Discovery/ProjectRefreshUpdate.cs`
- `src/UProjectHub.Core/Discovery/ProjectRefreshService.cs`
- `src/UProjectHub.Core/Discovery/ProjectRescanService.cs`
- `tests/UProjectHub.Core.Tests/Discovery/ProjectRefreshServiceTests.cs`
- `tests/UProjectHub.Core.Tests/Discovery/ProjectRescanServiceTests.cs`

**소비:** catalog, discovery service, cache repository, parser/activity services, cancellation token, progress callback.

**제공:** `RefreshKnownAsync`와 `RescanAsync`의 분리된 command API, incremental `ProjectRefreshUpdate`.

**회귀 사례**

- Refresh는 known path만 검증하고 새 project를 찾지 않음.
- Refresh는 missing path를 catalog에서 숨기지 않고 `Missing` 처리.
- Rescan만 configured root에서 새 project를 발견.
- 각 update가 독립적으로 적용되고 cancellation이 정상 종료.

**TDD 및 검증**

1. orchestration 테스트 작성 → focused test 실패.
2. 최소 service 구현 → focused test 통과.
3. 전체 test/build → 통과.

**커밋 경계:** refresh/rescan use case만 포함한다.

### Task 14. Core engine resolution

**목표:** provider 우선순위나 다른 버전 fallback 없이 association을 안전하게 해석한다.

**생성 파일**

- `src/UProjectHub.Core/Engines/EngineAssociation.cs`
- `src/UProjectHub.Core/Engines/EngineAssociationParser.cs`
- `src/UProjectHub.Core/Engines/EngineResolution.cs`
- `src/UProjectHub.Core/Engines/EngineResolver.cs`
- `tests/UProjectHub.Core.Tests/Engines/EngineAssociationParserTests.cs`
- `tests/UProjectHub.Core.Tests/Engines/EngineResolverTests.cs`

**소비:** `InstalledEngine`, `EngineVersion`.

**제공:** `EngineResolver.Resolve(string?, IReadOnlyCollection<InstalledEngine>)`.

**회귀 사례**

- numeric association은 major/minor로 매칭.
- usable candidate 1개 → `Resolved`.
- 같은 major/minor usable candidate 2개 이상 → `Ambiguous`.
- GUID는 normalized exact GUID만 매칭.
- expected association을 찾지 못해도 다른 버전으로 fallback하지 않고 `Missing`.
- 해석 불가/빈 association → `Unknown`.

**TDD 및 검증**

1. resolver 회귀 테스트 작성 → focused test 실패.
2. parser/resolver 최소 구현 → focused test 통과.
3. provider source/priority를 resolver 입력 정렬에 사용하지 않는지 review.
4. 전체 test/build → 통과.

**커밋 경계:** engine association parsing/resolution만 포함한다.

### Task 15. Local app-data paths와 Unreal known project roots

**목표:** Windows 경로 규칙과 Unreal Editor가 기록한 known project root를 격리한다.

**생성 파일**

- `src/UProjectHub.Windows/Storage/ILocalAppDataPathProvider.cs`
- `src/UProjectHub.Windows/Storage/LocalAppDataPathProvider.cs`
- `src/UProjectHub.Windows/Storage/AppDataPaths.cs`
- `src/UProjectHub.Windows/Projects/IUnrealKnownProjectRootProvider.cs`
- `src/UProjectHub.Windows/Projects/UnrealKnownProjectRootProvider.cs`
- `src/UProjectHub.Windows/Projects/UnrealEditorSettingsParser.cs`
- `tests/UProjectHub.Core.Tests/Windows/Storage/LocalAppDataPathProviderTests.cs`
- `tests/UProjectHub.Core.Tests/Windows/Projects/UnrealEditorSettingsParserTests.cs`
- `tests/UProjectHub.Core.Tests/Fixtures/Windows/UnrealEngine/5.8/Saved/Config/WindowsEditor/EditorSettings.ini`

**소비:** `%LOCALAPPDATA%`, `ProjectPath`.

**제공:** settings/cache/log 경로, Unreal `EditorSettings.ini`의 `CreatedProjectPaths`를 root 목록으로 읽는 provider. 지원 경로는 version별 `Saved/Config/WindowsEditor/EditorSettings.ini`와 기존 `Saved/Config/Windows/EditorSettings.ini`이다.

**테스트 및 검증**

1. 환경 독립적인 base path와 INI fixture 테스트 작성 → focused test 실패.
2. path provider/parser 구현 → focused test 통과.
3. 존재하지 않거나 읽을 수 없는 Unreal 설정은 empty result와 issue로 격리되는지 확인.
4. 전체 test/build → 통과.

**커밋 경계:** app-data path 및 known-root provider만 포함한다.

### Task 16. Epic Launcher engine provider

**목표:** Epic Launcher 설치 metadata에서 usable engine 후보를 만든다.

**생성 파일**

- `src/UProjectHub.Windows/Engines/IEngineProvider.cs`
- `src/UProjectHub.Windows/Engines/EngineProviderResult.cs`
- `src/UProjectHub.Windows/Engines/Launcher/LauncherEngineProvider.cs`
- `src/UProjectHub.Windows/Engines/Launcher/LauncherInstalledManifestParser.cs`
- `src/UProjectHub.Windows/Engines/Launcher/LauncherInstalledManifest.cs`
- `tests/UProjectHub.Core.Tests/Windows/Engines/LauncherInstalledManifestParserTests.cs`
- `tests/UProjectHub.Core.Tests/Windows/Engines/LauncherEngineProviderTests.cs`
- `tests/UProjectHub.Core.Tests/Fixtures/Windows/Epic/LauncherInstalled.valid.json`
- `tests/UProjectHub.Core.Tests/Fixtures/Windows/Epic/LauncherInstalled.malformed.json`

**소비:** `%ProgramData%\Epic\UnrealEngineLauncher\LauncherInstalled.dat`, `InstalledEngine`.

**제공:** `IEngineProvider.DiscoverAsync`, Launcher association/display version/install/editor path normalization, provider-level issue isolation.

**테스트 및 검증**

1. valid/malformed manifest 및 missing editor fixture 테스트 작성 → focused test 실패.
2. `System.Text.Json` parser/provider 구현 → focused test 통과.
3. `Engine\Binaries\Win64\UnrealEditor.exe`가 없으면 candidate가 usable이 아님을 확인.
4. 전체 test/build → 통과.

**커밋 경계:** Launcher engine discovery만 포함한다.

### Task 17. Registered source-build engine provider

**목표:** 실제 Registry를 테스트에서 변경하지 않고 GUID source build를 발견한다.

**생성 파일**

- `src/UProjectHub.Windows/Registry/IRegistryReader.cs`
- `src/UProjectHub.Windows/Registry/WindowsRegistryReader.cs`
- `src/UProjectHub.Windows/Engines/SourceBuild/SourceBuildEngineProvider.cs`
- `tests/UProjectHub.Core.Tests/Windows/Registry/FakeRegistryReader.cs`
- `tests/UProjectHub.Core.Tests/Windows/Engines/SourceBuildEngineProviderTests.cs`

**소비:** `HKCU\SOFTWARE\Epic Games\Unreal Engine\Builds`, `IEngineProvider`.

**제공:** exact GUID association과 editor path를 가진 source-build `InstalledEngine` 후보.

**테스트 및 검증**

1. GUID/path/missing editor/Registry read error fake 테스트 작성 → focused test 실패.
2. Registry abstraction과 provider 구현 → focused test 통과.
3. 실제 HKCU에 write가 없음을 코드 review와 `rg -n "SetValue|DeleteValue|DeleteSubKey" src`로 확인.
4. 전체 test/build → 통과.

**커밋 경계:** Registry read boundary와 source-build provider만 포함한다.

### Task 18. Manual engine provider와 engine aggregation

**목표:** user settings의 engine root를 검증하고 provider 결과를 안전하게 합친다.

**생성 파일**

- `src/UProjectHub.Windows/Engines/Manual/ManualEngineProvider.cs`
- `src/UProjectHub.Windows/Engines/Manual/ManualEngineValidator.cs`
- `src/UProjectHub.Windows/Engines/EngineDiscoveryService.cs`
- `src/UProjectHub.Windows/Engines/EngineDiscoveryResult.cs`
- `tests/UProjectHub.Core.Tests/Windows/Engines/ManualEngineProviderTests.cs`
- `tests/UProjectHub.Core.Tests/Windows/Engines/EngineDiscoveryServiceTests.cs`

**소비:** `AppSettings.ManualEngineRoots`, 모든 `IEngineProvider`.

**제공:** manual root validation, provider별 issue를 보존한 normalized candidate 집합.

**테스트 및 검증**

1. valid/missing editor root, provider exception isolation, 동일 physical editor path 중복 테스트 작성 → focused test 실패.
2. validator/aggregator 구현 → focused test 통과.
3. 중복 제거는 canonical editor path에만 적용하고, 서로 다른 설치의 같은 major/minor 후보는 둘 다 유지해 resolver가 `Ambiguous`를 반환할 수 있는지 확인.
4. 전체 test/build → 통과.

**커밋 경계:** manual provider와 candidate aggregation만 포함한다.

### Task 19. Windows launch 및 context integration

**목표:** project/Explorer/Visual Studio 실행을 안전한 process boundary로 제공한다.

**생성 파일**

- `src/UProjectHub.Windows/Launching/IProcessLauncher.cs`
- `src/UProjectHub.Windows/Launching/ProcessLauncher.cs`
- `src/UProjectHub.Windows/Launching/ProcessRequest.cs`
- `src/UProjectHub.Windows/Launching/LaunchResult.cs`
- `src/UProjectHub.Windows/Launching/IUnrealEditorLauncher.cs`
- `src/UProjectHub.Windows/Launching/UnrealEditorLauncher.cs`
- `src/UProjectHub.Windows/Launching/IExplorerLauncher.cs`
- `src/UProjectHub.Windows/Launching/ExplorerLauncher.cs`
- `src/UProjectHub.Windows/Launching/IVisualStudioLauncher.cs`
- `src/UProjectHub.Windows/Launching/VisualStudioLauncher.cs`
- `tests/UProjectHub.Core.Tests/Windows/Launching/FakeProcessLauncher.cs`
- `tests/UProjectHub.Core.Tests/Windows/Launching/UnrealEditorLauncherTests.cs`
- `tests/UProjectHub.Core.Tests/Windows/Launching/ExplorerLauncherTests.cs`
- `tests/UProjectHub.Core.Tests/Windows/Launching/VisualStudioLauncherTests.cs`

**소비:** `EngineResolution`, `UnrealProject`, `IClock`.

**제공:** argument-list 기반 process 요청, 성공/실패 result, existing `.sln` detection.

**동작 규칙**

- Unreal launch는 `Resolved`와 존재하는 editor path에서만 가능하다.
- 성공적으로 process 시작을 요청한 시각을 result에 담고 App이 `LastLaunched`로 저장한다.
- Visual Studio는 project 이름과 같은 `.sln`을 우선하고, 그것이 없을 때 project root에 `.sln`이 정확히 하나인 경우만 제공한다.
- Generate Project Files를 호출하지 않는다.
- Explorer reveal은 project path를 argument로 전달하고 shell file association으로 Unreal Editor를 열지 않는다.

**테스트 및 검증**

1. 공백/따옴표 path, unresolved engine, missing editor, `.sln` 0/1/복수 테스트 작성 → focused test 실패.
2. fake process launcher 기반 최소 구현 → focused test 통과.
3. 제품 코드에서 `GenerateProjectFiles`, `.uproject` write가 없음을 검색.
4. 전체 test/build → 통과.

**커밋 경계:** Windows process integration만 포함한다.

### Task 20. Bounded text logging

**목표:** startup, cache, scan, parse, resolution, launch 오류를 기록할 좁은 logging 기반을 만든다.

**생성 파일**

- `src/UProjectHub.Core/Diagnostics/IAppLogger.cs`
- `src/UProjectHub.Core/Diagnostics/NullAppLogger.cs`
- `src/UProjectHub.Windows/Logging/RollingFileLogger.cs`
- `src/UProjectHub.Windows/Logging/LogRetentionPolicy.cs`
- `tests/UProjectHub.Core.Tests/Windows/Logging/RollingFileLoggerTests.cs`

**소비:** `AppDataPaths.LogPath`.

**제공:** thread-safe human-readable log, size-bounded `app.log` 및 제한된 backup 파일.

**테스트 및 검증**

1. level/timestamp/exception formatting, rotation threshold, retention count 테스트 작성 → focused test 실패.
2. logger 최소 구현 → focused test 통과.
3. 메시지가 project content를 읽거나 descriptor 전체를 dump하지 않는지 review.
4. 전체 test/build → 통과.

**커밋 경계:** logging abstraction과 file sink만 포함한다.

### Task 21. WPF composition root, MVVM primitives, application shell

**목표:** 외부 MVVM/DI 패키지 없이 얇은 shell과 수동 composition root를 만든다.

**생성 파일**

- `src/UProjectHub.App/Infrastructure/ObservableObject.cs`
- `src/UProjectHub.App/Infrastructure/RelayCommand.cs`
- `src/UProjectHub.App/Infrastructure/AsyncRelayCommand.cs`
- `src/UProjectHub.App/Composition/AppBootstrapper.cs`
- `src/UProjectHub.App/ViewModels/MainViewModel.cs`
- `src/UProjectHub.App/ViewModels/StatusBarViewModel.cs`
- `tests/UProjectHub.Core.Tests/App/MainViewModelTests.cs`

**수정 파일**

- `src/UProjectHub.App/App.xaml`
- `src/UProjectHub.App/App.xaml.cs`
- `src/UProjectHub.App/MainWindow.xaml`
- `src/UProjectHub.App/MainWindow.xaml.cs`
- `tests/UProjectHub.Core.Tests/UProjectHub.Core.Tests.csproj`

**소비:** Task 1~20의 Core/Windows 서비스 생성자.

**제공:** `AppBootstrapper.Build`, property/command notification primitives, title/count/status/settings action 영역을 가진 shell.

**테스트 및 검증**

1. ViewModel notification/command 상태 테스트 작성 → focused test 실패.
2. MVVM primitives와 shell 구현 → focused test 통과.
3. `dotnet run --project src/UProjectHub.App` → 빈 shell이 열리고 UI thread exception 없음.
4. `MainWindow.xaml.cs`가 presentation wiring 외 로직을 갖지 않는지 review.
5. 전체 test/build → 통과.

**커밋 경계:** shell, composition root, MVVM 기반만 포함한다.

### Task 22. Virtualized project details list

**목표:** 실제 `UnrealProject` snapshot을 vertical details list로 표시한다.

**생성 파일**

- `src/UProjectHub.App/Controls/ProjectList.xaml`
- `src/UProjectHub.App/Controls/ProjectList.xaml.cs`
- `src/UProjectHub.App/ViewModels/ProjectListViewModel.cs`
- `src/UProjectHub.App/ViewModels/ProjectRowViewModel.cs`
- `src/UProjectHub.App/Converters/RelativeTimeConverter.cs`
- `src/UProjectHub.App/Converters/ProjectStateMessageConverter.cs`
- `tests/UProjectHub.Core.Tests/App/ProjectListViewModelTests.cs`
- `tests/UProjectHub.Core.Tests/App/RelativeTimeConverterTests.cs`

**수정 파일**

- `src/UProjectHub.App/MainWindow.xaml`
- `src/UProjectHub.App/ViewModels/MainViewModel.cs`
- `src/UProjectHub.App/Composition/AppBootstrapper.cs`

**소비:** `ProjectCatalogSnapshot`, `IClock`.

**제공:** favorite/name+path/engine/type/last modified/last launched column, Missing/Broken warning row, visible/total count, empty-state 구분.

**테스트 및 검증**

1. snapshot-to-row, Missing 표시, exact/relative time, no-project와 no-result state 테스트 작성 → focused test 실패.
2. virtualizing WPF DataGrid 기반 list 구현 → focused test 통과.
3. 수동 확인: 1,000개 in-memory row에서 scroll/selection이 반응하고 강한 grid line이나 Healthy label이 없음.
4. 전체 test/build → 통과.

**커밋 경계:** read-only project list와 row state만 포함한다.

### Task 23. Search/filter/sort UI integration

**목표:** Core의 in-memory query/filter/sort를 search surface와 column header에 연결한다.

**생성 파일**

- `src/UProjectHub.App/Controls/SearchBox.xaml`
- `src/UProjectHub.App/Controls/SearchBox.xaml.cs`
- `src/UProjectHub.App/Controls/FilterChip.xaml`
- `src/UProjectHub.App/Controls/FilterChip.xaml.cs`
- `src/UProjectHub.App/ViewModels/SearchFilterViewModel.cs`
- `tests/UProjectHub.Core.Tests/App/SearchFilterViewModelTests.cs`

**수정 파일**

- `src/UProjectHub.App/MainWindow.xaml`
- `src/UProjectHub.App/ViewModels/MainViewModel.cs`
- `src/UProjectHub.App/ViewModels/ProjectListViewModel.cs`
- `src/UProjectHub.App/Composition/AppBootstrapper.cs`

**소비:** `ProjectQueryParser`, search/filter/sort services, `AppSettings`의 persisted state.

**제공:** live search, Engine/Type/Favorites filter chips, header sort toggle, reset action, `Ctrl+F`/`Esc` 동작. Engine filter option은 설치 여부와 무관하게 현재 project snapshot에 나타나는 모든 engine display/version 값을 사용한다.

**테스트 및 검증**

1. text/filter/sort state 변화가 같은 raw snapshot에만 적용되고 discovery service를 호출하지 않는 ViewModel 테스트 작성 → 실패.
2. ViewModel/control binding 구현 → focused test 통과.
3. 수동 확인: quoted path, malformed token fallback, 5.9/5.10 header sort, no-result reset.
4. 전체 test/build → 통과.

**커밋 경계:** search/filter/sort UI와 in-memory integration만 포함한다.

### Task 24. Favorites, keyboard, context actions, project information

**목표:** row interaction을 command로 연결하고 모든 mutation을 manager-owned data로 제한한다.

**생성 파일**

- `src/UProjectHub.App/Services/IClipboardService.cs`
- `src/UProjectHub.App/Services/WpfClipboardService.cs`
- `src/UProjectHub.App/Services/ProjectActionService.cs`
- `src/UProjectHub.App/ViewModels/ProjectContextActionsViewModel.cs`
- `src/UProjectHub.App/ViewModels/ProjectInformationViewModel.cs`
- `src/UProjectHub.App/Views/ProjectInformationWindow.xaml`
- `src/UProjectHub.App/Views/ProjectInformationWindow.xaml.cs`
- `tests/UProjectHub.Core.Tests/App/ProjectActionServiceTests.cs`
- `tests/UProjectHub.Core.Tests/App/ProjectContextActionsViewModelTests.cs`

**수정 파일**

- `src/UProjectHub.App/Controls/ProjectList.xaml`
- `src/UProjectHub.App/ViewModels/ProjectRowViewModel.cs`
- `src/UProjectHub.App/ViewModels/MainViewModel.cs`
- `src/UProjectHub.App/Composition/AppBootstrapper.cs`

**소비:** settings/cache repositories, catalog removal, Windows launchers, clipboard service.

**제공:** favorite persistence, double-click/Enter open, folder/reveal/copy path, conditional VS action, information dialog, Missing-only Remove from List, Up/Down/Delete keyboard policy.

**테스트 및 검증**

1. favorite restart persistence, successful launch의 LastLaunched 갱신, unavailable action, Missing removal 테스트 작성 → 실패.
2. command orchestration 최소 구현 → focused test 통과.
3. 수동 확인: favorite click은 row를 열지 않음, Delete는 아무 파괴 작업도 하지 않음, context/overflow action 집합 동일.
4. `ProjectActionService.cs`와 `ManagedProjectRemovalService.cs`에 project filesystem write/delete dependency가 없고 `EngineAssociation`을 대입하지 않는지 review.
5. 전체 test/build → 통과.

**커밋 경계:** row commands와 project information만 포함한다.

### Task 25. Semantic themes, motion, density, responsive columns

**목표:** One UI 참고를 semantic theme/motion resource로 구현하되 usability, scanability, input responsiveness, desktop density를 우선한다.

**생성 파일**

- `src/UProjectHub.App/Themes/Colors.xaml`
- `src/UProjectHub.App/Themes/Typography.xaml`
- `src/UProjectHub.App/Themes/Spacing.xaml`
- `src/UProjectHub.App/Themes/Motion.xaml`
- `src/UProjectHub.App/Themes/Buttons.xaml`
- `src/UProjectHub.App/Themes/DataGrid.xaml`
- `src/UProjectHub.App/Themes/Light.xaml`
- `src/UProjectHub.App/Themes/Dark.xaml`
- `src/UProjectHub.App/Themes/NormalDensity.xaml`
- `src/UProjectHub.App/Themes/CompactDensity.xaml`
- `src/UProjectHub.App/Services/ThemeService.cs`
- `src/UProjectHub.App/Services/ISystemAnimationPreference.cs`
- `src/UProjectHub.App/Services/WpfSystemAnimationPreference.cs`
- `src/UProjectHub.App/Services/MotionService.cs`
- `src/UProjectHub.App/Behaviors/ResponsiveColumnsBehavior.cs`
- `tests/UProjectHub.Core.Tests/App/ThemeServiceTests.cs`
- `tests/UProjectHub.Core.Tests/App/MotionServiceTests.cs`

**수정 파일**

- `src/UProjectHub.App/App.xaml`
- `src/UProjectHub.App/MainWindow.xaml`
- `src/UProjectHub.App/Controls/ProjectList.xaml`
- `src/UProjectHub.App/Controls/SearchBox.xaml`
- `src/UProjectHub.App/Controls/FilterChip.xaml`
- `src/UProjectHub.App/Views/ProjectInformationWindow.xaml`
- `src/UProjectHub.App/Composition/AppBootstrapper.cs`
- `src/UProjectHub.App/ViewModels/StatusBarViewModel.cs`

**소비:** `ThemeMode`, `RowDensity`, `ColumnLayoutState`, WPF `SystemParameters.ClientAreaAnimation`.

**제공:** System/Light/Dark switching, 56~64px Normal, 42~48px Compact, semantic warning/selection resources, wide/medium/narrow column progression, centralized Fast 약 90ms / Normal 약 140ms / Slow 약 180ms Ease-Out motion token, Windows preference가 꺼졌을 때의 immediate state transition.

`MotionService`는 App-layer의 좁은 presentation service로 유지한다. `WpfSystemAnimationPreference`는 `SystemParameters.ClientAreaAnimation`과 preference change notification만 감싼다. animation setting을 `AppSettings`에 추가하지 않으며 외부 animation package를 사용하지 않는다.

허용 구현은 hover/selection의 brush·foreground transition, button/filter chip의 미세한 `RenderTransform` press feedback, favorite의 짧은 scale/opacity feedback, active operation 동안만 보이는 refresh rotation, 작은 dialog의 짧은 opacity/scale transition으로 제한한다. Favorite/search/filter/sort의 실제 state/data 변경은 animation 시작 전에 즉시 완료한다.

**테스트 및 검증**

1. resource switch, persisted mode/density, enabled/disabled system animation preference, effective duration 테스트 작성 → 실패.
2. theme/motion services, resource dictionaries, responsive behavior 구현 → focused test 통과.
3. hover/selection brush transition과 button/filter chip press feedback이 90~140ms token을 참조하는지 수동 확인.
4. favorite state는 즉시 바뀌고 scale/opacity micro-interaction만 짧게 실행되는지 확인.
5. `StatusBarViewModel`의 operation-active 상태에서만 refresh indicator가 움직이고 false 전환 즉시 멈추는지 확인. Task 27은 이 상태를 실제 Refresh/Rescan lifecycle에 연결한다.
6. search/filter/sort 결과가 entrance/reorder animation을 기다리지 않고 즉시 적용되는지 확인.
7. 1,000-row virtualized list에서 hover/selection motion이 scrolling과 container virtualization을 해치지 않는지 확인.
8. Windows animation preference가 disabled이면 non-essential motion이 즉시 전환되고 layout/functionality는 동일한지 확인.
9. Project Information surface가 짧은 opacity/scale token만 사용하고 과한 window zoom을 사용하지 않는지 확인.
10. Light/Dark × Normal/Compact × wide/narrow에서 project/engine/last modified 유지, selection contrast, virtualization 확인.
11. `Motion.xaml` 밖의 control에 duration/easing literal이 없고 `rg -n "#[0-9A-Fa-f]{6,8}" src/UProjectHub.App --glob "*.xaml"` 결과가 color dictionaries 밖에 없는지 확인.
12. Width/Height/Margin/GridLength/layout-position animation, full-list/row-entrance/reorder animation이 없음을 XAML review.
13. 전체 test/build → 통과.

**커밋 경계:** semantic styling/motion, Windows motion preference, density, responsive columns만 포함한다.

### Task 26. Settings UI, search roots, manual engines

**목표:** user-owned settings를 단순한 vertical settings surface에서 편집하고 explicit Rescan을 제공한다.

**생성 파일**

- `src/UProjectHub.App/Views/SettingsWindow.xaml`
- `src/UProjectHub.App/Views/SettingsWindow.xaml.cs`
- `src/UProjectHub.App/ViewModels/SettingsViewModel.cs`
- `src/UProjectHub.App/Services/IFolderPickerService.cs`
- `src/UProjectHub.App/Services/FolderPickerService.cs`
- `src/UProjectHub.App/Services/IProjectOperations.cs`
- `src/UProjectHub.App/Services/ProjectOperations.cs`
- `src/UProjectHub.App/Behaviors/FolderDropBehavior.cs`
- `tests/UProjectHub.Core.Tests/App/SettingsViewModelTests.cs`
- `tests/UProjectHub.Core.Tests/App/ProjectOperationsTests.cs`

**수정 파일**

- `src/UProjectHub.App/MainWindow.xaml`
- `src/UProjectHub.App/ViewModels/MainViewModel.cs`
- `src/UProjectHub.App/Composition/AppBootstrapper.cs`

**소비:** settings repository, manual engine validator, project rescan service, theme service.

**제공:** root add/remove, folder picker/drop의 항상-persistent-root semantics, manual engine validation/add/remove, theme/density, sort/filter/column state 저장, explicit Rescan command.

**테스트 및 검증**

1. folder 내용과 무관한 root 저장, duplicate root, invalid manual engine, settings round-trip, Rescan command 분리 테스트 작성 → 실패.
2. ViewModel/service/dialog 구현 → focused test 통과.
3. 수동 확인: startup Rescan option 없음; folder picker와 drop이 같은 root-add command 사용; 전체 drive가 자동 추가되지 않음.
4. 전체 test/build → 통과.

**커밋 경계:** settings UI와 explicit configuration operations만 포함한다.

### Task 27. Cache-first startup와 non-blocking background refresh

**목표:** 문서의 고정 startup 순서를 실제 composition에 연결하고 UI를 block하지 않는다.

**생성 파일**

- `src/UProjectHub.App/Services/ApplicationCoordinator.cs`
- `src/UProjectHub.App/Services/BackgroundRefreshService.cs`
- `src/UProjectHub.App/Services/UiUpdateBatcher.cs`
- `tests/UProjectHub.Core.Tests/App/ApplicationCoordinatorTests.cs`
- `tests/UProjectHub.Core.Tests/App/BackgroundRefreshServiceTests.cs`

**수정 파일**

- `src/UProjectHub.App/App.xaml.cs`
- `src/UProjectHub.App/Composition/AppBootstrapper.cs`
- `src/UProjectHub.App/ViewModels/MainViewModel.cs`
- `src/UProjectHub.App/ViewModels/StatusBarViewModel.cs`
- `src/UProjectHub.App/MainWindow.xaml`
- `src/UProjectHub.App/Services/ProjectOperations.cs`

**소비:** settings/cache repositories, catalog, refresh/rescan, known roots, engine discovery/resolver, logger, Dispatcher, cancellation token.

**제공:** `ApplicationCoordinator.StartAsync/RefreshAsync/RescanAsync/StopAsync`, quiet status, incremental row/engine updates, shutdown cancellation.

`ApplicationCoordinator`는 `StatusBarViewModel`의 operation-active 상태만 관리한다. indicator의 motion token과 system-preference 동작은 Task 25의 presentation layer가 소유한다.

**고정 startup 순서**

1. settings load 및 적용.
2. project/engine cache load.
3. cached catalog을 ViewModel에 전달하고 MainWindow 표시.
4. window interaction 가능 상태에서 background Refresh 시작.
5. known project validation, activity/descriptor refresh, engine discovery/resolution을 UI thread 밖에서 수행.
6. 짧은 collection update만 Dispatcher에 batch 적용.
7. updated cache 저장.

Startup에서는 full Rescan을 호출하지 않는다. F5는 `RefreshAsync`, settings의 Rescan button만 `RescanAsync`를 호출한다.

**테스트 및 검증**

1. call-order spy, cache-before-refresh, no-startup-rescan, cancellation, incremental progress, per-item failure 테스트 작성 → 실패.
2. coordinator/background service 구현 → focused test 통과.
3. logger에 startup/cache/refresh/rescan/parse/resolution/launch failure event가 연결되는지 테스트.
4. 수동 확인: 느린 fixture root에서 window가 즉시 선택/검색 가능하고 overlay로 block되지 않음.
5. 전체 test/build → 통과.

**커밋 경계:** startup/background orchestration과 operational error wiring만 포함한다.

### Task 28. Full MVP integration verification

**목표:** SPEC 요구사항을 하나의 fixture workflow와 UI verification matrix로 최종 검증한다.

**생성 파일**

- `tests/UProjectHub.Core.Tests/Integration/MvpWorkflowTests.cs`
- `tests/UProjectHub.Core.Tests/Fixtures/Integration/GameAcademy/CppGame/CppGame.uproject`
- `tests/UProjectHub.Core.Tests/Fixtures/Integration/GameAcademy/BlueprintGame/BlueprintGame.uproject`
- `tests/UProjectHub.Core.Tests/Fixtures/Integration/GameAcademy/BrokenGame/BrokenGame.uproject`
- `scripts/verify.ps1`
- `docs/VERIFICATION.md`

**수정 파일**

- `README.md`
- `scripts/build.ps1`
- `scripts/test.ps1`
- `scripts/run.ps1`

**소비:** Task 1~27의 전체 product surface.

**제공:** 한 명령으로 실행되는 최종 검증과 재현 가능한 수동 UI checklist.

**통합 회귀 흐름**

1. settings와 cache를 fixture app-data에 준비한다.
2. cache project를 즉시 catalog에 표시한다.
3. Refresh에서 Missing/Broken/Available을 격리 갱신한다.
4. explicit Rescan으로 새 project를 발견한다.
5. search/filter/sort/favorite를 적용한다.
6. engine candidates를 해석하고 resolved project만 fake process launcher로 연다.
7. LastLaunched를 저장한다.
8. Missing entry를 목록에서 제거하고 fixture filesystem이 그대로인지 확인한다.

**검증 명령과 기대 결과**

- `dotnet test UProjectHub.sln` → 모든 fixture/unit/integration test 통과.
- `dotnet build UProjectHub.sln -c Release` → 경고와 오류 0.
- `pwsh -File scripts/verify.ps1` → restore, test, Release build, forbidden-pattern 검사 모두 통과.
- `dotnet run --project src/UProjectHub.App` → cache-first startup 및 manual verification 가능.
- `git diff --check` → whitespace error 없음.
- `git status --short` → Task 28 의도 파일만 변경.

`docs/VERIFICATION.md`에는 Light/Dark, Normal/Compact, narrow width, keyboard, Missing/Broken, engine states, Refresh/Rescan 구분, settings persistence, log rotation 확인 결과를 기록한다.

Motion UI matrix에는 최소한 다음 상태를 포함한다.

- Windows animations enabled;
- Windows animations disabled;
- row/button/filter hover와 selection;
- favorite micro-interaction과 즉시 state update;
- Refresh/Rescan indicator가 작업 중에만 동작하고 종료 즉시 정지;
- search/filter/sort 결과의 즉시 update와 entrance/reorder animation 부재;
- 1,000-row scrolling 및 virtualization 유지;
- animations disabled 상태에서도 동일 functionality/layout과 정적인 operation status 유지.

**커밋 경계:** integration tests, verification script/document, 실제 build/run 설명만 포함한다.

## 4. Task별 focused 검증 명령

Task 2~27은 아래 명령을 테스트 작성 직후 실행해 red를 확인하고, 최소 구현 후 같은 명령을 다시 실행해 green을 확인한다. Red에서는 새 테스트가 타입/동작 부재라는 의도한 이유로 하나 이상 실패해야 하고, green에서는 해당 명령의 모든 테스트가 통과해야 한다. 각 green 이후에는 공통으로 `dotnet test UProjectHub.sln`, `dotnet build UProjectHub.sln`, `git diff --check`를 실행하며 기대 결과는 전체 테스트 통과, 경고/오류 0, whitespace error 없음이다.

| Task | focused 명령 | 기대 결과 |
|---|---|---|
| 1 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~SolutionSmokeTests"` | scaffold 후 smoke test 통과 |
| 2 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~UnrealProjectTests\|FullyQualifiedName~ProjectPathTests"` | model/path identity 테스트 통과 |
| 3 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~UProjectParserTests\|FullyQualifiedName~ProjectClassifierTests"` | parser/classifier 회귀 테스트 통과 |
| 4 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~EngineVersion"` | version parse/compare 테스트 통과 |
| 5 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ProjectQueryParserTests"` | query parsing/fallback 테스트 통과 |
| 6 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ProjectSearchServiceTests\|FullyQualifiedName~ProjectFilterServiceTests"` | search/filter 테스트 통과 |
| 7 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ProjectSortServiceTests"` | 전체 sort column 테스트 통과 |
| 8 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ProjectActivityDetectorTests"` | meaningful activity 테스트 통과 |
| 9 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~JsonSettingsRepositoryTests"` | settings/atomic persistence 테스트 통과 |
| 10 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~CacheRepositoryTests"` | project/engine cache 테스트 통과 |
| 11 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ProjectCatalogTests\|FullyQualifiedName~ManagedProjectRemovalServiceTests"` | Missing/removal 테스트 통과 |
| 12 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ProjectRootScannerTests\|FullyQualifiedName~ProjectDiscoveryServiceTests"` | scan/isolation 테스트 통과 |
| 13 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ProjectRefreshServiceTests\|FullyQualifiedName~ProjectRescanServiceTests"` | Refresh/Rescan 분리 테스트 통과 |
| 14 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~EngineAssociationParserTests\|FullyQualifiedName~EngineResolverTests"` | engine resolution 테스트 통과 |
| 15 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~LocalAppDataPathProviderTests\|FullyQualifiedName~UnrealEditorSettingsParserTests"` | app path/known root 테스트 통과 |
| 16 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~LauncherEngine"` | Launcher manifest/provider 테스트 통과 |
| 17 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~SourceBuildEngineProviderTests"` | Registry provider 테스트 통과 |
| 18 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ManualEngineProviderTests\|FullyQualifiedName~EngineDiscoveryServiceTests"` | manual/aggregate 테스트 통과 |
| 19 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~LauncherTests"` | Unreal/Explorer/Visual Studio launcher 테스트 통과 |
| 20 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~RollingFileLoggerTests"` | formatting/rotation/retention 테스트 통과 |
| 21 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~MainViewModelTests"` | MVVM shell 테스트 통과 |
| 22 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ProjectListViewModelTests\|FullyQualifiedName~RelativeTimeConverterTests"` | list/relative time 테스트 통과 |
| 23 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~SearchFilterViewModelTests"` | UI query/filter/sort state 테스트 통과 |
| 24 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ProjectActionServiceTests\|FullyQualifiedName~ProjectContextActionsViewModelTests"` | context/favorite/launch/removal 테스트 통과 |
| 25 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ThemeServiceTests\|FullyQualifiedName~MotionServiceTests"` | theme/motion/density/system-preference 테스트 통과 |
| 26 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~SettingsViewModelTests\|FullyQualifiedName~ProjectOperationsTests"` | settings/root/manual engine 테스트 통과 |
| 27 | `dotnet test UProjectHub.sln --filter "FullyQualifiedName~ApplicationCoordinatorTests\|FullyQualifiedName~BackgroundRefreshServiceTests"` | startup/background/cancellation 테스트 통과 |
| 28 | `pwsh -File scripts/verify.ps1` | restore, 전체 test, Release build, safety 검사 통과 |

Task 19의 filter는 `UnrealEditorLauncherTests`, `ExplorerLauncherTests`, `VisualStudioLauncherTests` 세 class가 모두 `LauncherTests` suffix를 사용하도록 고정한다.

## 5. 핵심 의존 관계

```text
Task 1 Scaffold
  └─ Task 2 Models/Path
      ├─ Task 3 Parser/Classification ─┐
      ├─ Task 4 EngineVersion ────────┼─ Task 7 Sorting
      ├─ Task 5 Query Parser ─ Task 6 Search/Filter
      ├─ Task 8 Activity ─────────────┤
      ├─ Task 9 Settings ─ Task 10 Cache ─ Task 11 Catalog
      │                                └─ Task 12 Discovery ─ Task 13 Refresh/Rescan
      └─ Task 14 EngineResolver

Task 9/10 ─ Task 15 AppData/KnownRoots
Task 2 ─ Task 16 LauncherProvider ─┐
         Task 17 SourceProvider ───┼─ Task 18 Manual/Aggregation
Task 9 ─ Task 18 Manual/Aggregation┘
Task 14 ─ Task 19 Launching
Task 15 ─ Task 20 Logging

Tasks 1~20 ─ Task 21 WPF Shell
Task 11 ─ Task 22 Project List
Tasks 5~7 ─ Task 23 Search/Filter/Sort UI
Tasks 9/11/19 ─ Task 24 Actions
Tasks 9/21~24 ─ Task 25 Themes/Motion/Density
Tasks 9/13/18/25 ─ Task 26 Settings UI
Tasks 10/13/15/18/20/21~26 ─ Task 27 Startup/Refresh
Tasks 1~27 ─ Task 28 Final Verification
```

병렬 실행 가능한 구간은 Task 3/4/5/8, Task 9/14, Task 16/17이다. 같은 파일을 수정하는 UI Task 21~27은 순차 실행한다.

## 6. SPEC 요구사항 추적표

| SPEC | 요구사항 | 대응 Task |
|---|---|---|
| 2.1 | .NET 10, WPF, 최소 의존성 | 1, 21, 28 |
| 3.1 | known/configured roots, folder picker/drop, manual Rescan | 12, 13, 15, 26, 27 |
| 3.2 | project metadata 전체 | 2, 3, 8, 10, 12, 14, 24 |
| 3.3 | Modules-only C++/Blueprint | 3 |
| 3.4 | meaningful LastModified | 8, 13, 27 |
| 3.5 | plain/structured in-memory search | 5, 6, 23 |
| 3.6 | Engine/Type/Favorites visible filters | 6, 23 |
| 3.7 | 모든 sort와 5.9/5.10 | 4, 7, 23 |
| 3.8 | favorites persistence | 9, 24 |
| 3.9 | resolved Unreal Editor launch, LastLaunched | 19, 24 |
| 3.10 | providers 및 safe engine resolution | 14, 16, 17, 18 |
| 3.11 | context action 전체와 비파괴 removal | 11, 19, 24 |
| 3.12 | keyboard interaction | 23, 24, 27 |
| 3.13 | Refresh와 Rescan 구분 | 13, 26, 27 |
| 3.14 | settings/cache/UI/background startup 순서 | 9, 10, 21, 27 |
| 3.15 | Missing/Broken 격리와 quiet warning | 3, 11, 12, 22, 27 |
| 4 | settings 전체 | 9, 25, 26 |
| 5 | settings/cache 분리와 atomic storage | 9, 10, 15 |
| 6 | bounded human-readable logs | 20, 27 |
| 7 | main details UI | 21, 22, 23, 24, 25 |
| 7.1 | subtle motion, Windows preference, immediate search/filter/sort/reorder | 25, 28 |
| 8 | MVP non-goals 준수 | 모든 Task의 금지-pattern 검토, 28 |
| 9 | build/test/docs/UI matrix | 각 Task, 28 |

## 7. 자체 검토 결과

- SPEC의 모든 MVP section은 최소 한 Task에 연결되어 있다.
- 반드시 요구된 15개 회귀 영역은 Task 3, 4, 5, 6, 8, 11, 12, 14, 28에 명시되어 있다.
- 미결정 또는 미완성 표식을 포함하지 않는다.
- 앞 Task가 제공하는 타입 이름을 뒤 Task가 동일하게 소비하도록 고정 계약과 의존 그래프를 대조했다.
- Windows provider는 Core resolver보다 뒤에 있고, WPF integration은 필요한 Core/Windows use case 이후에 있어 숨은 product dependency가 없다.
- UI thread에는 collection mutation만 짧게 전달하고 scan/activity/engine/cache 검증은 background/cancellable 작업으로 계획했다.
- Git, project 삭제, cache-folder 삭제, version conversion, project generation, watcher, card grid, plugin management, tagging을 포함하지 않는다.
- Motion은 Task 25의 App presentation resource/service에만 두고, Task 27은 animation 지식 없이 operation-active state만 제공한다.
- search/filter/sort/list reorder는 Motion과 무관하게 즉시 갱신되며 full-list entrance, per-row entrance, layout animation은 계획에 포함하지 않는다.
- Task 28의 수동 matrix는 Windows animations enabled/disabled와 virtualization 유지 상태를 모두 포함한다.
- UI를 shell, list, search/filter/sort, actions, themes/motion, settings, startup integration의 7개 독립 Task로 나누었다.
- 총 구현 Task는 28개이며 각 Task는 focused 검증, 전체 test/build, diff review 후 독립 커밋 가능한 경계로 끝난다.
