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

安装目录应允许应用整体迁移到另一台电脑或另一个盘符，且不依赖旧的绝对路径。

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

`App` 只放程序发布文件和运行时依赖。`Projects`、`Data`、`Backups`、`Logs` 和 `Docs` 位于根目录，便于升级保留、备份和整目录迁移。

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

不创建以下系统集成：

- Windows Service；
- Scheduled Task；
- Startup shortcut；
- Registry Run 自动启动项。

## 10. 升级策略

升级安装必须优先复用上一次已安装的 `<Root>`。例如首次安装选择 `D:\MineRailMonitor` 后，后续升级默认继续使用 `D:\MineRailMonitor`，不能静默恢复到 `C:\MineRailMonitor`。

用户仍可在升级时主动改变安装目录，但这不等同于数据迁移。安装器不得静默把已有 `Data`、`Projects`、`Backups` 或 `Logs` 复制到新目录，也不得简单覆盖新目录中的同名数据。跨目录升级/迁移必须由明确的迁移流程单独处理并取得用户确认。

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

默认不勾选。只有用户明确选择“删除所有数据”后，才允许删除整个 `<Root>`。删除前必须再次显示将被删除的 Data、Projects、Backups 和 Logs 范围。

## 12. 整目录迁移

在软件关闭状态下，允许将整个：

```text
D:\MineRailMonitor
```

复制到另一台电脑或另一个盘符。再次启动后，程序必须通过当前 `ApplicationRoot` 解析 Data、Projects、Backups 和 Logs，不能依赖旧安装路径。

Recovery marker 是迁移的特殊边界。当前 marker 可能包含绝对路径，因此：

- 存在未完成 recovery marker 时，不能把目录迁移当作普通启动处理；
- 不得由安装器偷偷重写 marker 中的路径；
- 应先完成或明确处理 recovery；
- marker relocation 需要后续单独设计安全规则。

本安装任务不修改 recovery marker 语义。

## 13. 权限模型

正常运行不应要求管理员权限。普通用户不能对整个 `<Root>` 授予 `Modify`，否则会使 `App` 中的 exe/dll 也可写。

安装器应按目录设置 ACL：

| 目录 | 普通用户权限 |
| --- | --- |
| `<Root>\` | Read / Execute |
| `<Root>\App\` | Read / Execute |
| `<Root>\Data\` | Modify |
| `<Root>\Backups\` | Modify |
| `<Root>\Logs\` | Modify |
| `<Root>\Projects\` | Modify |
| `<Root>\Docs\` | 根据现场文档需求授予 Write / Modify |

`Data`、`Backups`、`Logs`、`Projects` 以及 recovery marker 所在目录必须由安装器预创建并保证可写。程序运行时不应依赖向 `App` 写入数据，普通用户不得修改 `App` 下的 exe/dll。

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

MSIX 对沙箱、签名、应用身份和商店/企业分发更友好，但当前应用需要自定义可写根目录、长期保留现场数据、访问 SQLite native runtime 和支持整目录迁移。MSIX 的容器与数据隔离语义不适合作为首版现场安装方案，暂不优先。

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

实现自定义安装目录、首次模板部署、桌面快捷方式、立即启动选项和数据保留规则。

### Task 5：升级/卸载数据保留验证

验证升级不覆盖现场项目、数据库、备份和日志；卸载默认保留现场数据，明确删除选项才删除全部数据。

### Task 6：干净 Windows VM / 实机安装验收

在干净 Windows 环境和目标工控机上验证安装、普通用户启动、目录可写、升级、卸载和重装流程。

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
- SQLite health、backup、recovery 和 marker 语义不退化；
- Projects 能正常加载；
- 软件升级不丢失历史数据库；
- 软件升级不覆盖现场项目配置；
- 卸载默认保留现场数据和历史记录；
- 重装后可以继续使用原有数据；
- 现有 Acceptance 8/8 不退化；
- 整目录迁移到不同盘符后不依赖旧绝对安装路径；
- 未完成 recovery marker 不会被迁移或安装流程静默改写。

## 17. 本轮限制

本轮只新增本设计 spec，不执行任何实现。

禁止本轮：

- 编写 `ApplicationPaths` 或其他生产代码；
- 修改 `App.xaml.cs`；
- 修改任何 csproj；
- 添加 Inno Setup 或其他安装脚本；
- 修改 Projects 模板；
- 修改 SQLite、备份或恢复实现；
- 编写测试代码；
- 创建 implementation plan；
- 修改 main。

本轮提交信息：

```text
docs: design desktop installation layout
```
