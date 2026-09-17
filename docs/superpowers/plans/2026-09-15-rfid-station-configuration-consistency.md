# RFID Station Configuration Consistency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent RFID station configuration from becoming inconsistent across yard ownership, map binding, runtime refresh, and page-level station metadata.

**Architecture:** Keep `RfidStationConfig.YardId` as the station's explicit yard ownership and keep `DeviceConfig.RfidStationId` as the optional one-to-one map-point binding. Validate the relationship at persistence time, include explicit ownership in yard display scopes with legacy map-binding fallback, and restart Poller/Runtime only when communication-affecting fields change.

**Tech Stack:** .NET 8 test projects, .NET Framework 4.8 WPF host, C#, xUnit, Newtonsoft.Json project persistence.

**Spec:** `docs/superpowers/plans/2026-09-14-yard-context-monitor-filter.md` plus the current RFID station configuration consistency requirements.

## Global Constraints

- Do not modify RFID protocol framing, Runtime recognition rules, Passage lifecycle, SQLite schema, or UI layout.
- Preserve legacy duplicate map bindings as diagnostics; never auto-delete or auto-overwrite them.
- A station ownership change must not restart Poller/Runtime when endpoint and communication settings are unchanged.
- Existing uncommitted work is user-owned; do not reset, clean, commit, or push it.

---

### Task 1: Add failing ownership and configuration-change tests

**Files:**
- Create: `src/MineRailMonitor.Core/Services/RfidStationOwnershipRules.cs`
- Create: `src/MineRailMonitor.Core/Services/RfidStationConfigurationChangeRules.cs`
- Test: `tests/MineRailMonitor.Core.Tests/RfidStationOwnershipRulesTests.cs`
- Test: `tests/MineRailMonitor.Core.Tests/RfidStationConfigurationChangeRulesTests.cs`
- Test: `tests/MineRailMonitor.Core.Tests/YardRfidStationResolverTests.cs`

**Interfaces:**
- `RfidStationOwnershipRules.Validate(yards, stations)` returns user-facing validation messages.
- `RfidStationConfigurationChangeRules.RequiresRuntimeRestart(previous, candidate)` returns whether communication/runtime state must be rebuilt.

- [x] **Step 1: Write tests for invalid yard ownership, cross-yard map mismatch, valid same-yard binding, and metadata-only changes.**
- [x] **Step 2: Add resolver tests showing an owned station appears in its yard scope and is excluded from unbound scope.**
- [x] **Step 3: Run the focused Core tests and verify they fail for the missing rules/behavior.**

### Task 2: Implement minimal consistency and scope behavior

**Files:**
- Modify: `src/MineRailMonitor.Core/Services/RfidStationOwnershipRules.cs`
- Modify: `src/MineRailMonitor.Core/Services/RfidStationConfigurationChangeRules.cs`
- Modify: `src/MineRailMonitor.Core/Services/YardRfidStationResolver.cs`
- Modify: `tests/MineRailMonitor.Core.Tests/YardRfidStationResolverTests.cs`

- [x] **Step 1: Implement ownership validation without mutating yard or binding data.**
- [x] **Step 2: Include explicit `YardId` ownership in `Resolve`, while retaining explicit map bindings for legacy configurations.**
- [x] **Step 3: Exclude explicitly owned stations from `ResolveUnbound`.**
- [x] **Step 4: Run focused tests and verify they pass.**

### Task 3: Wire persistence and prevent unnecessary runtime restart

**Files:**
- Modify: `src/MineRailMonitor.Infrastructure/Configuration/ProjectConfigService.cs`
- Modify: `src/MineRailMonitor/MainWindow.xaml.cs`
- Test: `tests/MineRailMonitor.Infrastructure.Tests/ProjectConfigPersistenceTests.cs`
- Test: `tests/MineRailMonitor.Core.Tests/SettingsPageMarkupTests.cs`

- [x] **Step 1: Add persistence tests proving invalid yard IDs and cross-yard ownership/binding mismatches are rejected without writing files.**
- [x] **Step 2: Validate candidate station ownership against the currently loaded project before writing `project.json`.**
- [x] **Step 3: Rebuild Poller/Runtime only when communication-affecting station fields changed; keep metadata-only changes live.**
- [x] **Step 4: Run Core and Infrastructure tests.**

### Task 4: Refresh all page station metadata after a successful station save

**Files:**
- Modify: `src/MineRailMonitor/Pages/HistoryPage.xaml.cs`
- Modify: `src/MineRailMonitor/Pages/AlarmHistoryPage.xaml.cs`
- Modify: `src/MineRailMonitor/Pages/RfidStatisticsPage.xaml.cs`
- Modify: `src/MineRailMonitor/MainWindow.xaml.cs`
- Test: `tests/MineRailMonitor.Core.Tests/SettingsPageMarkupTests.cs`

- [x] **Step 1: Add public station-list refresh methods that preserve the current filters where possible.**
- [x] **Step 2: Refresh Communication, History, Alarm, Statistics, and Monitor station lists from the saved candidate list.**
- [x] **Step 3: Verify changed names/endpoints do not leave stale page selectors or labels.**

### Task 5: Verification

- [x] **Step 1: Run focused changed tests.**
- [x] **Step 2: Run Core Tests.**
- [x] **Step 3: Run Infrastructure Tests.**
- [x] **Step 4: Run Release Build and Simulator Build.**
- [x] **Step 5: Run RFID Acceptance 8/8.**
- [x] **Step 6: Run `git diff --check` and `git status`; do not commit or push.**
