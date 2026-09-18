# Alarm Acknowledgement and Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persistent uncoupling-alarm acknowledgement and recovery lifecycle while preserving the existing RFID clear lifecycle and dual-yard communication design.

**Architecture:** Extend `PassageRecord` and both passage stores with nullable acknowledgement/recovery timestamps. Keep the existing Clear state machine unchanged, but let `RfidRuntimeCoordinator` maintain a per-station set of unacknowledged alarm PassageIds and derive visual alarm state from that set plus the active lifecycle. Route UI confirmation through `YardCommunicationContext` so SQLite success precedes runtime latch removal.

**Tech Stack:** .NET Framework 4.8, C#, WPF, xUnit, System.Data.SQLite, existing Core/Infrastructure architecture.

**Spec:** `docs/superpowers/specs/2026-09-18-alarm-ack-recovery-design.md`

## Global Constraints

- Only `PassageOutcome.UncouplingAlarm` requires acknowledgement.
- Warning records remain visible but show “无需确认” and have no acknowledgement action.
- `PassageClearState` remains independent from acknowledgement/recovery fields.
- Do not modify RFID protocol, Raw Packet Black Box, simulator fault injection, .NET Framework target, or unrelated architecture.
- Preserve independent 560/620 contexts and never match restart records by ProtocolAddress alone when stable station identity is available.
- Do not add `Acknowledged` or `Recovered` to `PassageLifecycleState`.

### Task 1: Extend the passage model and in-memory store

**Files:**
- Modify: `src/MineRailMonitor.Core/Models/PassageRecord.cs`
- Modify: `src/MineRailMonitor.Core/Interfaces/IPassageRecordStore.cs`
- Modify: `src/MineRailMonitor.Core/Services/InMemoryPassageRecordStore.cs`
- Test: `tests/MineRailMonitor.Core.Tests/PassageRecordStoreTests.cs`

- [ ] Write tests for constructor timestamp preservation, warning exclusion, idempotent acknowledgement, and clear recovery timestamp.
- [ ] Run the focused Core tests and confirm the new assertions fail because the fields/API do not exist.
- [ ] Add nullable constructor parameters at the end, properties, and `RequiresAlarmAcknowledgement`/timestamp helpers.
- [ ] Add `MarkAlarmAcknowledged` and `GetUnacknowledgedAlarms` to the interface and implement them in memory.
- [ ] Make in-memory `MarkCleared` set `AlarmRecoveredAt` only for uncoupling alarms while preserving acknowledgement time.
- [ ] Rerun focused Core tests.

### Task 2: Upgrade SQLite persistence to schema v3

**Files:**
- Modify: `src/MineRailMonitor.Infrastructure/Persistence/SqlitePassageRecordStore.cs`
- Modify: `tests/MineRailMonitor.Infrastructure.Tests/SqlitePassageRecordStoreTests.cs`

- [ ] Add failing tests for v3 columns, v2 Cleared migration, v2 PendingClear migration, timestamp round-trip, acknowledgement idempotency, warning rejection, and unacknowledged query filtering.
- [ ] Run the focused Infrastructure tests and verify they fail before implementation.
- [ ] Create fresh databases as schema v3 and add both nullable columns to INSERT/SELECT/update paths.
- [ ] Implement v2 → v3 migration in a transaction, backfilling only Cleared uncoupling alarms with `COALESCE(cleared_at, completed_at)`.
- [ ] Implement `MarkAlarmAcknowledged` with first-write-wins semantics and exceptions for missing/non-alarm records.
- [ ] Implement `GetUnacknowledgedAlarms` using `result = UncouplingAlarm AND alarm_acknowledged_at IS NULL`.
- [ ] Rerun focused Infrastructure tests.

### Task 3: Add runtime alarm latch and stable restoration

**Files:**
- Modify: `src/MineRailMonitor.Core/Models/StationRuntimeState.cs`
- Modify: `src/MineRailMonitor.Core/Services/RfidRuntimeCoordinator.cs`
- Modify: `src/MineRailMonitor.Core/Services/YardCommunicationContext.cs`
- Modify: `src/MineRailMonitor.Core/Services/YardCommunicationManager.cs` only if a wrapper is required by the UI path
- Test: `tests/MineRailMonitor.Core.Tests/PassageLifecycleTests.cs`
- Test: new `tests/MineRailMonitor.Core.Tests/AlarmAcknowledgementTests.cs`
- Test: `tests/MineRailMonitor.Infrastructure.Tests/Phase32SqliteIntegrationTests.cs` where restart behavior is exercised

- [ ] Add failing tests for recovery without acknowledgement, acknowledgement before recovery, two unacknowledged alarms, restart restoration, same ProtocolAddress in 560/620, and store-write failure.
- [ ] Run the focused tests and verify expected failures.
- [ ] Add a per-`StationRuntimeState` unacknowledged PassageId set with snapshot access.
- [ ] Add `RestoreUnacknowledgedAlarms`, `HasUnacknowledgedAlarm`, and `AcknowledgeAlarm` APIs.
- [ ] Register an alarm PassageId only after `Save` succeeds; retain it after `MarkCleared`; remove it only after acknowledgement persistence succeeds.
- [ ] Update visual derivation to use the explicit set and active lifecycle, eliminating the old implicit “VisualState was Alarm” latch.
- [ ] Filter restore records by stable StationId/context before coordinator restoration; keep protocol fallback only for unambiguous compatibility cases.
- [ ] Preserve existing pending-clear behavior and run focused runtime tests.

### Task 4: Restore alarm latches at application startup and route confirmation

**Files:**
- Modify: `src/MineRailMonitor/MainWindow.xaml.cs`
- Modify: `src/MineRailMonitor.Core/Services/YardCommunicationContext.cs` if not completed in Task 3
- Test: `tests/MineRailMonitor.Core.Tests/YardContextMarkupTests.cs` or an existing MainWindow markup test

- [ ] Add a failing source/behavior assertion that startup restores `GetUnacknowledgedAlarms` in addition to pending Clear records.
- [ ] Load both lists once during manager recreation and pass them through each context’s stable station filter.
- [ ] Add a MainWindow acknowledgement callback that locates the owning context, persists acknowledgement, then refreshes runtime/monitor state.
- [ ] Ensure exceptions propagate to the AlarmHistoryPage error area without clearing the red state.
- [ ] Run focused startup/route tests.

### Task 5: Extend AlarmHistoryPage and PassageDetailsDialog

**Files:**
- Modify: `src/MineRailMonitor/Pages/AlarmHistoryPage.xaml`
- Modify: `src/MineRailMonitor/Pages/AlarmHistoryPage.xaml.cs`
- Modify: `src/MineRailMonitor/Pages/PassageDetailsDialog.xaml`
- Modify: `src/MineRailMonitor/Pages/PassageDetailsDialog.xaml.cs`
- Test: `tests/MineRailMonitor.Core.Tests/HistoryPageMarkupTests.cs`

- [ ] Add failing markup assertions for status column, confirmation action, warning suppression, acknowledgement/recovery details.
- [ ] Add status text for the four alarm combinations and “无需确认” for warnings.
- [ ] Add a visible confirmation button only for unacknowledged uncoupling alarms; confirmed and warning rows have no usable confirmation action.
- [ ] On success refresh the current list; on failure set the existing query error text and leave runtime unchanged.
- [ ] Add detail fields with `-` for missing timestamps and warning status “无需确认”.
- [ ] Run the focused markup tests.

### Task 6: Full regression and handoff

**Files:**
- No production file changes beyond Tasks 1–5.

- [ ] Run `dotnet build MineRailMonitor.sln -c Release`.
- [ ] Run Core Release tests with `--no-build`.
- [ ] Run Infrastructure Release tests with `--no-build`.
- [ ] Run `scripts/run-rfid-acceptance.ps1` and confirm all 8 scenarios pass.
- [ ] Review `git diff --check`, status, and `git diff --stat main...HEAD`.
- [ ] Commit to `feat/alarm-ack-recovery` and push without merging main.
