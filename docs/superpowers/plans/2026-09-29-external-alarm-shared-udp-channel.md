# External Alarm Shared UDP Channel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 让每个站场的 External Alarm UDP 在同一个固定本机端口上完成状态发送和清除命令接收。

**Architecture:** 新增一个拥有单一 `UdpClient` 的 `ExternalAlarmUdpChannel`，在构造时绑定本机 `IPEndPoint`，发送状态帧和接收清除帧共用该 Socket。保留现有 `ExternalAlarmUdpSender` 与 `ExternalAlarmUdpReceiver` 作为兼容组件，但组合根在正式运行链路中改用共享通道；560/620 仍按站场独立配置，启用的本机 `IP:Port` 不允许重复。

**Tech Stack:** C# / .NET Framework 4.8 / UDP `UdpClient` / xUnit / WPF 组合根。

**Spec:** 当前会话已确认的共享端口设计：本机 `192.168.16.118:58112`，目标服务器 `192.168.16.100:58110`；同一个本机 Socket 负责发送和接收。

## Global Constraints

- 不修改 RFID 40 字节协议、`RfidUdpTransport`、轮询、Passage、报警状态机和 Clear/WaitForEmpty 业务语义。
- External Alarm 12 字节协议字段、CRC 算法和字节顺序保持不变。
- 发送失败和接收异常只记录 External Alarm 独立日志，不污染 RFID 健康状态。
- 不删除旧 `ExternalAlarmUdpSender`、`ExternalAlarmUdpReceiver`，保留历史兼容 API。
- 不修改版本号、tag、Release；不处理现有未跟踪计划文件。

## Review Focus

- 发送 Socket 已绑定时，接收清除帧必须仍能到达同一通道。
- 同一站场发送源端口与接收监听端口相同时，不得出现端口重复绑定。
- 560/620 使用相同本机端口时必须在配置应用阶段拒绝，不能静默串站。
- 通道启动、停止、替换和释放必须不影响 RFID Socket 生命周期。
- 外部 UDP 发送异常必须仍只触发 External Alarm 日志事件。

---

### Task 1: Add shared-channel tests

**Files:**
- Create: `tests/MineRailMonitor.Core.Tests/ExternalAlarmUdpChannelTests.cs`
- Test: `tests/MineRailMonitor.Infrastructure.Tests/YardAlarmForwardConfigurationTests.cs`

**Interfaces:**
- `ExternalAlarmUdpChannel(IPEndPoint localEndpoint, IPEndPoint targetEndpoint, ExternalAlarmFrameCodec? codec = null)`
- `Task SendAsync(byte[] payload, CancellationToken cancellationToken)`
- `Task StartAsync(CancellationToken cancellationToken = default)` / `Task StopAsync()`
- Receive/reject/error events保持现有 `ExternalAlarmUdpReceiver` 的事件形状。

- [x] **Step 1: Write failing tests**
  - 使用 loopback 本机端口验证：通道发送的数据从配置的固定本机端口发出。
  - 向同一本机端口发送合法 12 字节清除帧，验证 `ClearReceived` 被触发。
  - 验证发送和接收同时使用同一端口，不发生第二次 bind。
  - 为两个启用站场配置相同 `IP:Port`，验证配置校验拒绝重复端点。

- [x] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test tests\MineRailMonitor.Core.Tests\MineRailMonitor.Core.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ExternalAlarmUdpChannelTests"
```

Expected: FAIL because `ExternalAlarmUdpChannel` does not exist.

### Task 2: Implement the shared UDP channel

**Files:**
- Create: `src/MineRailMonitor.Core/ExternalAlarm/ExternalAlarmUdpChannel.cs`
- Modify: `src/MineRailMonitor.Core/Services/YardCommunicationContext.cs`
- Modify: `src/MineRailMonitor.Core/Services/YardCommunicationManager.cs`

**Interfaces:**
- The channel implements `IExternalAlarmStatusSender` and owns exactly one bound `UdpClient`.
- `SendAsync` sends to the configured target through the bound client.
- The receive loop validates frames through `ExternalAlarmFrameCodec`, raises raw-frame logging events, and forwards valid clear commands into the existing context clear entry.

- [x] **Step 1: Implement channel ownership and lifecycle**
  - Bind once to the local endpoint in `StartAsync`/constructor path.
  - Reuse the same client for `SendAsync` and `ReceiveAsync`.
  - Make stop/dispose idempotent and prevent sends after disposal.

- [x] **Step 2: Connect the channel to context and manager**
  - Add the channel as the context’s External Alarm transport while preserving existing constructor compatibility where practical.
  - Manager creates the channel from `ListenIp + ExternalAlarmListenPort` and the configured alarm target, then passes the same instance into `ExternalAlarmStatusCoordinator`.
  - Keep RFID transport construction untouched.

- [x] **Step 3: Run Task 1 tests and verify GREEN**

### Task 3: Expose the fixed external local port in configuration UI

**Files:**
- Modify: `src/MineRailMonitor/Pages/YardCommunicationConfigDialog.xaml`
- Modify: `src/MineRailMonitor/Pages/YardCommunicationConfigDialog.xaml.cs`
- Modify: `tests/MineRailMonitor.Core.Tests/SettingsPageMarkupTests.cs`

- [x] **Step 1: Add a failing markup assertion** for an “外部接口监听端口” field bound to `ExternalAlarmListenPort`.
- [x] **Step 2: Verify RED** with the Settings markup test.
- [x] **Step 3: Add the field** without changing the existing RFID listener or alarm target fields; keep the same dark input style and disable editing only under existing view/admin rules.
- [x] **Step 4: Verify GREEN** and check 560/620 values remain independent.

### Task 4: Validate integration and regression safety

**Files:**
- No additional production files expected.

- [x] **Step 1: Run `git diff --check`.**
- [x] **Step 2: Run Release solution build; expect 0 warnings / 0 errors.**
- [x] **Step 3: Run Core and Infrastructure tests.**
- [x] **Step 4: Confirm external log view reports TX/RX through the shared channel and the existing RFID tests remain unchanged.**
- [x] **Step 5: Confirm working tree still leaves the pre-existing smoke/checklist/plan files untouched and do not commit or push.**
