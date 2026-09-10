# MineRailMonitor

矿山轨道运输智能监控系统阶段 1.6，基于 .NET Framework 4.8 WPF。

## 开发验证

```powershell
dotnet restore MineRailMonitor.sln
dotnet build MineRailMonitor.sln -c Release
dotnet test MineRailMonitor.sln -c Release --no-build
```

启动程序：

```powershell
dotnet run --project src/MineRailMonitor/MineRailMonitor.csproj
```

Framework-dependent 发布（依赖现场已安装的 .NET Framework 4.8；开发阶段保持 Any CPU）：

```powershell
msbuild MineRailMonitor.sln /t:Build /p:Configuration=Release /p:Platform="Any CPU"
```

默认项目配置位于 `Projects/Example`。这是不含现场地图和客户 CAD 坐标的脱敏示例，缺少底图时程序显示“未配置站场底图”，不会生成或绘制示意轨道。现场项目资料放在本地 `Projects/Default`，该目录不进入 Git。

当前阶段包含配置加载、真实图片底图承载、坐标转换、40 Byte RFID UDP 协议解析、查询应答 Simulator、可配置六基站轮询、基站独立车厢识别/脱节状态机，以及本机 SQLite PassageRecord 历史保存、PendingClear 恢复和历史查询。地图点位不自动视为 RFID 基站。

## 本地管理员模式配置

管理员口令不再写入源码。运行前可在本机设置环境变量 `MINE_RAIL_ADMIN_PASSWORD`，或在未提交的程序配置中设置 `appSettings` 的 `AdminPassword`；仓库中的 `src/MineRailMonitor/App.config` 只保留空值模板。未配置口令时，管理员模式会拒绝进入。不要把真实口令写回仓库。

## RFID 自动验收（阶段 3.3A）

一键运行 8 个业务场景：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-rfid-acceptance.ps1
```

脚本由 PowerShell 统一调度；Simulator 只执行单个场景。验收模式强制将上位机和 Simulator 固定到 `127.0.0.1` 及测试 UDP 端口（默认 `62102`/`62101`），不会读取或使用正式 `RfidStations` 地址，也不会向现场 IP 发送 Read/Clear。上位机仍通过真实 40 Byte UDP 请求/响应完成端到端链路，人工 Simulator 模式和“扫入下一张/移除标签/清空”功能保持不变。

每个场景使用独立目录：`artifacts/acceptance/<run-id>/<scenario>/`，其中包含独立 SQLite 数据库、原子写入的 `runtime-state.json`、Simulator 请求/响应日志和进程日志。总报告为 `phase33a-result.json`；失败场景目录会保留，便于查看当时的 Runtime、Slots、Read/Clear 日志和 SQLite。成功场景默认清理，可使用 `-KeepSuccessfulArtifacts` 保留全部场景文件。

可选择场景或配置 Debug 构建：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-rfid-acceptance.ps1 -Scenario Normal11,SparseSlots
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-rfid-acceptance.ps1 -Configuration Debug -KeepSuccessfulArtifacts
```
