# Alarm UDP Forwarding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变 RFID 协议和本地报警生命周期的前提下，把每个 `UncouplingAlarm` Passage 的最后一次新增 RFID 原始 UDP 报文按 Yard 原样发送一次。

**Architecture:** 在 `StationRuntimeState` 中保存当前 RecognitionSession 最后一次新增 RFID 的 raw clone，并由 `RfidRuntimeCoordinator` 在 `Save(PassageRecord)` 成功后、退出锁后发布一次 `AlarmForwardRequest`。`YardCommunicationContext` 在锁外启动一次异步发送；Core 只依赖现有 `IExternalDataInterface`，Infrastructure 提供绑定目标端点的 UDP 实现。

**Tech Stack:** C#/.NET Framework 4.8, WPF, xUnit, `System.Net.Sockets.UdpClient`, existing JSON configuration and SQLite record store.

**Spec:** User-provided requirements in the task conversation; no machine-local attachment path is required.

## Global Constraints

- Forward only `PassageOutcome.UncouplingAlarm`.
- Forward the saved raw `byte[]` without JSON, encoding, framing, CRC, or protocol reconstruction.
- Use last raw frame that actually increases the current RecognitionSession RFID count.
- Never key raw state by `ProtocolAddress` alone; retain Yard and station endpoint identity.
- Mark one forward attempt per Passage after persistence succeeds; do not retry.
- Never perform network I/O or wait for it under `RfidRuntimeCoordinator`'s lock.
- Missing `YardAlarmForwards` in old manifests means disabled forwarding.
- Do not add ACK, retry, recovery notification, new SQLite schema, or a new alarm protocol.
- Do not modify the v1.0.0 tag/release, merge main, or create a release/tag.
- Preserve the pre-existing untracked `docs/v1.0.0-smoke-test-checklist.md`.

## Review Focus

- Empty valid frames must not overwrite the last-new-RFID raw; covered by Task 1.
- Repeated non-empty RFID frames must not overwrite the last-new-RFID raw; covered by Task 1.
- Same protocol address across 560/620 must route by endpoint and Yard; covered by Task 2.
- Persistence failure and retry must produce zero then one forward request; covered by Task 2.
- Legacy and missing configuration must not enable network output; covered by Task 3.

---

### Task 1: Raw packet capture and coordinator one-shot request

**Files:**
- Modify: `src/MineRailMonitor.Core/Models/StationRuntimeState.cs`
- Modify: `src/MineRailMonitor.Core/Services/RfidRuntimeCoordinator.cs`
- Modify: `src/MineRailMonitor.Core/Models/RfidStationFrame.cs` only if a defensive raw helper is required; do not alter protocol parsing.
- Create: `tests/MineRailMonitor.Core.Tests/AlarmForwardingTests.cs`

**Interfaces:**
- Produces `AlarmForwardRequest` and `RfidRuntimeCoordinator.AlarmForwardRequested` for the Yard layer.
- The request contains `PassageId`, `StationId`, `StationAddress`, and a cloned raw payload; an empty payload represents missing raw data and must never be sent.

- [ ] **Step 1: Write the failing tests** for last-new-RFID capture, repeated-frame protection, empty-frame protection, one request across repeated Evaluate calls, Clear/WaitForEmpty/Acknowledge isolation, and new Passage re-arming.
- [ ] **Step 2: Run the focused Core test class** and confirm it fails because the request event/state do not exist.
- [ ] **Step 3: Add the smallest state/request implementation**: capture `(byte[])frame.RawData.Clone()` only when `DetectedVehicleCount` increases; clear it on new Session and `ResetToIdle`; create the request only after successful `Save` and only once for the current Passage.
- [ ] **Step 4: Publish requests after the coordinator lock** from both the normal alarm path and persistence-retry path; run the focused tests and confirm they pass.
- [ ] **Step 5: Run the existing Core lifecycle and endpoint tests** and keep all existing behavior green.

### Task 2: Yard forwarding boundary and failure isolation

**Files:**
- Create: `src/MineRailMonitor.Core/Models/AlarmForwardRequest.cs`
- Modify: `src/MineRailMonitor.Core/Services/YardCommunicationContext.cs`
- Modify: `src/MineRailMonitor.Core/Services/YardCommunicationManager.cs`
- Modify: `tests/MineRailMonitor.Core.Tests/YardCommunicationManagerTests.cs`
- Modify: `tests/MineRailMonitor.Core.Tests/YardCommunicationClosedLoopTests.cs` only if required for lifecycle coverage.

**Interfaces:**
- `YardCommunicationContext` accepts an optional `IExternalDataInterface` sender and subscribes to the coordinator request event.
- `YardCommunicationManager` accepts per-Yard sender instances without changing `IExternalDataInterface`.
- Sender failures raise/log through a non-fatal event and never escape into RFID polling or lifecycle code.

- [ ] **Step 1: Write the failing tests** for 560/620 target isolation, same ProtocolAddress isolation, exact payload equality, sender exception isolation, and no sender when disabled.
- [ ] **Step 2: Run those tests** and confirm the expected compile/test failures before production changes.
- [ ] **Step 3: Add the request model and context dispatch**; start one fire-and-forget task outside the coordinator lock, catch all send exceptions, and expose diagnostic failure data without changing `LastError`/RFID health.
- [ ] **Step 4: Add manager wiring and run focused tests**; verify that one Yard's sender cannot receive the other Yard's request.
- [ ] **Step 5: Run the full Core test project** and resolve only regressions caused by this feature.

### Task 3: Independent Yard alarm-forward configuration and persistence

**Files:**
- Create: `src/MineRailMonitor.Core/Models/YardAlarmForwardConfig.cs`
- Modify: `src/MineRailMonitor.Core/Models/ProjectConfig.cs`
- Modify: `src/MineRailMonitor.Infrastructure/Configuration/ProjectConfigService.cs`
- Create or modify: `tests/MineRailMonitor.Infrastructure.Tests/YardCommunicationConfigurationTests.cs`
- Modify: `tests/MineRailMonitor.Infrastructure.Tests/ProjectConfigPersistenceTests.cs`

**Interfaces:**
- `YardAlarmForwardConfig.Validate()` rejects invalid enabled IP/port and permits empty target values when disabled.
- `ProjectConfig.YardAlarmForwards` is an optional manifest list with disabled-by-default behavior.
- Save/load APIs preserve independent 560/620 values and permit equal target endpoints.

- [ ] **Step 1: Write failing persistence and validation tests** for independent targets, same target, disabled blank target, invalid IP, invalid ports, missing property, and legacy listener enablement refusal.
- [ ] **Step 2: Run the focused Infrastructure tests** and confirm they fail because the model/manifest field/save path do not exist.
- [ ] **Step 3: Implement the model and manifest mapping** with no changes to `YardCommunicationConfig` listen fields; missing manifest property maps to disabled defaults.
- [ ] **Step 4: Implement save/load validation and run focused tests** until green.
- [ ] **Step 5: Run the full Infrastructure test project**.

### Task 4: Infrastructure UDP sender and WPF composition/UI

**Files:**
- Create: `src/MineRailMonitor.Infrastructure/ExternalData/UdpExternalDataInterface.cs`
- Modify: `src/MineRailMonitor/MainWindow.xaml.cs`
- Modify: `src/MineRailMonitor/Pages/SettingsPage.xaml`
- Modify: `src/MineRailMonitor/Pages/SettingsPage.xaml.cs`
- Modify: `src/MineRailMonitor/Pages/YardCommunicationConfigDialog.xaml`
- Modify: `src/MineRailMonitor/Pages/YardCommunicationConfigDialog.xaml.cs`
- Optional only if diagnostics are simple: `src/MineRailMonitor/Pages/CommunicationPage.xaml.cs`
- Create: `tests/MineRailMonitor.Infrastructure.Tests/UdpExternalDataInterfaceTests.cs`
- Modify: WPF markup tests only for the new labels/controls.

**Interfaces:**
- `UdpExternalDataInterface(IPEndPoint target)` implements unchanged `IExternalDataInterface.SendAsync(byte[], CancellationToken)` and sends exactly the supplied byte count.
- MainWindow constructs one sender per enabled Yard target and passes it to the manager; disabled/invalid targets never create a sending path.

- [ ] **Step 1: Write the failing UDP sender test** using a loopback listener and assert exact length and bytes.
- [ ] **Step 2: Run it** and confirm the implementation type is missing.
- [ ] **Step 3: Implement the minimal sender** with `UdpClient.SendAsync(payload, payload.Length, target)` and no transformation; run the sender test green.
- [ ] **Step 4: Add the Settings dialog fields, draft/save/reload handling, admin gating, and MainWindow sender composition**; run focused configuration/UI tests.
- [ ] **Step 5: Run all available Core, Infrastructure, and WPF tests** before refactoring.

### Task 5: Full verification and loopback acceptance

**Files:**
- Modify only tests or production files needed to fix verified failures; do not alter protocol semantics.

- [ ] **Step 1: Run the complete solution build** and capture exit code.
- [ ] **Step 2: Run full Core and Infrastructure test projects plus any WPF test project** and capture passed/failed/skipped counts.
- [ ] **Step 3: Run the non-destructive RFID dual-Yard acceptance path if available.**
- [ ] **Step 4: Run two local UDP listeners, trigger 560 then 620 alarms, record length/hex/SHA256, and compare each forwarded payload with the corresponding original raw packet using `SequenceEqual`.
- [ ] **Step 5: Inspect `git diff --stat`, `git status`, and confirm v1.0.0 Release/tag was not modified, main was not merged, and the smoke-test checklist remains untracked and unchanged.**
