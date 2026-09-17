# SettingsPage Duplicate Rows and Binding Diagnostics Implementation Plan

> **For agentic workers:** This plan is intended for inline execution in the current session. Do not dispatch subagents. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make SettingsPage render each RFID station once, reject duplicate station configuration at the project boundary, and present cross-yard binding conflicts clearly without changing communication or runtime behavior.

**Architecture:** Keep `SettingsPage` as the owner of the editable row collections and make every refresh rebuild the visible collection from the canonical row collection. Add duplicate `StationId` validation to `ProjectConfigService.LoadAsync` so duplicate project data is reported as a configuration error instead of being silently deduplicated in the UI. Compute binding display state per row and expose a compact diagnostic summary.

**Tech Stack:** .NET Framework 4.8, WPF, `ObservableCollection`, existing `RfidStationBindingRules`, xUnit, System.Text.Json/Newtonsoft.Json.

**Spec:** User request in this conversation: “修复 SettingsPage 当前出现的两个问题”。

## Global Constraints

- 每个 `RfidStationConfig.StationId` 在 SettingsPage 表格中只能出现一次。
- 重复 `StationId` 必须在项目加载/保存边界报告配置错误，不能只在 UI 使用 `Distinct` 掩盖。
- 560/620/全部/未归属筛选连续刷新必须幂等。
- 跨站场绑定继续阻止保存，不自动改 `YardId`、解绑或移动地图点。
- 本轮不修改 `YardCommunicationContext`、`YardCommunicationManager`、Poller、Runtime、UDP、Protocol、SQLite、History、Statistics。
- 不 commit，不 push。

---

### Task 1: Add failing duplicate-configuration and filter regression tests

**Files:**
- Modify: `tests/MineRailMonitor.Infrastructure.Tests/ProjectConfigPersistenceTests.cs` or the existing project-config test file that owns load validation.
- Modify: `tests/MineRailMonitor.Core.Tests/SettingsPageMarkupTests.cs`.

- [ ] **Step 1: Add a test proving duplicate `StationId` in `project.json` is reported by load.**

Create a temporary project manifest with two `RfidStations` entries whose IDs differ only by case and assert load fails with `RFID基站编号重复` and the station ID.

- [ ] **Step 2: Add markup/data-flow assertions for idempotent SettingsPage refresh.**

Assert SettingsPage owns one canonical `_stationRows`, clears `_visibleStationRows` before every filter projection, and does not use a UI-only `Distinct` expression as the duplicate fix. Assert the conflict copy contains `归属冲突` and the summary copy contains `当前存在`.

- [ ] **Step 3: Run only the new tests and verify they fail for the missing behavior.**

Run:

```text
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj --filter FullyQualifiedName~DuplicateStation
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~SettingsPageMarkupTests
```

Expected: the new load test fails because `LoadAsync` currently builds a project even when manifest RFID station IDs repeat; the markup test fails because the new diagnostics contract is not present.

### Task 2: Reject duplicate RFID station IDs at the project boundary

**Files:**
- Modify: `src/MineRailMonitor.Infrastructure/Configuration/ProjectConfigService.cs`.
- Modify: `tests/MineRailMonitor.Infrastructure.Tests/ProjectConfigPersistenceTests.cs` only if the test needs shared fixture helpers.

- [ ] **Step 1: Validate the deserialized RFID station list before constructing `ProjectConfig`.**

Group non-empty `StationId` values case-insensitively after trimming. Add one error per duplicate ID with the existing Chinese error format `RFID基站编号重复：{stationId}。`. Treat blank IDs as the existing validation concern; do not synthesize or merge duplicate entries.

- [ ] **Step 2: Return the normal project-load failure result when duplicate IDs are found.**

Do not change any protocol/runtime objects, and do not remove legacy duplicate map bindings. A project with duplicate RFID station IDs must not reach SettingsPage as a valid project.

- [ ] **Step 3: Run the focused infrastructure tests and verify green.**

Run the duplicate-load test and the full `ProjectConfigPersistenceTests` class.

### Task 3: Make SettingsPage binding conflict display compact and diagnostic

**Files:**
- Modify: `src/MineRailMonitor/Pages/SettingsPage.xaml`.
- Modify: `src/MineRailMonitor/Pages/SettingsPage.xaml.cs`.
- Modify: `tests/MineRailMonitor.Core.Tests/SettingsPageMarkupTests.cs`.

- [ ] **Step 1: Add row properties for conflict state and a page-level summary.**

Expose `HasYardBindingConflict`, `BindingStatusText`, `BindingDetailText`, and a summary text/error state. Keep full binding details available through the existing `查看` button/tool tip.

- [ ] **Step 2: Update `RefreshBindingOverview` to separate status from details.**

For a cross-yard binding, show `⚠ 归属冲突` as the short status and `地图所属站场 / 点位名称` as the detail. For normal binding show the existing concise location. Count only visible rows for the current filter and set a single summary message such as `当前存在 3 个通信归属与地图绑定冲突，请处理后保存。`.

- [ ] **Step 3: Bind warning status and summary in XAML.**

Use the existing warning brush for conflict status, keep the table compact, and remove reliance on long inline error strings at the bottom. Keep the full message in the existing dialog path for `查看` or save rejection.

- [ ] **Step 4: Run the focused markup tests and the full Core test project.**

Expected: all tests pass without touching communication/runtime code.

### Task 4: Verify SettingsPage filtering and save protection end to end

**Files:**
- Modify tests only if a missing assertion is discovered.

- [ ] **Step 1: Exercise 20 filter cycles over 560, 620, 全部, 未归属.**

Confirm every displayed station ID is unique on each cycle and RFID-04/05/06 appear once each in 620.

- [ ] **Step 2: Confirm cross-yard binding remains a save blocker.**

Use the existing binding/save tests and verify no YardId or map binding is changed automatically.

- [ ] **Step 3: Run the required validation commands.**

```text
Core Tests
Infrastructure Tests
Release Build
Simulator Build
RFID Acceptance 8/8
git diff --check
```

- [ ] **Step 4: Report files, root cause, behavior, and test results.**

Do not commit or push.
