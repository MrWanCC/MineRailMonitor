# Vehicle Passage Identity Implementation Plan

> **For agentic workers:** Execute this plan natively in the current task. Steps use checkbox syntax for tracking.

**Goal:** Snapshot matched vehicle identity into each passage, persist it in SQLite schema v4, and show vehicle-specific uncoupling details without changing alarm decisions.

**Architecture:** Keep `StationRecognitionSession` as the immutable per-passage snapshot owner. Copy matched name/head RFID into `PassageRecord`, persist nullable columns in the current SQLite store, and format identity from persisted records at display boundaries. Unmatched passages retain the current generic alarm.

**Tech Stack:** C#, .NET, WPF, SQLite, xUnit.

**Spec:** User request in this task.

## Global Constraints

- Do not change carriage Position validation.
- Do not change RFID counting or uncoupling decision logic.
- Unmatched vehicles keep generic alarm behavior.
- Migrate SQLite schema version 3 to version 4 without losing existing records.

## Review Focus

- Unmatched records must keep null vehicle identity and generic alarm display.
- Existing v1/v2 databases must pass through v3 and reach v4.
- Vehicle identity must persist through in-memory copies, SQLite save/load, and pending recovery.
- Vehicle head display must use the configured protocol-slot head RFID, not infer from current numeric RFID rules.
- Displayed missing count must not go below zero.

---

### Task 1: Passage snapshot and formatting

**Files:** `StationRecognitionSession.cs`, `PassageRecord.cs`, `RfidRuntimeCoordinator.cs`, `PassageAlarmDisplayFormatter.cs`; test `VehiclePassageIdentityTests.cs`.

- [x] Add tests proving a matched session snapshot reaches a frozen alarm record and unmatched remains generic.
- [x] Run the new tests and verify they fail on the missing identity contract.
- [x] Store the immutable matched result on session creation; write matched identity to the frozen record.
- [x] Add summary and multiline detail formatting for matched records only.
- [x] Run focused Core tests.

### Task 2: SQLite v4 persistence

**Files:** `SqlitePassageRecordStore.cs`, `InMemoryPassageRecordStore.cs`; test `SqlitePassageRecordStoreTests.cs`.

- [x] Extend schema and round-trip tests to require v4 identity columns.
- [x] Verify fresh-schema and v3-migration tests fail before implementation.
- [x] Add nullable identity columns, write/read parameters, and migrate v3 to v4 transactionally; preserve identities in in-memory record copies.
- [x] Run focused Infrastructure tests, including legacy v1/v2 migration coverage.

### Task 3: Alarm display integration

**Files:** `AlarmHistoryPage.xaml.cs`, `MonitorPage.xaml.cs`, `PassageDetailsDialog.xaml.cs`; tests for display formatting.

- [x] Test summary/detail output for matched vehicle and generic fallback for unmatched record.
- [x] Show a concise vehicle summary in alert lists and the requested multiline fields in passage details.
- [x] Run focused Core and Infrastructure tests, then the full test suite.

### Task 4: Verification

- [x] Build the solution and run the full test suite.
- [x] Review the diff to confirm no RFID counting, Position validation, or alarm-decision code changed.
