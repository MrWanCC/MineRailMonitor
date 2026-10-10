# External Alarm Alarm Latch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Keep External Alarm status `03` latched after an RFID internal clear until a valid external clear command releases it, then send `01` for two minutes before `00`.

**Architecture:** Keep the existing RFID runtime and internal Clear/WaitForEmpty flow unchanged. Add latch metadata and release semantics to `ExternalAlarmStationSnapshot`/`ExternalAlarmStatusCache`; route the existing yard-level external clear callback through the coordinator so only a matching latched station is released.

**Tech Stack:** C#, .NET Framework 4.8, xUnit, existing MineRailMonitor Core services.

**Spec:** User request in the current conversation.

## Global Constraints

- Do not modify `RfidFrameParser`, `RfidStationPoller`, `RfidRuntimeCoordinator`, Passage recognition, RFID Clear/WaitForEmpty, or the RFID protocol.
- Only External Alarm snapshot/cache/coordinator behavior and its existing `YardCommunicationContext` integration may change.
- Internal RFID recovery must not clear `AlarmLatched`.
- A valid matching External Clear must change `03` to `01`; existing two-minute Normal retention then changes it to `00`.
- Do not commit, push, change version, or change Release assets.

## Review Focus

- Internal RFID recovery while the alarm is latched: it must continue sending `03`; covered by the context/cache latch test.
- External clear for the wrong address: it must not release another station; covered by the cache/coordinator test.
- Same address in different yards: each yard coordinator must release only its own cache; covered by the existing multi-yard boundary test plus the new release test.
- Station identity during recovery: the snapshot must remain `RFID-620-01`, not fall back to `RFID-01`; covered by the recovery snapshot test.
- Normal retention after a valid clear: `01` must become `00` only after two minutes; covered by the scheduler/cache test.

### Task 1: Add failing latch tests

**Files:**
- Create: `tests/MineRailMonitor.Core.Tests/ExternalAlarmLatchTests.cs`
- Test support: existing `ExternalAlarmStatusTests` helpers may be reused only if they remain private-compatible; otherwise keep local minimal helpers.

**Interfaces:**
- The tests will exercise `ExternalAlarmStatusCache.Update`, `TryReleaseAlarm`, `ExternalAlarmStatusCoordinator.PublishAsync`, and the existing `YardCommunicationContext.ProcessFrame`/`Evaluate` path.

- [ ] Write tests for alarm latching, internal recovery preservation, matching/incorrect clear, station identity, and two-minute retention.
- [ ] Run the new test file and verify it fails because latch metadata/release behavior is not implemented.

### Task 2: Implement snapshot/cache latch semantics

**Files:**
- Modify: `src/MineRailMonitor.Core/ExternalAlarm/ExternalAlarmStationSnapshot.cs`
- Modify: `src/MineRailMonitor.Core/ExternalAlarm/ExternalAlarmStatusCache.cs`

**Interfaces:**
- Add `AlarmLatched` and nullable `AlarmTime` to the snapshot while preserving existing constructor call compatibility.
- Add `TryReleaseAlarm(byte stationAddress, DateTimeOffset releasedAt, out ExternalAlarmStationSnapshot snapshot)` to the cache.

- [ ] Make `03` updates latch and timestamp the station.
- [ ] Make Normal/NoVehicle updates preserve a live latch until explicit release.
- [ ] Release exactly one matching latched station; reject none or ambiguous matches.
- [ ] Preserve station ID when creating recovery/NoVehicle snapshots.
- [ ] Run the new tests and verify they pass.

### Task 3: Connect external clear to latch release

**Files:**
- Modify: `src/MineRailMonitor.Core/ExternalAlarm/ExternalAlarmStatusCoordinator.cs`
- Modify: `src/MineRailMonitor.Core/Services/YardCommunicationContext.cs`

**Interfaces:**
- Add coordinator method `TryReleaseAlarm(byte stationAddress, DateTimeOffset releasedAt)` delegating to the per-yard cache.
- Keep the existing external clear receiver and runtime acknowledgement path; combine their results without changing runtime behavior.

- [ ] Release the external latch only after a valid yard-local clear address matches.
- [ ] Preserve `YardCommunicationContext`’s existing Clear/WaitForEmpty call path.
- [ ] Pass `state.StationId` into recovery snapshots so UI/log identity remains stable.
- [ ] Run the new tests and the existing External Alarm tests.

### Task 4: Verify the full solution

**Files:** None.

- [ ] Run `dotnet build MineRailMonitor.sln -c Release --no-restore`.
- [ ] Run Core and Infrastructure tests plus the existing UI markup tests.
- [ ] Run `git diff --check` and `git status --short`.
- [ ] Confirm no commit/push and report remaining pre-existing worktree changes.
