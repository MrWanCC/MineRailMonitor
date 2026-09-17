# MineRailMonitor.Simulator 多虚拟基站 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改动上位机生产 Protocol、Runtime、Passage 或地图代码的前提下，让一个 Simulator 窗口管理任意数量的独立虚拟 RFID 分站，并支持默认 6 站、逐站配置、持久化、场景播放和并发联调。

**Architecture:** 新增 `SimulatorStationContext`，每个 Context 持有一个站点配置、14 个槽位、独立场景播放状态、计数/日志和一个 UDP listener。`MainWindow` 只维护 `ObservableCollection<SimulatorStationContext>` 与 `SelectedStation`，继续复用现有详情控件；`SimulatorStationPersistence` 负责 Simulator 私有 JSON 配置文件，默认 6 站只通过预设工厂创建。

**Tech Stack:** C# / WPF / .NET Framework 4.8 / `ObservableCollection` / `INotifyPropertyChanged` / `System.Text.Json` / xUnit / loopback UDP。

**Spec:** `docs/superpowers/specs/2026-09-13-multi-station-simulator-design.md`

## Global Constraints

- 只修改 `src/MineRailMonitor.Simulator` 及 Simulator 相关测试。
- 不修改 `src/MineRailMonitor.Core` 中正式 Protocol、Runtime、Passage、Poller 或数据绑定逻辑。
- 继续使用现有 40 Byte 帧、CRC、Read、Clear、14 个槽位、逐张扫描和场景播放协议实现。
- 每个虚拟站必须绑定自己的 UDP Endpoint；多个站不能合并为一个监听器。
- 默认监听 `127.0.0.1:62001`、`62003`~`62007`；`62002` 保留给上位机。
- ListenIp、ListenPort、ProtocolAddress 必须可编辑；默认值只是预设，不作为固定 6 站限制。
- 应用配置只重绑定当前站；端口冲突显示“端口已被占用”，当前站停止，其他站继续运行。
- 配置保存到 Simulator 私有文件，下次启动恢复最近一次站点配置。
- 不提交、不推送。

---

### Task 1: 写多站 Context 的失败测试

**Files:**
- Create: `tests/MineRailMonitor.Core.Tests/SimulatorStationContextTests.cs`
- Read-only reference: `src/MineRailMonitor.Simulator/Communication/SimulatorUdpResponder.cs`
- Read-only reference: `src/MineRailMonitor.Simulator/Protocol/RfidSimulatorResponder.cs`

**Interfaces:**
- The tests will consume the new `SimulatorStationConfig` and `SimulatorStationContext` APIs defined below.
- Later tasks must preserve these test-facing members:

```csharp
public sealed class SimulatorStationConfig
{
    public string StationName { get; set; }
    public string ListenIp { get; set; }
    public int ListenPort { get; set; }
    public byte ProtocolAddress { get; set; }
    public bool Enabled { get; set; }
    public ushort EmptySlotValue { get; set; }
    public byte CrcHigh { get; set; }
    public byte CrcLow { get; set; }
}

public sealed class SimulatorStationContext : IDisposable
{
    public SimulatorStationContext(SimulatorStationConfig config);
    public SimulatorStationConfig Config { get; }
    public bool IsRunning { get; }
    public string StatusText { get; }
    public string ErrorMessage { get; }
    public ScenarioPlaybackState Playback { get; }
    public int RequestCount { get; }
    public int ResponseCount { get; }
    public int ClearCount { get; }
    public int ErrorCount { get; }
    public Task StartAsync(CancellationToken cancellationToken = default);
    public void Stop();
    public void ClearSlots();
    public void ResetScenario();
    public void LoadScenario(string name, IEnumerable<ushort> sequence);
    public bool StepScenario();
    public bool StartScenario(TimeSpan interval);
    public void PauseScenario();
    public bool TickScenario(DateTimeOffset now);
    public bool TryApplyConfiguration(SimulatorStationConfig updated, out string error);
    public void SetSlots(IReadOnlyList<ushort> slots);
    public IReadOnlyList<ushort> SnapshotSlots();
}
```

- [ ] **Step 1: Add tests for six independent listeners**

Create six contexts using dynamically allocated loopback ports. Start all six, send one valid Read frame to every endpoint, and assert each response has the target station’s ProtocolAddress and 40-byte length. Use `try/finally` to call `Dispose` for every context.

```csharp
[Fact]
public async Task Six_contexts_bind_and_respond_independently()
{
    var contexts = Enumerable.Range(1, 6)
        .Select(address => CreateContext((byte)address, GetUnusedUdpPort()))
        .ToArray();

    try
    {
        foreach (var context in contexts) await context.StartAsync();
        foreach (var context in contexts)
        {
            var response = await SendReadAsync(context.Config.ListenPort, context.Config.ProtocolAddress);
            Assert.Equal(40, response.Length);
            Assert.Equal(context.Config.ProtocolAddress, response[2]);
        }
        Assert.All(contexts, context => Assert.True(context.IsRunning));
    }
    finally
    {
        foreach (var context in contexts) context.Dispose();
    }
}
```

- [ ] **Step 2: Add tests for station state isolation**

Set different slots on two contexts, send Read to both, then send Clear to only the second. Assert the first station still returns its RFID and the second returns empty slots. Also stop one context and assert the other still responds.

- [ ] **Step 3: Add tests for same-address endpoint isolation**

Create two contexts with `ProtocolAddress = 0x01` but different dynamic ports and different slots. Send the same Read request to both ports and assert each response reflects only its own slot state.

- [ ] **Step 4: Add tests for per-station scenario players**

Load Normal11 into one context and a short sequence into another. Step, pause, and reset only one context. Assert the other context’s `Playback.CurrentIndex`, `Playback.Slots`, and `IsPlaying` remain unchanged; assert duplicate sequence values are preserved.

- [ ] **Step 5: Add tests for one-station startup failure**

Hold a UDP socket on a dynamic port, start a context configured to that port, and assert it does not throw, `IsRunning` is false, `StatusText` indicates stopped/error, and `ErrorMessage` is exactly or contains `端口已被占用`. Start a second context on a free port and assert it responds normally.

- [ ] **Step 6: Run the new tests to verify the expected RED state**

Run:

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~SimulatorStationContextTests --no-restore
```

Expected: compilation/test failure because the new Context and configuration types do not exist yet. Fix only test setup errors until the failure is caused by the missing production API.

### Task 2: Implement per-station configuration and Context lifecycle

**Files:**
- Create: `src/MineRailMonitor.Simulator/Models/SimulatorStationConfig.cs`
- Create: `src/MineRailMonitor.Simulator/Models/SimulatorStationContext.cs`
- Modify: `src/MineRailMonitor.Simulator/Communication/SimulatorUdpResponder.cs` only if the Context needs a non-breaking lifecycle hook
- Test: `tests/MineRailMonitor.Core.Tests/SimulatorStationContextTests.cs`

**Interfaces:**
- `SimulatorStationConfig` is a mutable, serializable configuration object with the properties in Task 1.
- `SimulatorStationContext` implements `INotifyPropertyChanged` and `IDisposable` so WPF lists can refresh without a separate view model.
- `StartAsync` binds one `SimulatorUdpResponder`, creates one `RfidSimulatorResponder` containing one `SimulatorStation`, attaches packet telemetry, launches the receive loop, and returns after binding. Receive-loop failures are stored in `ErrorMessage` and do not escape into other contexts.
- `TryApplyConfiguration` validates IP, port, and frame values before binding. It remembers whether the station was running, always stops the old listener first, applies the new config, and only restarts when the station had been running. A failed bind leaves this context stopped.

- [ ] **Step 1: Implement the serializable configuration object**

Give `SimulatorStationConfig` safe defaults (`127.0.0.1`, port `0`, address `0`, enabled `true`, empty slot `0`, CRC bytes `0`) and a `Clone()` method returning a new object with all properties copied. Do not put the six production preset endpoints into this model.

- [ ] **Step 2: Implement Context state and snapshot helpers**

Construct a one-station `SimulatorStation` from `Config.ProtocolAddress`, allocate exactly 14 slots, create a `ScenarioPlaybackState`, and expose read-only counts/status. `SetSlots` must require exactly 14 values and clone the array. `SnapshotSlots` must return a clone. Protect slot and lifecycle state with a private synchronization object because UDP receive and WPF scenario actions can overlap.

- [ ] **Step 3: Implement StartAsync and the isolated receive loop**

Bind only `Config.ListenIp`/`Config.ListenPort`. Create `RfidSimulatorResponder(new[] { station }, Config.EmptySlotValue)` and one `SimulatorUdpResponder`. On packet handled, increment only this Context’s counters and update this Context’s last-request telemetry. On bind failure, catch `SocketException`; map `SocketError.AddressAlreadyInUse` to `端口已被占用`, set stopped/error state, and dispose only this Context’s partially-created resources.

- [ ] **Step 4: Implement Stop and Dispose**

Make both operations idempotent. Cancel the Context CTS, detach packet handlers, dispose the listener and CTS, stop playback, and set this Context to stopped. Do not access or mutate any other Context.

- [ ] **Step 5: Implement ClearSlots and scenario delegation**

`ClearSlots` resets only this station’s 14 values and pushes the snapshot to its responder when running. `LoadScenario`, `StepScenario`, `StartScenario`, `PauseScenario`, `ResetScenario`, and `TickScenario` operate only on this Context. `TickScenario` advances at most one RFID when `now` reaches the Context’s own next-step deadline; ordinary UDP Read does not call it.

- [ ] **Step 6: Run the focused tests to verify GREEN**

Run the same `dotnet test` command from Task 1. Expected: all Context isolation, same-address, scenario, stop/restart, and occupied-port tests pass.

### Task 3: Add Simulator-only configuration persistence and preset factories

**Files:**
- Create: `src/MineRailMonitor.Simulator/Models/SimulatorStationPersistence.cs`
- Create: `src/MineRailMonitor.Simulator/Models/SimulatorStationPresets.cs`
- Create: `tests/MineRailMonitor.Core.Tests/SimulatorStationPersistenceTests.cs`
- Test: `tests/MineRailMonitor.Core.Tests/SimulatorStationContextTests.cs`

**Interfaces:**

```csharp
public sealed class SimulatorStationPersistence
{
    public static IReadOnlyList<SimulatorStationConfig> Load(string path);
    public static void Save(string path, IEnumerable<SimulatorStationConfig> configs);
}

public static class SimulatorStationPresets
{
    public static IReadOnlyList<SimulatorStationConfig> CreateDefaultSix();
    public static void ApplyAllNormal(IEnumerable<SimulatorStationContext> stations);
    public static void ApplyOneNormalFiveOffline(IEnumerable<SimulatorStationContext> stations);
    public static void ApplySameAddressIsolation(IReadOnlyList<SimulatorStationContext> stations);
}
```

- [ ] **Step 1: Write persistence tests**

Use a unique directory under `Path.GetTempPath()` and a `try/finally` cleanup. Save two custom configs, load them, and assert station names, custom ports, protocol addresses, enabled states, empty-slot and CRC values survive. Test a missing file returns an empty collection and invalid JSON does not crash the Simulator loader; it returns an empty collection for the caller to replace with defaults.

- [ ] **Step 2: Run persistence tests to verify RED**

Run:

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~SimulatorStationPersistenceTests --no-restore
```

Expected: failure because the persistence type is not implemented.

- [ ] **Step 3: Implement atomic Simulator-only JSON persistence**

Serialize a list of `SimulatorStationConfig` with `System.Text.Json`. Save to `<path>.tmp`, flush/close it, then replace the target with `File.Replace` when possible or `File.Move` after deleting only the known target file. Create the parent directory. Never read or write `Projects/Default` or the upper application configuration.

- [ ] **Step 4: Implement default and test preset factories**

`CreateDefaultSix()` returns editable config objects for `RFID-01`/`62001`/`01`, `RFID-02`/`62003`/`02`, through `RFID-06`/`62007`/`06`. The factory is only called when the persistence file is absent or by an explicit “一键创建6基站” action. `ApplySameAddressIsolation` changes only the first two Context configs to ports 62001/62003 and address 01; it does not merge listeners.

- [ ] **Step 5: Run persistence, preset, and Context tests to verify GREEN**

Run both focused filters. Expected: all pass, including custom port persistence and same-address preset behavior.

### Task 4: Write failing MainWindow multi-station markup and delegation tests

**Files:**
- Modify: `tests/MineRailMonitor.Core.Tests/SimulatorMarkupTests.cs`
- Create: `tests/MineRailMonitor.Core.Tests/SimulatorMultiStationMarkupTests.cs`
- Read-only reference: `src/MineRailMonitor.Simulator/MainWindow.xaml`
- Read-only reference: `src/MineRailMonitor.Simulator/MainWindow.xaml.cs`

**Interfaces:**
- Tests will require the XAML names `VirtualStationsListBox`, `MultiStationOverviewGrid`, `CreateSixStationsButton`, `StartAllStationsButton`, `StopAllStationsButton`, `ClearAllStationsButton`, `ResetAllScenariosButton`, `AddStationButton`, and `RemoveStationButton`.
- Tests will require code-behind to contain a collection of `SimulatorStationContext`, selected-context switching, persistence load/save, and no `_stationInputs` fixed array or single `_responder` ownership.

- [ ] **Step 1: Add markup assertions for the new operator surface**

Assert the XAML contains the virtual station list, multi-station overview, all five global actions, editable endpoint fields, same existing RFID slot/scenario/log controls, and a horizontal/scrollable overview region that can display dynamic rows.

- [ ] **Step 2: Add code-behind assertions for dynamic delegation**

Assert the code contains `ObservableCollection<SimulatorStationContext>`, `SelectedStation`, `SimulatorStationPersistence.Load`, `SimulatorStationPersistence.Save`, and handlers for list selection and global controls. Assert the old fixed `_stationInputs` field and single `_responder` field are absent.

- [ ] **Step 3: Run markup tests to verify RED**

Run:

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~SimulatorMultiStationMarkupTests --no-restore
```

Expected: failure because the XAML and code-behind have not been converted.

### Task 5: Implement MainWindow station collection, persistence, selection, and dynamic station editing

**Files:**
- Modify: `src/MineRailMonitor.Simulator/MainWindow.xaml`
- Modify: `src/MineRailMonitor.Simulator/MainWindow.xaml.cs`
- Test: `tests/MineRailMonitor.Core.Tests/SimulatorMarkupTests.cs`
- Test: `tests/MineRailMonitor.Core.Tests/SimulatorMultiStationMarkupTests.cs`

**Interfaces:**
- `MainWindow` owns `ObservableCollection<SimulatorStationContext> Stations` and `SimulatorStationContext? SelectedStation`.
- Details continue to use the existing controls (`LocalIpTextBox`, `LocalPortTextBox`, `AddressTextBox`, `RfidInputsPanel`, scenario buttons, response controls, and log tabs), but every handler reads/writes `SelectedStation`.
- The default persistence path is under `Environment.SpecialFolder.LocalApplicationData`, in a Simulator-specific directory such as `MineRailMonitor.Simulator/stations.json`; tests use the static persistence class with an injected temporary path and do not start the WPF window.

- [ ] **Step 1: Add the left virtual-station panel to XAML**

Add a compact `Border`/`ListBox` before the existing detail columns. Each row shows station name, running dot, endpoint port, and two-digit address. Add “一键创建6基站”, “添加基站”, and “删除当前” controls. Keep the existing right-side detail controls instead of creating six copies.

- [ ] **Step 2: Add XAML global operation and overview panels**

Add named buttons for start/stop/clear/reset all and a `DataGrid` named `MultiStationOverviewGrid` with columns Station, Endpoint, Addr, 状态, 场景, 进度, 有效RFID, 请求, 响应, Clear, 异常. Bind its `ItemsSource` to `Stations`; handle double-click by selecting the clicked Context. Use a scrollable container or DataGrid scrolling so adding stations never narrows existing details into unusable controls.

- [ ] **Step 3: Replace fixed station fields in MainWindow code-behind**

Remove the fixed `_stationInputs[6]`, `_stationConfigured[6]`, `_stationCatalog` startup path, single `_responder`, single `_simulatorResponder`, and single-station counters. Add the collection, selected context, a UI refresh timer, and a persistence path. Do not remove or modify the protocol classes used by acceptance tests.

- [ ] **Step 4: Load persisted stations or create the default preset**

During `OnLoaded`, call `SimulatorStationPersistence.Load`. If it returns no valid configs, create `SimulatorStationPresets.CreateDefaultSix`, build Contexts, select the first one, and save the config file. If configs exist, recreate those exact editable values without automatically starting them.

- [ ] **Step 5: Implement selection and detail hydration**

Before switching selection, save the current detail controls into the old Context. Then load the new Context’s IP, port, address, empty-slot value, CRC bytes, 14 slot values, scenario status, counters, and log into the existing controls. Selection changes must not call Stop, Reset, Clear, or change another Context.

- [ ] **Step 6: Implement add/remove station actions**

Adding creates a new editable Context with the first unused station name, first unused port above the default range, and first unused ProtocolAddress; it does not start the listener. Removing stops/disposes only the selected Context, removes it from the collection, saves the remaining configs, and selects a neighbor.

- [ ] **Step 7: Implement Apply Configuration semantics**

Parse the current IP, port, protocol address, empty-slot, CRC, and slot values. Call `SelectedStation.TryApplyConfiguration`. Persist the updated config even when the selected Context remains stopped due to an invalid or occupied port. Show `端口已被占用` in the selected station error text and overview; leave all other Contexts untouched. If the selected station was running before Apply, restart only it on the new Endpoint.

- [ ] **Step 8: Run markup tests and build Simulator**

Run the focused markup tests and:

```powershell
dotnet build src/MineRailMonitor.Simulator/MineRailMonitor.Simulator.csproj --no-restore
```

Expected: markup assertions and Simulator build pass; no `MineRailMonitor.Core` source files are modified by this task.

### Task 6: Delegate all existing single-station controls to the selected Context

**Files:**
- Modify: `src/MineRailMonitor.Simulator/MainWindow.xaml.cs`
- Modify: `src/MineRailMonitor.Simulator/MainWindow.xaml` only where a control must expose the selected-station state
- Test: `tests/MineRailMonitor.Core.Tests/SimulatorStationContextTests.cs`

**Interfaces:**
- Existing scenario names and sequences remain unchanged, plus the existing “脱节报警10节” scenario if present in the current Simulator code.
- `ScenarioPlaybackState` remains the source of order, duplicate preservation, current RFID, next RFID, and slot snapshot for each Context.

- [ ] **Step 1: Route preset buttons to SelectedStation**

Keep the current seven scenario sequences and call `SelectedStation.LoadScenario` only. Loading a scenario clears that station’s slots and playback index; it must not alter any other Context. The “一键创建6基站” action creates six contexts, while scenario preset buttons operate on the selected station.

- [ ] **Step 2: Route manual slot, scan, remove, and Clear actions**

Manual edit operations update only the selected Context’s slots and responder. A Clear operation calls `SelectedStation.ClearSlots`; no shared responder or shared slot array remains. Preserve duplicate input behavior by leaving duplicate scenario values untouched.

- [ ] **Step 3: Route start/stop and response telemetry**

Start/stop buttons act on the selected Context. “全部启动” starts all Enabled Contexts, “全部停止” stops all, and one Context’s start failure is shown only for that Context. Details show the selected Context’s counters/logs; the overview reads every Context’s counters.

- [ ] **Step 4: Tick every Context’s ScenarioPlayer**

Replace the single `_scenarioPlaybackTimer` handler with one UI timer that calls `TickScenario(DateTimeOffset.UtcNow)` for every running playback Context and refreshes the selected details plus the overview. A paused Context does not advance; a reset Context returns to 0/sequence length and empty slots. The timer never changes UDP protocol or Runtime behavior.

- [ ] **Step 5: Implement all global controls and four presets**

Wire start-all, stop-all, clear-all, reset-all, 6-station concurrent, all-normal, one-normal-five-offline, and same-address isolation actions. The “1 normal 5 offline” preset must leave only RFID-01 running and stop the other Contexts. The same-address preset must retain two independent ports.

- [ ] **Step 6: Refresh station list and overview without stale selection**

When a Context property changes, refresh its row through `INotifyPropertyChanged`. Keep `SelectedStation` valid after add/remove and do not display the previous station’s counters or logs after selection changes.

- [ ] **Step 7: Run focused Context tests and manual static checks**

Run:

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~SimulatorStationContextTests --no-restore
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~SimulatorMarkupTests --no-restore
```

Verify with `rg` that only `src/MineRailMonitor.Simulator` and Simulator test files changed for this feature, and that no `src/MineRailMonitor.Core` file was modified by the multi-station implementation.

### Task 7: Add the complete multi-station regression suite

**Files:**
- Modify: `tests/MineRailMonitor.Core.Tests/SimulatorStationContextTests.cs`
- Create: `tests/MineRailMonitor.Core.Tests/SimulatorMultiStationRegressionTests.cs`
- Modify: `tests/MineRailMonitor.Core.Tests/SimulatorScenarioPlaybackTests.cs` only if a per-Context API assertion belongs there

**Interfaces:**
- Tests use dynamic loopback ports and `SimulatorStationContext`; no test binds real production IPs or edits `Projects/Default`.
- Every network test has `try/finally` disposal and a bounded receive timeout.

- [ ] **Step 1: Add concurrent six-station Read/response regression**

Start six contexts, send multiple Reads to all six, and assert every Context’s RequestCount/ResponseCount advances independently. Change RFID-01 slots and assert RFID-02 response bytes remain unchanged.

- [ ] **Step 2: Add per-station Clear and stop regression**

Send Clear to RFID-02 only and assert RFID-01 slots remain. Stop RFID-03 and assert the other five continue to answer Reads. Restart RFID-03 and assert its port is usable again.

- [ ] **Step 3: Add exception containment regression**

Force one context to fail binding on an occupied port, then start/use the remaining contexts. Assert the failed Context has an error and is stopped while all other listeners stay running.

- [ ] **Step 4: Add same-address regression**

Run two contexts using address 01 on different ports with different slot values and assert responses do not cross. This proves routing is by each independent endpoint plus that Context’s own station state, not a shared address dictionary.

- [ ] **Step 5: Add player and reset regression**

Run different scenario sequences and intervals on two contexts. Assert one can pause/reset while the other continues; assert all-stop then restart works and scenario state is preserved or reset only according to the invoked operation.

- [ ] **Step 6: Run the complete Core test project**

Run:

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --no-restore
```

Expected: all existing tests plus the new multi-station tests pass. Investigate only failures caused by this feature; do not change production Runtime or protocol logic.

### Task 8: Verify builds, acceptance, diff boundaries, and operator workflow

**Files:**
- No new production files; update only test/markup assertions if a verified name is wrong.
- Read-only: `src/MineRailMonitor.Core/**`, `Projects/Default/**`, `src/MineRailMonitor/Pages/**`

- [ ] **Step 1: Run Core Tests**

Run the repository’s established Core test command and record the exact passed/failed count.

- [ ] **Step 2: Run Infrastructure Tests**

Run the repository’s established Infrastructure test command and record the exact passed/failed count.

- [ ] **Step 3: Run Release Build**

Build the formal upper application Release target without changing its source. If a running process holds a binary, report the lock instead of killing the user process.

- [ ] **Step 4: Run Simulator Build**

Build `MineRailMonitor.Simulator` in Release and confirm the generated executable includes the new XAML and Context code.

- [ ] **Step 5: Run RFID Acceptance 8/8**

Run the existing acceptance script unchanged against its isolated loopback/test artifacts. Do not redirect it to production DB/config or change its Runtime assertions.

- [ ] **Step 6: Run `git diff --check` and `git status`**

Confirm no whitespace errors, no commit, no push, and no changes under upper-app production logic. Report pre-existing dirty files separately from Simulator/test files changed by this work.

- [ ] **Step 7: Report operator workflow**

Document: launch Simulator, click “一键创建6基站”, edit/apply any ListenIp/ListenPort/ProtocolAddress, use “全部启动” or the “6基站并发” preset, select a station for scenario playback, use “1正常5离线” and “同Address隔离”, and use “全部停止” before closing. Include the Simulator configuration JSON path and all test results.

## Verification Checklist

- [ ] One Context owns exactly one UDP listener and one station responder.
- [ ] Six default contexts use 62001 and 62003~62007; 62002 is never selected by the preset.
- [ ] ListenIp, ListenPort, and ProtocolAddress are editable and persisted.
- [ ] Apply on a running station releases/rebinds only that station.
- [ ] Occupied port is shown as `端口已被占用`; failed station stays stopped.
- [ ] Same ProtocolAddress on different ports is isolated.
- [ ] Scene playback, slots, Clear, counters, logs, Pause, and Reset are independent.
- [ ] Dynamic add/remove does not narrow or duplicate the detail UI.
- [ ] `src/MineRailMonitor.Core` production code is unchanged by this feature.
- [ ] Core Tests, Infrastructure Tests, Release Build, Simulator Build, RFID Acceptance 8/8, and `git diff --check` have recorded results.
- [ ] No commit or push was performed.
