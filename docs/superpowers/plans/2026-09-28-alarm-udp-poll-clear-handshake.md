# Alarm UDP Poll/Clear Handshake Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将脱节报警外部 UDP 通信改为由基站轮询驱动的周期报警帧发送，并由外部返回清除帧后结束报警。

**Architecture:** 保留 RFID 本地轮询、接收和健康状态为独立链路；新增独立的报警协议编解码和外部清除接收链路。报警帧按站点地址发送，清除帧按站点地址回填到对应运行时状态，外部 UDP 失败只记录报警转发日志，不污染 RFID 通信状态。

**Tech Stack:** .NET Framework 4.8、C#、UDP、xUnit、现有 `YardCommunicationContext` / `RfidRuntimeCoordinator` / `UdpExternalDataInterface`。

**Spec:** 当前对话中的协议表和报警闭环需求（2026-09-28）。

## Global Constraints

- 不修改 RFID 报文协议、RFID 本地轮询健康判定和 SQLite 既有历史兼容逻辑，除非为外部清除状态提供明确的最小持久化入口。
- 报警外部 UDP 失败不得写入 Yard `LastError`，不得增加 Yard `ErrorCount`，不得停止 RFID polling。
- 报警帧使用固定结构：`B0 B0 [站点地址] 04 [命令1] [命令2] [命令3] [车厢数] AA AA`。
- 外部返回命令 1 为 `01` 的、地址匹配的完整清除帧后，结束对应报警发送并恢复界面报警状态。
- 车厢数只在识别到新的 RFID 车厢时累加，同一车厢重复轮询不得重复累加。
- 560、620、不同基站地址、不同 Passage 的状态必须隔离。
- `docs/v1.0.0-smoke-test-checklist.md` 不 add、不修改、不删除。

## Decision Gate Before Implementation

以下三项必须先由需求确认，不能由实现者猜测：

1. `PollIntervalMs=200` 的现场含义：当前实现是每次轮询一个基站后等待 200ms，因此 6 个基站同一站点的单基站周期约为 1200ms；新协议要求的是继续沿用该现有节拍，还是每个基站必须独立每 200ms 发送一次外部报警帧。
2. 外部清除帧是否确认为 10 字节固定帧，且命令 1 为 `01`；此前出现的更长清除报文是否废弃，不再参与匹配。
3. 外部清除后是否立即将 Passage 的 `ClearState` 标记为 `Cleared` 并让界面恢复正常，同时 RFID 自身的 `Clear/WaitForEmpty` 仍继续；还是外部清除直接结束整个 RFID Passage 生命周期。

## Review Focus

- 外部目标不可达时，报警发送失败不能改变 RFID `LastError` / `ErrorCount`，测试归入 Task 4。
- 同一基站重复收到同一 RFID 帧时，车厢数不能重复增加，测试归入 Task 2。
- 560 与 620 使用相同协议地址时，必须按 endpoint/yard 隔离，测试归入 Task 3。
- 错误长度、错误帧头、错误帧尾、错误模式、错误地址或非清除命令不能清除报警，测试归入 Task 3。
- 外部清除和 RFID 空槽恢复并发发生时，不能重复保存、重复清除或重新触发同一 Passage，测试归入 Task 4。

### Task 1: 固定报警协议编解码

**Files:**
- Create: `src/MineRailMonitor.Core/Protocol/AlarmStationFrame.cs`
- Create: `src/MineRailMonitor.Core/Protocol/AlarmStationFrameCodec.cs`
- Test: `tests/MineRailMonitor.Core.Tests/AlarmStationFrameCodecTests.cs`

**Interfaces:**
- Consumes: 站点地址、命令 1～4、累计车厢数。
- Produces: 固定 10 字节报警/清除帧，以及严格校验后的解析结果。

- [ ] 先写 RED 测试：验证报警帧头尾、地址、模式 `04`、命令 1=`03`、车厢数位置和清除帧命令 1=`01`。
- [ ] 增加 RED 测试：错误长度、头尾、模式、地址范围和命令值不能被接受。
- [ ] 实现最小 codec；禁止复用旧的 40 字节原始 RFID payload 转发逻辑。
- [ ] 运行 `dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~AlarmStationFrameCodecTests`，确认 GREEN。

### Task 2: 车厢累计与轮询发送数据生成

**Files:**
- Modify: `src/MineRailMonitor.Core/Models/StationRuntimeState.cs`
- Modify: `src/MineRailMonitor.Core/Services/RfidRuntimeCoordinator.cs`
- Modify: `src/MineRailMonitor.Core/Models/AlarmForwardRequest.cs` 或以新的报警发送模型替代旧的一次性请求模型
- Test: `tests/MineRailMonitor.Core.Tests/AlarmForwardingTests.cs`
- Test: `tests/MineRailMonitor.Core.Tests/AlarmForwardingDispatchTests.cs`

**Interfaces:**
- Consumes: `RfidStationFrame` 的新车厢识别结果和当前站点状态。
- Produces: 按站点地址生成的当前报警帧，携带累计车厢数；同一 Passage 只建立一个报警发送状态。

- [ ] 先写 RED 测试：新 RFID 车厢使累计数 `+1`，重复 RFID 不增加，新的 Passage 重新开始计数。
- [ ] 先写 RED 测试：报警帧包含当前计数；更新计数后下一次轮询帧使用新值。
- [ ] 实现最小状态维护，不把累计数混入 `ErrorCount`、通信状态或旧的原始 payload 字段。
- [ ] 运行 Core 相关测试，确认计数和 Passage 隔离通过。

### Task 3: 以基站轮询驱动外部 UDP，并接收外部清除

**Files:**
- Modify: `src/MineRailMonitor.Core/Interfaces/IExternalDataInterface.cs`，或新增专用双向接口以避免破坏旧的一次性发送调用方
- Modify: `src/MineRailMonitor.Infrastructure/ExternalData/UdpExternalDataInterface.cs`
- Modify: `src/MineRailMonitor.Core/Communication/RfidStationPoller.cs` 或 `src/MineRailMonitor.Core/Services/YardCommunicationContext.cs`
- Modify: `src/MineRailMonitor.Core/Services/YardCommunicationManager.cs`
- Test: `tests/MineRailMonitor.Infrastructure.Tests/UdpExternalDataInterfaceTests.cs`
- Test: `tests/MineRailMonitor.Core.Tests/YardCommunicationManagerTests.cs`

**Interfaces:**
- Consumes: 轮询成功事件、Task 1 的报警帧、外部 UDP 接收 datagram。
- Produces: 每个站点轮询节拍的一次非阻塞外部发送；地址匹配的清除事件。

- [ ] 先写 RED 测试：轮询成功后生成对应站点报警帧；外部发送不能阻塞或失败 RFID poller。
- [ ] 先写 RED 测试：收到完整清除帧后只通知匹配的站点/yard；非匹配帧被忽略。
- [ ] 实现 UDP 接收路径。优先复用同一外部 UDP socket 的响应路径；若现场要求固定本地接收端口，再扩展配置模型前先确认端口来源。
- [ ] 发送任务必须与 RFID poller 解耦，外部发送异常只能触发 `AlarmForwardFailed`/日志事件。
- [ ] 按 Decision Gate 选定的 200ms 语义实现，避免把现有“站点间隔”误当成“单基站周期”。
- [ ] 运行 Core 和 Infrastructure 相关测试，确认原有 `UdpExternalDataInterfaceTests` 仍通过。

### Task 4: 外部清除闭环与持久化/界面状态

**Files:**
- Modify: `src/MineRailMonitor.Core/Services/RfidRuntimeCoordinator.cs`
- Modify: `src/MineRailMonitor.Core/Interfaces/IPassageRecordStore.cs`（仅在外部清除必须持久化且现有接口不足时）
- Modify: `src/MineRailMonitor.Infrastructure/Persistence/SqlitePassageRecordStore.cs`（仅对应最小实现）
- Modify: `src/MineRailMonitor/MainWindow.xaml.cs`
- Test: `tests/MineRailMonitor.Core.Tests/AlarmAcknowledgementTests.cs`
- Test: `tests/MineRailMonitor.Infrastructure.Tests/SqlitePassageRecordStoreTests.cs`

**Interfaces:**
- Consumes: Task 3 的匹配清除事件。
- Produces: 报警停止、历史状态按已确认语义更新、界面恢复正常且 RFID polling 继续。

- [ ] 先写 RED 测试：报警发生后未收到清除帧时保持报警并持续按轮询发送。
- [ ] 先写 RED 测试：收到命令 1=`01` 的匹配清除帧后停止发送、显示正常、记录状态正确。
- [ ] 先写 RED 测试：外部发送失败时本地报警仍保留，RFID `LastError` / `ErrorCount` 不变。
- [ ] 实现清除幂等性：重复清除帧不得重复保存、重复触发状态事件或创建新 Passage。
- [ ] 明确处理外部清除与本地 `Clear/WaitForEmpty` 的并发关系，不能通过删除原有 RFID 生命周期逻辑来掩盖。
- [ ] 运行 Core/Infrastructure 相关测试，确认旧人工确认测试按新需求调整，而非简单删除覆盖范围。

### Task 5: 集成验证

**Files:**
- Modify only tests/docs needed for verified behavior; no UI redesign.

- [ ] 使用 Simulator 验证 560、620 独立报警帧、累计车厢数、200ms 轮询节拍、外部清除帧和单 Passage 发送行为。
- [ ] 验证错误帧不会清除报警，目标不可达不会影响 RFID 健康状态和 polling。
- [ ] 运行 Core 全量、Infrastructure 全量、solution build，并记录 passed/failed/skipped、warnings/errors。
- [ ] `git diff --check`，确认 `docs/v1.0.0-smoke-test-checklist.md` 仍未跟踪且未被修改。
- [ ] 不修改 v1.0.0 tag/Release，不生成 installer，不 push，除非后续另行授权。
