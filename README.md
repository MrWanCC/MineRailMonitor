# MineRailMonitor

矿山轨道运输 RFID 车皮计数与脱节监控上位机。

基于 .NET Framework 4.8 + WPF，围绕矿山轨道运输场景实现 RFID 多基站通信、车皮计数、脱节报警、历史记录、报警查询和模拟联调。

> 个人实践项目。当前仓库用于版本管理和项目展示，仅包含脱敏源码与示例配置。

## 已实现功能

- 多 RFID 基站动态配置，数量不写死
- `IP + Port + ProtocolAddress` 独立通信
- 40 Byte RFID UDP 协议
- 14 个 RFID 槽位解析
- 稀疏槽位支持
- Passage 识别
- 默认 11 节，可配置
- 脱节超时报警
- `Clear + WaitForEmpty`
- SQLite 历史持久化
- `PendingClear` 恢复
- `StationId` 稳定身份
- 历史查询
- 报警记录
- 地图 RFID 运行态绑定
- RFID Simulator
- 8 个 Acceptance 场景

## 技术栈

- C# / .NET Framework 4.8
- WPF
- UDP RFID 通信
- SQLite
- PowerShell 自动验收
- xUnit 核心与基础设施测试

## 核心工作流程

1. 在项目配置中维护每个 RFID 基站的 `StationId`、名称、IP、端口、协议地址和启用状态。
2. 上位机按基站独立 Endpoint 发送 Read，并通过真实 40 Byte UDP 响应读取 14 个 RFID 槽位。
3. 按非零 RFID 的首次出现顺序进行 Passage 识别和车皮计数。
4. 达到标准节数时保存完成记录；超过脱节超时仍未达到标准时保存脱节报警记录。
5. 发送 Clear，等待下位机缓存清空并连续确认空状态后回到 Idle。
6. Passage 和 RFID 明细持久化到 SQLite，历史查询与报警记录页复用这些记录。

## 项目结构

```text
MineRailMonitor.sln
├─ src/MineRailMonitor.Core/             核心模型、协议、识别与运行时
├─ src/MineRailMonitor.Infrastructure/   配置、SQLite 与日志基础设施
├─ src/MineRailMonitor.Simulator/        人工 Simulator 与自动场景执行器
├─ src/MineRailMonitor/                  WPF 上位机
├─ tests/                                Core / Infrastructure 测试
├─ scripts/                              自动验收脚本
└─ Projects/Example/                     脱敏示例配置
```

## 快速开始

在 Windows 上安装 .NET Framework 4.8 开发环境和可用的 .NET SDK 后执行：

```powershell
dotnet restore MineRailMonitor.sln
dotnet build MineRailMonitor.sln -c Debug
dotnet test MineRailMonitor.sln -c Debug --no-build
```

启动 WPF 上位机：

```powershell
dotnet run --project src/MineRailMonitor/MineRailMonitor.csproj -c Debug
```

仓库不包含现场项目、客户地图或生产数据库。没有本地现场配置时，程序使用 `Projects/Example`，缺少底图时显示未配置站场底图。

## Example 配置

`Projects/Example/project.json` 是可直接用于开发和测试的脱敏配置，包含两个禁用的示例 RFID 基站：

- Endpoint 使用 `127.0.0.1`
- 使用测试端口和示例协议地址
- 默认轮询间隔为 200ms
- 默认标准节数为 11
- 默认脱节超时为 30 秒
- 不包含现场地图、客户 CAD 坐标或生产设备参数

现场配置应由部署环境单独提供，不要将 `Projects/Default` 或现场 `stations/*.json` 放入仓库。

## RFID Simulator

启动人工 Simulator：

```powershell
dotnet run --project src/MineRailMonitor.Simulator/MineRailMonitor.Simulator.csproj -c Debug
```

人工模式保留扫入下一张、移除标签和清空等操作。Simulator 与上位机之间仍使用真实 RFID UDP 协议链路。

首次使用人工双站场验证时，在模拟器中点击“创建 560 / 620 双站场（12基站）”。左侧切换“560 站场”或“620 站场”，可分别启动、停止对应的 6 个虚拟基站；两个站场的协议地址均为 `01`~`06`，端口组互不重复，并与示例上位机配置对应。

## 自动验收

由 PowerShell 统一调度上位机和 Simulator：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-rfid-acceptance.ps1
```

自动验收包含：

```text
Normal11
Uncoupling10
TwoConsecutiveTrains
TwoStationsConcurrent
ClearReappearingTags
SparseSlots
MultipleHeads
NoHead
```

Acceptance 模式强制使用 `127.0.0.1` 和测试 UDP 端口，不读取或访问正式 RFID 基站地址。每次运行使用独立的 SQLite、Runtime 快照、Simulator 日志和结果目录；生成物位于 `artifacts/`，不会进入 Git。

可选参数示例：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-rfid-acceptance.ps1 -Scenario Normal11,SparseSlots
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-rfid-acceptance.ps1 -Configuration Debug -KeepSuccessfulArtifacts
```

## 管理员模式

本地演示默认管理员口令为 `admin123`。部署环境建议通过环境变量 `MINE_RAIL_ADMIN_PASSWORD` 或未提交的本地配置覆盖 `AdminPassword`，不要继续使用默认口令。

不要将真实口令、Token、Secret 或本地配置文件提交到仓库。

## 后续计划

- 根据实际部署环境补充独立的现场配置和地图资源
- 持续完善发布、部署和运维文档
