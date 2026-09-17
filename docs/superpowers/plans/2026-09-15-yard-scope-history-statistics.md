# 站场历史与统计范围隔离 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让当前选中的站场范围同时约束首页、历史、报警和 RFID 统计数据，避免 560 的记录显示到 620。

**Architecture:** 在主窗口维护当前站场对应的 RFID StationId 集合；页面接收该集合作为显示范围。页面内部继续使用现有 PassageRecordStore，不改变数据库记录、Runtime、Poller 或协议。

**Tech Stack:** C#、WPF、.NET Framework 4.8、现有 Core 测试项目。

**Spec:** 当前用户需求：560 记录不能在切换到 620 后继续显示。

## Global Constraints

- 不删除已有数据库记录。
- 不修改正式 Protocol、Runtime、Poller、SQLite schema 或 PassageRecord 保存逻辑。
- 当前站场为空时，统计和列表显示 0，而不是全局数据。
- 全局总览仍显示全部站场数据。
- 不 commit，不 push。

---

### Task 1: 增加站场范围过滤的可测试核心逻辑

**Files:**
- Create: `src/MineRailMonitor.Core/Services/YardPassageFilter.cs`
- Test: `tests/MineRailMonitor.Core.Tests/YardPassageFilterTests.cs`

**Interfaces:**
- Produces `YardPassageFilter.Filter(IEnumerable<PassageRecord>, IEnumerable<string>?)`。

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Filter_keeps_only_records_from_current_yard_station_ids()
{
    var records = new[]
    {
        CreateRecord("RFID-01"),
        CreateRecord("RFID-02"),
        CreateRecord("RFID-03")
    };

    var filtered = YardPassageFilter.Filter(records, new[] { "RFID-01", "RFID-03" });

    Assert.Equal(new[] { "RFID-01", "RFID-03" }, filtered.Select(record => record.StationId));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~YardPassageFilterTests`

Expected: FAIL because `YardPassageFilter` does not exist.

- [ ] **Step 3: Write minimal implementation**

Implement a case-insensitive StationId allow-list filter. A null station-id collection means global scope and returns all records; an empty collection returns no records.

- [ ] **Step 4: Run test to verify it passes**

Run the same `dotnet test` command. Expected: PASS.

### Task 2: Apply the range to the main-window dashboard

**Files:**
- Modify: `src/MineRailMonitor/MainWindow.xaml.cs:1057-1070`
- Modify: `src/MineRailMonitor/Pages/MonitorPage.xaml.cs:425-444`
- Test: `tests/MineRailMonitor.Core.Tests/YardContextMarkupTests.cs`

**Interfaces:**
- `MainWindow` passes the current yard's StationId allow-list to dashboard statistics.
- `MonitorPage.SetHistoricalStatistics` receives already scoped statistics.

- [ ] **Step 1: Write the failing regression assertion**

Assert that `RefreshHistoricalStatistics` does not call the unscoped statistics path for a non-global yard, and that the dashboard station summary excludes StationIds outside the selected scope.

- [ ] **Step 2: Run the targeted regression test and verify it fails**

Run: `dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~YardContextMarkupTests`

Expected: FAIL against the current unscoped dashboard statistics behavior.

- [ ] **Step 3: Implement the minimal scoped dashboard path**

Use the current yard's StationIds to filter today's records and construct `PassageStatistics` before calling `MonitorPage.SetHistoricalStatistics`. Keep `ResolveAll()` behavior unchanged for global overview.

- [ ] **Step 4: Run the targeted test and verify it passes**

Run the same command. Expected: PASS.

### Task 3: Apply the range to history, alarm, and statistics pages

**Files:**
- Modify: `src/MineRailMonitor/Pages/HistoryPage.xaml.cs`
- Modify: `src/MineRailMonitor/Pages/AlarmHistoryPage.xaml.cs`
- Modify: `src/MineRailMonitor/Pages/RfidStatisticsPage.xaml.cs`
- Modify: `src/MineRailMonitor/MainWindow.xaml.cs:273-340,1113-1180`
- Test: `tests/MineRailMonitor.Core.Tests/YardContextMarkupTests.cs`

**Interfaces:**
- Add `SetDisplayScope(IEnumerable<string>?)` to the three pages.
- `null` means global overview; an empty set means no records; non-empty values limit all page queries and counters by StationId.

- [ ] **Step 1: Write the failing regression assertions**

Cover these behaviors: after selecting 620, history and alarm queries receive only 620 StationIds; statistics metrics do not use unscoped `GetStatistics`; an empty 620 scope produces zero rows/counters; selecting global restores all records.

- [ ] **Step 2: Run the targeted tests and verify they fail**

Run: `dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~YardContextMarkupTests`

Expected: FAIL because the pages currently have no display-scope API and build queries from all stations.

- [ ] **Step 3: Implement the minimal page scope**

Store a case-insensitive StationId set in each page, merge it with the user-selected StationId filter, and filter all list results, counters, ranking, recent rows, and trend input. Update the main window whenever `CurrentYardContext` changes and before navigating to History, Alarms, or RFID Statistics.

- [ ] **Step 4: Run the targeted tests and verify they pass**

Run the same command. Expected: PASS.

### Task 4: Full verification

**Files:**
- No additional production files.

- [ ] **Step 1: Run Core Tests**

Run the repository's existing Core test command and expect all tests to pass.

- [ ] **Step 2: Run Infrastructure Tests**

Expect all tests to pass.

- [ ] **Step 3: Run Release Build and Simulator Build**

Expect both builds to pass without new warnings or errors.

- [ ] **Step 4: Run RFID Acceptance 8/8**

Expect 8/8 and verify the acceptance database path is untouched by the display-only change.

- [ ] **Step 5: Run diff and status checks**

Run `git diff --check` and `git status`; report modified files and do not commit or push.
