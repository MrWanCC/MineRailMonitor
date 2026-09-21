# MineRailMonitor Windows 桌面安装与目录布局设计

## 1. 产品定位

MineRailMonitor 是运行在现场 Windows 工控机上的 WPF 桌面上位机。用户通过桌面快捷方式手动启动程序。

本设计明确不引入以下运行形态：

- Windows Service
- 开机自启
- watchdog
- 后台常驻守护进程
- Server/Client 架构

程序进程的生命周期由用户启动和关闭负责。

## 2. 默认安装目录

默认安装目录固定为：

```text
C:\MineRailMonitor\
```

安装界面允许用户选择其他有效的本地目录，例如：

```text
D:\MineRailMonitor\
E:\Software\MineRailMonitor\
```

用户最终选择的目录本身就是 ApplicationRoot / `<Root>`。安装器不得在用户选择的目录下再额外嵌套一层 `MineRailMonitor`。

不使用 `C:\Program Files\MineRailMonitor` 作为默认目录。当前应用需要持续写入 SQLite、日志、黑匣子、备份和项目配置；将可写运行数据与只读程序文件放在同一个可迁移根目录下，可以避免普通运行用户写入 Program Files 时遇到权限问题。

目录布局使用相对 `<Root>` 的路径，不把程序运行时路径绑定到固定盘符；但首版安装器不实现跨 Root 升级或整目录迁移，未来迁移必须通过独立流程设计和验证。

## 3. 目标目录结构

以安装根目录 `<Root>` 为例：

```text
<Root>\
│
├─ App\
│  ├─ MineRailMonitor.exe
│  ├─ MineRailMonitor.exe.config
│  ├─ MineRailMonitor.Core.dll
│  ├─ MineRailMonitor.Infrastructure.dll
│  ├─ 其他第三方 DLL
│  └─ SQLite native runtime files
│
├─ Projects\
│  ├─ Default\
│  └─ Example\
│
├─ Data\
│  └─ MineRailMonitor.db
│
├─ Backups\
│  └─ SQLite\
│     └─ yyyy-MM-dd\
│
├─ Logs\
│  └─ BlackBox\
│
└─ Docs\
```

桌面快捷方式名称：

```text
矿车编组监控系统
```

快捷方式目标：

```text
<Root>\App\MineRailMonitor.exe
```

`App` 只放程序发布文件和运行时依赖。`Projects`、`Data`、`Backups`、`Logs` 和 `Docs` 位于根目录，便于升级保留、备份和后续明确迁移流程管理。

## 4. 路径基准与 ApplicationRoot

当前程序大量使用 `AppContext.BaseDirectory`。程序移动到 `<Root>\App\` 后，安装布局下的 `AppContext.BaseDirectory` 将指向 `App`，因此不能继续直接使用：

```csharp
Path.Combine(AppContext.BaseDirectory, "Data")
```

否则会得到 `<Root>\App\Data`，与目标布局不符。

正式设计引入统一的 `ApplicationRoot` 概念。解析规则必须区分安装布局和开发/未打包布局：

### A. Installed layout

当 `AppContext.BaseDirectory` 的最终目录名为 `App`（大小写不敏感）时，视为 installed layout：

```text
AppContext.BaseDirectory = <Root>\App\
ApplicationRoot = Directory.GetParent(AppContext.BaseDirectory)
                 = <Root>\
```

这里的 parent 必须通过路径 API 获取，禁止用 `..\` 字符串手工拼接。

### B. Development / unpackaged layout

当 `AppContext.BaseDirectory` 的最终目录名不是 `App` 时，视为 development / unpackaged layout：

```text
ApplicationRoot = AppContext.BaseDirectory
```

这样从 `bin\Debug`、`bin\Release` 或 Visual Studio/F5 运行时，不会错误地把 Data、Logs 或 Projects 路径跳到开发输出目录的 parent。

### C. Explicit test injection

路径 provider 必须允许测试显式注入 `ApplicationRoot`。显式注入优先级高于上述自动推导，测试不得依赖机器盘符、当前工作目录或真实安装位置。

### D. Acceptance isolation

现有 Acceptance 的显式 `DatabasePath` / `LogDirectory` contract 必须保持不变。安装目录改造不得让 Acceptance 自动切换到真实安装根目录，也不得破坏其临时目录隔离。

因此最终优先级为：

```text
显式注入 ApplicationRoot
→ 如果 BaseDirectory 最终目录名为 App：Directory.GetParent(BaseDirectory)
→ 否则：BaseDirectory
```

例如用户选择 `D:\MineRailMonitor` 时，最终布局必须是：

```text
D:\MineRailMonitor\App
D:\MineRailMonitor\Projects
D:\MineRailMonitor\Data
D:\MineRailMonitor\Backups
D:\MineRailMonitor\Logs
D:\MineRailMonitor\Docs
```

快捷方式始终指向：

```text
<Root>\App\MineRailMonitor.exe
```

所有可写目录都必须从同一根目录派生：

```text
ApplicationRoot\Data
ApplicationRoot\Backups
ApplicationRoot\Logs
ApplicationRoot\Projects
ApplicationRoot\Docs
```

后续实现应提供统一的路径 provider / paths abstraction，负责：

- 规范化根目录和目录分隔符；
- 创建需要的可写目录；
- 暴露 Data、Backups、Logs、BlackBox、Projects 和 Docs 路径；
- 让测试可以注入临时根目录；
- 避免模块分别推导根目录。

禁止各模块自行通过 `..\` 或字符串拼接猜测安装根目录。开发布局不能因为发布布局的 parent 规则而改变路径。

路径 provider 只负责路径定义和目录准备，不改变 SQLite、备份、恢复或日志业务语义。

## 5. Projects 部署与升级规则

当前 csproj 会将 Projects 内容复制到 output/publish。新安装结构中，Projects 不应放在 `App` 下，而应部署到：

```text
<Root>\Projects
```

首次安装：

1. 创建 `<Root>\Projects`；
2. 部署 `Default` 和 `Example` 模板；
3. 创建快捷方式；
4. 可选地立即启动应用。

升级：

- 不覆盖已经存在的现场 `Projects`；
- 不直接覆盖现场 `project.json` 或其他现场配置；
- 默认只创建缺失的目录和明确标记为新增的模板文件；
- 模板内容发生变化时，必须通过单独的配置迁移设计处理，不能由安装器静默覆盖。

安装器不负责解释项目配置 schema，也不负责修改项目业务内容。

## 6. Data 与 SQLite 生命周期

正式数据库位置为：

```text
<Root>\Data\MineRailMonitor.db
```

现有 SQLite 语义保持不变，包括：

- WAL；
- `synchronous=FULL`；
- health inspection；
- backup；
- recovery；
- recovery marker；
- corrupt evidence。

本安装布局不重新设计 SQLite，不把数据库复制到 `App`，不由安装器创建或替换生产数据库。安装、升级和卸载流程必须把 Data 作为现场数据目录处理。

## 7. Backups

备份继续位于：

```text
<Root>\Backups\SQLite\yyyy-MM-dd\...
```

备份目录是现场历史数据的一部分：

- 安装升级不得删除；
- 重新安装不得默认删除；
- 卸载默认保留；
- 安装器不执行 SQLite backup retention；
- 备份和恢复仍由应用现有维护组件负责。

备份文件的命名、日期目录、健康检查、恢复 marker 和 corrupt evidence 语义由 SQLite backup/recovery 设计维护，不在安装器中复制实现。

## 8. Logs 与 Raw Packet BlackBox

普通日志继续位于：

```text
<Root>\Logs
```

Raw Packet BlackBox 继续位于：

```text
<Root>\Logs\BlackBox
```

升级不得清空或覆盖日志和黑匣子数据。卸载默认保留。重装后应用应继续使用原有日志和黑匣子目录，不创建一套位于 `App` 下的平行目录。

## 9. 安装体验

目标流程：

```text
安装程序（默认显示 C:\MineRailMonitor）
→ 选择安装目录
→ 将程序文件安装到 <Root>\App
→ 首次部署 Projects 模板
→ 创建 Data/Backups/Logs/Docs 等目录（按需）
→ 创建桌面快捷方式
→ 可选择立即启动
```

安装过程可以请求管理员权限，但安装完成后的 `MineRailMonitor.exe` 不应要求“以管理员身份运行”。

安装器必须记住上一次成功安装的 `<Root>`。首次安装默认显示 `C:\MineRailMonitor`；用户主动选择其他目录后，该目录成为后续升级默认复用的安装目录。

首版使用一个固定且跨版本不变的 Inno Setup `AppId`。安装脚本使用 Inno Setup 的 canonical literal-brace 语法表达固定 GUID：字面量 `{` 使用 `{{`，字面量 `}` 使用单个 `}`。不得按版本生成新的 `AppId`。

应用目标框架为 .NET Framework 4.8。安装器首版不自动联网下载 Framework，而是在安装初始化阶段读取：

```text
HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\Release
```

在 64 位系统同时检查 64 位 registry view；`Release >= 528040` 才视为满足 .NET Framework 4.8。未满足时安装器必须阻止安装，并明确提示“请先安装 .NET Framework 4.8”。

不创建以下系统集成：

- Windows Service；
- Scheduled Task；
- Startup shortcut；
- Registry Run 自动启动项。

## 10. 升级策略

升级安装必须优先复用上一次已安装的 `<Root>`。例如首次安装选择 `D:\MineRailMonitor` 后，后续升级默认继续使用 `D:\MineRailMonitor`，不能静默恢复到 `C:\MineRailMonitor`。

首版禁止跨 Root 升级。检测到已有相同 `AppId` 安装后，安装器必须读取 previous Root 并复用它；如果用户把 Root 改成其他目录，必须阻止继续，而不是弹出“是否继续”的确认后放行。提示必须包含：

```text
已安装版本位于 <previous Root>。
当前版本不支持升级时迁移安装目录。
请继续使用原安装目录；如需迁移，请先完成独立的数据迁移流程。
```

安装器不得自动复制 `Data`、`Projects`、`Backups` 或 `Logs`，不得自动移动 Backups，不得改写 recovery marker，也不得允许用户点击 Yes 绕过该阻止。跨 Root 数据迁移属于后续独立设计，不属于首版安装器。

升级只允许替换：

- `<Root>\App\` 下的程序文件和运行时依赖；
- 明确属于发布文档的必要 `Docs\` 文件。

升级默认不得覆盖：

- `<Root>\Projects\`；
- `<Root>\Data\`；
- `<Root>\Backups\`；
- `<Root>\Logs\`。

升级前应关闭 MineRailMonitor，确保数据库连接、备份、恢复和黑匣子写入已经停止。安装器不替代应用的 shutdown 流程。

如果未来需要数据库或项目配置 schema 升级，必须单独设计、测试并执行 migration；安装器不能通过覆盖文件实现 schema upgrade。

## 11. 卸载策略

默认卸载删除：

- `<Root>\App\`；
- MineRailMonitor 桌面快捷方式；
- 安装器登记信息。

默认保留：

- `<Root>\Projects\`；
- `<Root>\Data\`；
- `<Root>\Backups\`；
- `<Root>\Logs\`；
- `<Root>\Docs\` 中的现场文档。

卸载界面必须明确提示：

```text
是否同时删除现场数据和历史记录？
```

默认选择 No。第一次选择 No 时，卸载继续执行，但保留 `Projects`、`Data`、`Backups`、`Logs` 和 `Docs` 中的现场文件。

如果第一次选择 Yes，第二次确认必须明确显示：

```text
将删除 Projects、Data、Backups、Logs 和 Docs 中的现场文件，是否继续？
```

第二次选择 No 时，必须把 `DeleteFieldData` 重新设为 `False`，保持卸载结果为成功，不得取消整个 uninstall。只有两次都选择 Yes，卸载器才删除具体的 `Projects`、`Data`、`Backups`、`Logs` 和 `Docs` 目录；不得调用 `DelTree(<Root>)` 删除整个 Root。App、快捷方式和安装登记信息仍按正常卸载流程清理；当 Root 为空时，允许 Inno 的正常目录清理移除空 Root。

## 12. 跨 Root 迁移边界（首版不实现）

首版安装器不实现跨 Root 升级或数据迁移。不能把同一个稳定 `AppId` 的第二个安装目录当作 side-by-side 实例，也不能在已安装机器上把：

```text
D:\MineRailMonitor
```

直接改成另一个 Root 并继续升级。安装器不得复制、移动或覆盖旧 Root 的现场数据，不得改写绝对路径，不得修改 recovery marker。未来如果需要整目录搬迁，必须单独设计迁移流程并单独验证；本版本只保证同一 Root 的升级复用。

Recovery marker 是迁移的特殊边界。当前 marker 可能包含绝对路径，因此：

- 存在未完成 recovery marker 时，不能把目录迁移当作普通启动处理；
- 不得由安装器偷偷重写 marker 中的路径；
- 应先完成或明确处理 recovery；
- marker relocation 需要后续单独设计安全规则。

本安装任务不修改 recovery marker 语义。

## 13. 权限模型

正常运行不应要求管理员权限。普通用户不能对整个 `<Root>` 授予 `Modify`，否则会使 `App` 中的 exe/dll 也可写。

安装器必须设置并验证 effective ACL，而不能只追加 Inno Setup `Permissions` ACE。允许调用 Windows 自带 `icacls.exe`，使用稳定 SID，不依赖本地化组名；只修改 `<Root>` 及其子目录，不修改 Root 之外的父目录和系统目录，不授予 `Everyone` Full Control。规范化必须先清除目标树已有的 explicit DACL，再建立受保护的批准 ACL；不能假设 `/inheritance:r` 或 `/grant:r` 会删除未列出的 `Everyone`、`Authenticated Users` 或其它组 ACE。

目标 effective ACL 为：

| 目录 | 普通用户权限 |
| --- | --- |
| `<Root>\` | Read / Execute |
| `<Root>\App\` | Read / Execute |
| `<Root>\Data\` | Modify |
| `<Root>\Backups\` | Modify |
| `<Root>\Logs\` | Modify |
| `<Root>\Projects\` | Modify |
| `<Root>\Docs\` | Modify（现场文档写入） |

安装器应在管理员上下文中按固定顺序重置并保护目标目录。首先对 `<Root>` 执行 `/reset`，再执行 `/inheritance:r` 和 `/grant:r`；然后对每个 managed subtree 执行 `/reset /T /C`，使其及 descendants 回到已经安全的父级 ACL，再对该 top-level subtree 执行 `/inheritance:r` 和 `/grant:r`。这样可清除目标树原有的 explicit ACE；每条 `icacls` 命令返回非零时安装必须失败。命令模板如下，`S-1-5-18` 是 SYSTEM，`S-1-5-32-544` 是 Administrators，`S-1-5-32-545` 是内置 Users：

```powershell
icacls "$Root" /reset
icacls "$Root" /inheritance:r
icacls "$Root" /grant:r "*S-1-5-18:(OI)(CI)(F)" "*S-1-5-32-544:(OI)(CI)(F)" "*S-1-5-32-545:(OI)(CI)(RX)"

icacls "$Root\App" /reset /T /C
icacls "$Root\App" /inheritance:r
icacls "$Root\App" /grant:r "*S-1-5-18:(OI)(CI)(F)" "*S-1-5-32-544:(OI)(CI)(F)" "*S-1-5-32-545:(OI)(CI)(RX)"
icacls "$Root\Data" /reset /T /C
icacls "$Root\Data" /inheritance:r
icacls "$Root\Data" /grant:r "*S-1-5-18:(OI)(CI)(F)" "*S-1-5-32-544:(OI)(CI)(F)" "*S-1-5-32-545:(OI)(CI)(M)"
icacls "$Root\Backups" /reset /T /C
icacls "$Root\Backups" /inheritance:r
icacls "$Root\Backups" /grant:r "*S-1-5-18:(OI)(CI)(F)" "*S-1-5-32-544:(OI)(CI)(F)" "*S-1-5-32-545:(OI)(CI)(M)"
icacls "$Root\Logs" /reset /T /C
icacls "$Root\Logs" /inheritance:r
icacls "$Root\Logs" /grant:r "*S-1-5-18:(OI)(CI)(F)" "*S-1-5-32-544:(OI)(CI)(F)" "*S-1-5-32-545:(OI)(CI)(M)"
icacls "$Root\Projects" /reset /T /C
icacls "$Root\Projects" /inheritance:r
icacls "$Root\Projects" /grant:r "*S-1-5-18:(OI)(CI)(F)" "*S-1-5-32-544:(OI)(CI)(F)" "*S-1-5-32-545:(OI)(CI)(M)"
icacls "$Root\Docs" /reset /T /C
icacls "$Root\Docs" /inheritance:r
icacls "$Root\Docs" /grant:r "*S-1-5-18:(OI)(CI)(F)" "*S-1-5-32-544:(OI)(CI)(F)" "*S-1-5-32-545:(OI)(CI)(M)"
```

`Data`、`Backups`、`Logs`、`Projects`、`Docs` 以及 recovery marker 所在目录必须由安装器预创建并经过上述 effective-permission 检查。程序运行时不应依赖向 `App` 写入数据，普通用户不得修改或删除 `App` 下的 exe/dll。

验收必须覆盖 hostile pre-existing ACL：在 disposable Root 安装前预创建目录，并用 `icacls <Root> /grant:r "*S-1-1-0:(OI)(CI)(M)"` 写入 `Everyone:(M)`。安装完成后用 `icacls` 确认 Root/App 不存在 `Everyone` Modify 或其它未知 explicit write ACE，Root/App 的 Users 只有 Read/Execute，Data/Backups/Logs/Projects/Docs 的 Users 为 Modify；再使用真实普通非管理员账户验证数据目录可写而 App 下 exe/dll 不能修改或删除。markup test 不能替代这些证据。

安装器本身可请求提升权限完成安装和 ACL 设置；安装完成后的 MineRailMonitor.exe 保持普通用户权限启动。

## 14. 安装器选型

候选方案：

### A. Inno Setup

推荐优先评估 Inno Setup。它适合当前场景：

- .NET Framework 4.8 WPF 桌面程序；
- 单机工业上位机；
- 支持自定义安装目录；
- 支持部署完整 App 文件；
- 支持创建桌面快捷方式；
- 可通过安装/卸载逻辑保留 Data、Projects、Backups 和 Logs；
- 不要求 Microsoft Store 或 MSIX 沙箱。

### B. WiX Toolset

WiX 适合需要 MSI、企业软件分发、组策略和复杂安装事务的场景。当前应用是单机现场软件，首版安装/升级/卸载规则相对直接；使用 WiX 会增加 MSI 组件、升级码、组件 GUID 和安装事务的维护成本，暂不作为首选。

### C. MSIX

MSIX 对沙箱、签名、应用身份和商店/企业分发更友好，但当前应用需要自定义可写根目录、长期保留现场数据、访问 SQLite native runtime；首版还明确禁止跨 Root 升级。MSIX 的容器与数据隔离语义不适合作为首版现场安装方案，暂不优先。

因此本项目后续安装器实现优先使用 Inno Setup。本轮只记录选型，不引入 Inno Setup 或其他安装器依赖。

## 15. 后续实现阶段

安装与目录布局拆分为独立、可审核的后续任务：

### Task 1：ApplicationRoot / path abstraction

定义统一路径 provider，支持默认根目录和测试注入根目录。

### Task 2：调整 App、Projects、Data、Logs、Backups 路径

将现有运行时路径迁移到统一 `ApplicationRoot`，保持 SQLite、黑匣子、日志和项目加载语义不变。

### Task 3：Release publish layout 构建

生成 `<Root>\App` 所需的发布文件，并验证 Projects 模板不再依赖 App 目录。

### Task 4：Inno Setup installer

实现自定义安装目录、固定 AppId、.NET Framework 4.8 prerequisite 检查、首次模板部署、桌面快捷方式、立即启动选项和数据保留规则。

### Task 5：升级/卸载数据保留验证

验证 effective ACL 不受父目录继承的 Users Modify 影响；升级只复用同一 Root，跨 Root 选择被阻止；卸载默认保留现场数据，只有两次明确确认才删除具体现场目录。

### Task 6：干净 Windows VM / 实机安装验收

在两个独立的干净 Windows snapshot/VM 中验证 .NET prerequisite、安装、effective ACL、普通用户启动、同 Root 升级、跨 Root 阻止和卸载流程。Scenario A 使用默认 C Root，Scenario B 首次安装时手动选择 D Root；不进行同一 AppId 的 side-by-side 第二实例测试。

每个 Task 单独建立分支、提交和 review，不在本设计文档中执行实现。

## 16. 最终验收标准

最终实现必须满足：

- 新电脑运行 installer 后桌面出现“矿车编组监控系统”快捷方式；
- 普通用户双击快捷方式可以正常启动；
- MineRailMonitor 不需要管理员权限运行；
- Installed layout 正确将 `App` 的 parent 解析为 ApplicationRoot；
- Development/F5 layout 不会错误跳到 BaseDirectory 的 parent；
- Acceptance 的显式 DatabasePath / LogDirectory 保持隔离；
- 首次安装默认显示 `C:\MineRailMonitor`；
- 用户可以修改安装路径；
- 用户选中的目录就是 Root；
- 安装不会产生重复的 `MineRailMonitor` 子目录；
- 升级默认记住原安装目录；
- Data、Logs、Backups 可以正常写入；
- 普通用户可以写入 Data、Logs、Backups 和 Projects；
- 普通用户不能修改 App 下的 exe/dll；
- .NET Framework 4.8 Full Release 不满足时安装被阻止，满足时安装成功；
- Release package 包含 `System.Data.SQLite.dll`、`App\x86\SQLite.Interop.dll` 和 `App\x64\SQLite.Interop.dll`；
- `icacls` 实际 ACL 与普通非管理员文件操作共同证明 Root/App 为 Read/Execute、Data/Logs/Backups/Projects/Docs 为 Modify；
- hostile pre-existing `Everyone:(M)` ACL 在安装前存在时，安装后被清除且不残留未知 explicit write ACE；
- SQLite health、backup、recovery 和 marker 语义不退化；
- Projects 能正常加载；
- 软件升级不丢失历史数据库；
- 软件升级不覆盖现场项目配置；
- 卸载默认保留现场数据和历史记录；
- 重装后可以继续使用原有数据；
- 现有 Acceptance 8/8 不退化；
- Scenario A 首装默认使用 `C:\MineRailMonitor`，Scenario B 首装可选择 `D:\MineRailMonitor` 且不产生重复 `MineRailMonitor` 子目录；
- 已安装后尝试跨 Root 升级会被阻止，同 Root 升级继续复用 previous Root；
- 未完成 recovery marker 不会被迁移或安装流程静默改写。

## 17. 本轮限制

本设计只定义安装布局 contract，不执行任何实现；实现必须按独立 implementation plan 分 Task 进行并逐项 review。

禁止本轮：

- 编写 `ApplicationPaths` 或其他生产代码；
- 修改 `App.xaml.cs`；
- 修改任何 csproj；
- 添加 Inno Setup 或其他安装脚本；
- 修改 Projects 模板；
- 修改 SQLite、备份或恢复实现；
- 编写测试代码；
- 执行 implementation plan 中的任何 Task；
- 修改 main。

本轮提交信息：

```text
docs: harden desktop installer implementation plan
```
