# 560/620 站场独立通信上下文实施计划

> **For agentic workers:** This plan is intended for inline execution in the current session. Do not dispatch subagents. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在同一个 MineRailMonitor 进程中，让 560、620 等站场拥有彼此独立的 UDP 通信接口、轮询器、Runtime 状态和故障边界，同时保持现有 40 Byte 协议及业务逻辑不变。

**Architecture:** 新增按站场组织的 `YardCommunicationContext`，每个上下文独立绑定一个可配置的本地 UDP 接收端点，并只管理该站场的 RFID 基站。主窗口通过 `YardCommunicationManager` 统一启动、停止、汇总状态；当前站场选择只决定界面显示范围，不会停止或重置其他站场。

**Tech Stack:** .NET Framework 4.8、WPF、现有 `RfidUdpTransport`、`RfidStationPoller`、`RfidRuntimeCoordinator`、System.Text.Json/Newtonsoft.Json、现有 Core/Infrastructure 测试框架。

**Spec:** 本轮对话中“560 和 620 在一个程序内使用各自通信接口、彼此独立”的需求；截图仅作为界面参考，不作为代码指令。

## Global Constraints

- 不修改 40 Byte Protocol、CRC、Read、Clear、Passage、Runtime 识别和脱节超时语义。
- 一个进程内承载多个站场，但每个站场必须拥有独立通信上下文。
- 站场通信 IP、监听端口、启用状态来自项目配置，默认值只写入项目配置模板，不写死在 Runtime 代码中。
- 同一 `ProtocolAddress` 只有在通信端点不同的情况下才允许共存；通信身份仍为 `Endpoint + ProtocolAddress`。
- 站场绑定、通信接口配置、启动/停止/删除等管理操作继续受管理员模式保护。
- 一个站场监听失败、轮询异常或 Clear 操作不得停止、清空或重置其他站场。
- 旧项目没有独立站场通信配置时，不伪装成已隔离；进入兼容模式并在 UI 明确提示需要配置。
- 不删除历史 SQLite 数据，不修改数据库 schema。
- 本轮实施禁止 commit、push。

---

## 文件与职责映射

**新增：**

- `src/MineRailMonitor.Core/Models/YardCommunicationConfig.cs`：站场本地通信接口配置，包含 `YardId`、`ListenIp`、`ListenPort`、`Enabled`。
- `src/MineRailMonitor.Core/Services/YardCommunicationContext.cs`：单站场运行容器，持有该站场的 transport、poller、runtime、取消令牌、通信状态和异常信息。
- `src/MineRailMonitor.Core/Services/YardCommunicationManager.cs`：维护所有上下文，负责批量启动/停止、独立重启、事件汇总和全局状态快照。
- `tests/MineRailMonitor.Core.Tests/YardCommunicationContextTests.cs`：上下文生命周期、状态和故障隔离测试。
- `tests/MineRailMonitor.Core.Tests/YardCommunicationManagerTests.cs`：多站场并发、同协议地址、Clear/Reset 隔离测试。
- `tests/MineRailMonitor.Infrastructure.Tests/YardCommunicationConfigurationTests.cs`：配置读写和端点校验测试。

**修改：**

- `src/MineRailMonitor.Core/Models/ProjectConfig.cs`：增加 `YardCommunications` 配置集合。
- `src/MineRailMonitor.Infrastructure/Configuration/ProjectConfigService.cs`：读写 `YardCommunications`，校验站场唯一性、IP/端口合法性和监听端点冲突；保留旧配置兼容加载。
- `src/MineRailMonitor/MainWindow.xaml.cs`：将单一 `_rfidUdpTransport/_rfidPoller/_rfidRuntimeCoordinator` 改为 manager 管理的多上下文；按上下文汇总 UI、记录和告警。
- `src/MineRailMonitor/Pages/SettingsPage.xaml`：增加站场通信接口配置区域，显示当前站场监听 IP、端口、运行状态和错误信息。
- `src/MineRailMonitor/Pages/SettingsPage.xaml.cs`：管理员模式下编辑、校验、保存通信接口配置；应用配置时只重启当前站场上下文。
- `src/MineRailMonitor/Pages/CommunicationPage.xaml.cs`：按当前站场显示独立通信统计和日志，提供全局汇总视图。
- 现有相关 Core/Infrastructure/App 测试：把“单全局 listener/poller”断言改为“多上下文隔离”断言，但不删除协议兼容测试。

---

## Task 1: 定义站场通信配置与兼容策略

**Files:**

- Create: `src/MineRailMonitor.Core/Models/YardCommunicationConfig.cs`
- Modify: `src/MineRailMonitor.Core/Models/ProjectConfig.cs`
- Modify: `src/MineRailMonitor.Infrastructure/Configuration/ProjectConfigService.cs`
- Test: `tests/MineRailMonitor.Infrastructure.Tests/YardCommunicationConfigurationTests.cs`

**Interfaces:**

- `YardCommunicationConfig` 至少提供：`YardId`、`ListenIp`、`ListenPort`、`Enabled`。
- `ProjectConfig.YardCommunications` 类型为 `IReadOnlyList<YardCommunicationConfig>`。
- 配置服务提供站场通信配置的加载和保存入口，保存前返回逐项中文错误。

- [ ] **Step 1: 写失败测试**

测试覆盖：配置能保存并重新加载；IP 无效、端口越界、站场编号重复、两个站场监听端点重复时保存失败；缺少 `YardCommunications` 的旧项目只进入兼容模式并产生诊断，不自动假定 560/620 已隔离。

- [ ] **Step 2: 运行配置测试确认失败**

Run: `dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj --filter FullyQualifiedName~YardCommunicationConfigurationTests --no-restore`

Expected: 由于配置模型和保存入口不存在而失败。

- [ ] **Step 3: 实现最小配置模型和持久化**

在 `project.json` 增加类似以下数据结构；具体端口以项目配置为准，运行时代码不内置 560/620 端口：

```json
"YardCommunications": [
  {
    "YardId": "560",
    "ListenIp": "127.0.0.1",
    "ListenPort": 62002,
    "Enabled": true
  },
  {
    "YardId": "620",
    "ListenIp": "127.0.0.1",
    "ListenPort": 62012,
    "Enabled": true
  }
]
```

旧项目缺少该字段时，只生成一个明确标记为 `LegacySharedListener` 的兼容配置，保留现有 `App.config` 的监听设置；不把它报告为独立模式。

- [ ] **Step 4: 运行配置测试确认通过**

Run: `dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj --filter FullyQualifiedName~YardCommunicationConfigurationTests --no-restore`

Expected: 全部通过。

---

## Task 2: 抽出单站场通信上下文

**Files:**

- Create: `src/MineRailMonitor.Core/Services/YardCommunicationContext.cs`
- Modify: `src/MineRailMonitor.Core/Communication/RfidUdpTransport.cs` only when lifecycle injection is required
- Test: `tests/MineRailMonitor.Core.Tests/YardCommunicationContextTests.cs`

**Interfaces:**

- `YardCommunicationContext` 接收一个 `YardCommunicationConfig`、该站场的 `RfidStationConfig` 集合、`RfidSettings`、`IPassageRecordStore` 以及 transport/poller 构造依赖。
- 对外提供：`StartAsync(CancellationToken)`, `StopAsync()`, `RestartAsync(...)`, `IsRunning`, `LastError`, `RuntimeStates`, `PollingStatuses`, `ClearCount`, `RequestCount`, `ResponseCount`。
- `ProcessDatagram` 只把当前上下文监听端口收到且匹配本上下文 station endpoint/address 的帧交给本上下文 Runtime。

- [ ] **Step 1: 写失败测试**

测试两个上下文可同时启动；560 的 slot/runtime 状态变化不影响 620；停止 560 后 620 仍运行；560 监听异常只记录在 560；Clear/Reset 只影响当前上下文；同 `ProtocolAddress` 但不同目标端口可同时存在。

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~YardCommunicationContextTests --no-restore`

Expected: 上下文类型不存在或测试无法建立独立生命周期。

- [ ] **Step 3: 实现单上下文生命周期**

每个上下文独立创建并持有：

```text
YardCommunicationContext
├─ YardCommunicationConfig
├─ IReadOnlyList<RfidStationConfig> stationsOfThisYard
├─ RfidUdpTransport (独立本地 ListenIp/ListenPort)
├─ RfidStationPoller (只轮询本站场 stations)
├─ RfidRuntimeCoordinator (只保存本站场 runtime state)
├─ CancellationTokenSource
└─ counters/status/logs
```

`StopAsync` 必须先停止 poller，再停止并释放本上下文 transport；异常捕获到上下文状态，不向 manager 抛出导致全局退出。不得改变 `RfidFrameParser`、`RfidRequestFrameBuilder`、`RfidRuntimeCoordinator` 的业务判定。

- [ ] **Step 4: 运行上下文测试确认通过**

Run: `dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~YardCommunicationContextTests --no-restore`

Expected: 全部通过。

---

## Task 3: 增加多站场 manager 和路由

**Files:**

- Create: `src/MineRailMonitor.Core/Services/YardCommunicationManager.cs`
- Modify: `src/MineRailMonitor/MainWindow.xaml.cs`
- Test: `tests/MineRailMonitor.Core.Tests/YardCommunicationManagerTests.cs`

**Interfaces:**

- `YardCommunicationManager` 维护 `IReadOnlyDictionary<string, YardCommunicationContext>`，按 `YardId` 查找上下文。
- 对外提供：`StartAllAsync`, `StopAllAsync`, `StartYardAsync(yardId)`, `StopYardAsync(yardId)`, `RestartYardAsync(yardId)`, `GetSnapshot()`。
- 全局快照聚合各站场计数；单站场快照只来自对应上下文。

- [ ] **Step 1: 写失败测试**

至少覆盖：同时启动 560/620；两个 listener 端口都可接收；560 Clear 不清空 620；停止 560 不影响 620；一个上下文异常时 manager 仍能启动/保持其他上下文；全部停止后两端口均释放并可再次启动。

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~YardCommunicationManagerTests --no-restore`

Expected: 由于不存在 manager 和多端点路由而失败。

- [ ] **Step 3: 实现 manager**

启动流程为：加载并校验所有站场通信配置 → 为每个站场筛选 `RfidStationConfig.YardId` → 创建上下文 → 分别启动；任一站场失败只更新该站场状态并继续其他站场。旧项目兼容模式只创建一个共享上下文，并在快照中明确显示“兼容共享监听”。

`MainWindow` 不再以一个全局 `RfidRuntimeCoordinator` 处理所有帧；首页全局视图改为聚合 manager 快照，站场视图从当前上下文取状态。现有 `YardRfidStationResolver` 和 `YardPassageFilter` 继续负责地图绑定与历史数据范围，不把 YardId 写入 Passage schema。

- [ ] **Step 4: 运行 manager 测试确认通过**

Run: `dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~YardCommunicationManagerTests --no-restore`

Expected: 全部通过。

---

## Task 4: 接入设置页和管理员配置应用

**Files:**

- Modify: `src/MineRailMonitor/Pages/SettingsPage.xaml`
- Modify: `src/MineRailMonitor/Pages/SettingsPage.xaml.cs`
- Modify: `src/MineRailMonitor/MainWindow.xaml.cs`
- Modify: `src/MineRailMonitor/Pages/CommunicationPage.xaml.cs`
- Test: `tests/MineRailMonitor.Core.Tests/SettingsPageMarkupTests.cs`

- [ ] **Step 1: 写失败测试**

断言：设置页显示当前站场通信接口；未进入管理员模式时 IP/端口不可编辑；应用当前站场配置只停止/释放/重绑当前上下文；端口占用提示“端口已被占用”，其他站场保持运行；全部启动/全部停止状态正确显示。

- [ ] **Step 2: 运行 UI 标记测试确认失败**

Run: `dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~SettingsPageMarkupTests --no-restore`

Expected: 缺少站场通信接口配置控件或事件绑定。

- [ ] **Step 3: 实现管理员配置和独立应用**

在设置页增加紧凑的站场接口区域：站场选择、监听 IP、监听端口、启用状态、运行状态、错误提示。保存前校验当前站场配置与全局端点冲突；应用配置时调用 `RestartYardAsync`，不重建其他上下文，不清空其他站场槽位或 Runtime。

通信日志提供“当前站场/全部站场”范围，日志行必须带 `YardId`，避免 560 和 620 在视觉上混在一起。

- [ ] **Step 4: 运行 UI 标记测试确认通过**

Run: `dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter FullyQualifiedName~SettingsPageMarkupTests --no-restore`

Expected: 全部通过。

---

## Task 5: 保持历史、统计和当前站场显示一致

**Files:**

- Modify: `src/MineRailMonitor/MainWindow.xaml.cs`
- Modify: `src/MineRailMonitor/Pages/HistoryPage.xaml.cs`
- Modify: `src/MineRailMonitor/Pages/AlarmHistoryPage.xaml.cs`
- Modify: `src/MineRailMonitor/Pages/RfidStatisticsPage.xaml.cs`
- Test: existing `YardContextMarkupTests`, `YardPassageFilterTests`, `PassageRecordStoreTests`, `SqlitePassageRecordStoreTests`

- [ ] **Step 1: 增加隔离回归断言**

确认当前已经建立的规则继续成立：选 560 时只展示 560 的 StationId；选 620 时只展示 620；全局总览才展示全部；统计不能按 `ProtocolAddress` 分组；历史 SQLite 数据不迁移、不复制。

- [ ] **Step 2: 将 manager 快照接入现有页面**

页面状态来源改为当前 `YardCommunicationContext`；记录范围继续使用 `YardPassageFilter`/`PassageQuery.StationIds`，不要新增按协议地址过滤的旁路逻辑。站场切换只刷新显示范围，不调用任何其他站场的 `Reset`、`Clear` 或 `Stop`。

- [ ] **Step 3: 运行相关回归测试**

Run: `dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj --filter "FullyQualifiedName~YardContextMarkupTests|FullyQualifiedName~YardPassageFilterTests|FullyQualifiedName~PassageRecordStoreTests" --no-restore`

Run: `dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj --filter FullyQualifiedName~SqlitePassageRecordStoreTests --no-restore`

Expected: 当前站场筛选和 SQLite 范围查询全部通过。

---

## Task 6: 端到端验证与人工联调

**Files:**

- Modify: `tests/MineRailMonitor.Core.Tests/` only for missing regression cases
- Modify: `tests/MineRailMonitor.Infrastructure.Tests/` only for missing persistence cases
- Modify: `scripts/run-rfid-acceptance.ps1` only if the existing acceptance harness needs a multi-yard scenario
- Modify: project template `Projects/Default/project.json` only to add explicit editable `YardCommunications` data

- [ ] **Step 1: 增加自动化场景**

新增场景验证：

1. 同进程同时监听 560/620 两个不同端口。
2. 两站均能独立完成 Read/Response。
3. 560 槽位增加不改变 620。
4. 620 Clear 不清空 560。
5. 停止 560 后 620 继续响应。
6. 两站相同 `ProtocolAddress`、不同 endpoint 时都能正确匹配。
7. 单站异常不会让其他 listener 退出。
8. 两站 ScenarioPlayer/Runtime/日志/计数器各自独立。
9. 全部停止后端口释放，重新全部启动成功。

- [ ] **Step 2: 执行完整验证**

```text
Core Tests
Infrastructure Tests
Release Build
Simulator Build
RFID Acceptance 8/8
git diff --check
```

另外执行真实 UDP 人工验证：先启动 560 和 620 的模拟分站，分别加载不同场景；在上位机切换站场确认页面只变更显示范围，不影响另一站播放和轮询；分别执行 Clear、Stop、Restart，确认另一站仍保持运行。

- [ ] **Step 3: 输出结果，不提交代码**

报告修改文件、上下文架构、配置入口、560/620 并发操作步骤、同协议地址隔离结果、完整测试结果以及兼容模式提示；不执行 commit 和 push。

---

## 风险与处理边界

1. **旧配置没有 YardCommunications：** 只能进入兼容共享监听并提示管理员配置，不能把一个全局监听器错误地宣称为 560/620 独立通信。
2. **当前 620 没有绑定 RFID 基站：** 即使通信接口已创建，620 仍会显示空状态；必须先给 RFID 基站设置 `YardId=620`，并在 620 地图绑定对应 `RfidStationId`。
3. **协议地址相同：** 必须用 `Endpoint + ProtocolAddress` 匹配；不能按 `ProtocolAddress` 单独路由。
4. **端口占用：** 只让当前站场进入停止/异常状态，保留错误信息，manager 不退出，其他站场继续运行。
5. **历史数据隔离：** 继续按 `StationId` allow-list 过滤；不修改 SQLite schema，也不把站场编号改成协议地址。

## 方案自检

- 覆盖了独立配置、独立 listener、独立 poller、独立 Runtime、独立 Clear/Reset、故障隔离、管理员配置、历史统计过滤和人工联调。
- 没有要求修改正式 Protocol、Runtime、Passage 或数据库结构。
- 没有把 560/620 写入通信业务代码；默认值仅作为项目配置数据示例。
- 方案按当前项目的 `RfidStationConfig.YardId`、`RfidUdpTransport`、`RfidStationPoller`、`RfidRuntimeCoordinator` 结构展开。
