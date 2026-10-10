# RFID Runtime Safety Fixes Implementation Plan

> **For agentic workers:** Implement natively in this task. Steps use checkbox syntax for tracking.

**Goal:** Prevent RFID responses from being assigned to an arbitrary station and let each station use its own expected train length.

**Architecture:** Keep runtime state keyed by endpoint plus protocol address. Protocol-only compatibility lookups must succeed only when unique. Add an optional expected vehicle count to each station, falling back to the existing project-wide value, and carry it through settings persistence and runtime session creation.

**Tech Stack:** .NET, C#, WPF, xUnit.

**Spec:** Current task request and the reported 620-01 (11-car) / 620-02 (6-car) behavior.

## Global Constraints

- Do not change External Alarm protocol bytes or alarm-state transitions.
- Preserve existing project files: an absent station override uses the current global `RfidSettings.ExpectedVehicleCount`.
- Do not commit changes.

## Review Focus

- Endpoint-less frames with duplicate Byte2 addresses must not mutate either station; test endpoint routing and ambiguous legacy lookup.
- A station override must control initial state, recognition completion, and persisted `PassageRecord`; test 6-car and 11-car stations together.
- Blank override must retain the project-wide default; test legacy project configuration behavior.
- Invalid nonblank station counts must be rejected by settings, project loading/saving, and runtime configuration; test bounds 1 and 14, and reject 0/15.
- Station override must survive settings row conversion and project serialization; test config conversion and markup binding.

---

### Task 1: Make protocol-only runtime routing ambiguity-safe

**Files:**
- Modify: `src/MineRailMonitor.Core/Services/RfidRuntimeCoordinator.cs`
- Test: `tests/MineRailMonitor.Core.Tests/RfidStationEndpointTests.cs`

**Interfaces:** Existing lookup APIs stay unchanged. `TryGetFirstState` returns a state only when exactly one configured state has that protocol address.

- [x] Add a regression test with two stations sharing Byte2 but using different endpoints; an endpoint-less frame and Byte2-only command lookup must not select either station.
- [x] Run the focused test and confirm it fails because the first station currently receives the frame.
- [x] Change the protocol-only lookup to require exactly one match.
- [x] Run the focused endpoint tests and confirm explicit endpoint routing and single-station compatibility still pass.

### Task 2: Support per-station expected vehicle count

**Files:**
- Modify: `src/MineRailMonitor.Core/Models/RfidStationConfig.cs`
- Modify: `src/MineRailMonitor.Core/Services/RfidRuntimeCoordinator.cs`
- Modify: `src/MineRailMonitor.Core/Services/RfidStationConfigurationChangeRules.cs`
- Modify: `src/MineRailMonitor.Infrastructure/Configuration/ProjectConfigService.cs`
- Modify: `src/MineRailMonitor/Pages/SettingsPage.xaml`
- Modify: `src/MineRailMonitor/Pages/SettingsPage.xaml.cs`
- Test: `tests/MineRailMonitor.Core.Tests/PassageLifecycleTests.cs`
- Test: `tests/MineRailMonitor.Core.Tests/RfidStationConfigurationChangeRulesTests.cs`
- Test: `tests/MineRailMonitor.Core.Tests/SettingsPageMarkupTests.cs`
- Test: `tests/MineRailMonitor.Infrastructure.Tests/RfidStationConfigurationTests.cs`

**Interfaces:** Add nullable `RfidStationConfig.ExpectedVehicleCount` (valid range 1–14 when present). Runtime resolves it with `station.ExpectedVehicleCount ?? settings.ExpectedVehicleCount`.

- [x] Add a regression test proving 6 tags complete a station configured for 6 while a concurrent station configured for 11 remains recognizing; assert each runtime state and passage record retains its own expected count.
- [x] Run the focused test and confirm both sessions currently use the global count.
- [x] Add the optional model field and validate its range for enabled stations.
- [x] Apply the resolved count to initial runtime state and newly created recognition sessions.
- [x] Require a runtime restart when a station's override changes so saved values reach live coordinators.
- [x] Add station-row editing, validation, and config conversion; blank means use the global value.
- [x] Read, validate, and write the override through the explicit project manifest mapping.
- [x] Reject invalid enabled station overrides when loading existing project manifests.
- [x] Run focused runtime, persistence, and settings tests; run the full solution build and test suite.
