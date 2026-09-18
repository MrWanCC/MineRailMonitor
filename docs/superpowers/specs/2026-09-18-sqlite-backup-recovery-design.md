# MineRailMonitor SQLite Backup / Integrity / Recovery 设计

状态：Architecture Design

基线：`557c9dfd53c82848fd3d81f908e2306f60f11c98`

本阶段只定义 SQLite 数据保护、健康检查、备份和恢复边界，不实现生产代码，不修改测试、SQLite schema、RFID 协议或报警生命周期。

## 1. 设计结论

数据库启动门禁由 `App.OnStartup` 负责，并且必须发生在 `MainWindow` 创建之前。

正常启动顺序为：

```text
确定数据库路径
→ 健康检查
→ 对已有健康数据库执行当天补备份（当天没有时）
→ 创建 SqlitePassageRecordStore
→ 创建 MainWindow
→ LoadProject / 恢复运行时状态
→ 启动 YardCommunicationManager
```

数据库损坏时，正常业务运行环境不能创建，RFID 不能启动：

```text
健康检查失败
→ 扫描并逐个复核备份
→ 创建启动级 DatabaseRecoveryDialog
→ 用户恢复或退出
→ 恢复成功后最终健康检查
→ 创建 SqlitePassageRecordStore
→ 创建 MainWindow
```

没有通过最终健康检查时，不创建 MainWindow，不启动 RFID，不创建空数据库绕过故障。

核心不变量：

1. 从不覆盖唯一副本。
2. 任何备份在成为正式备份前必须验证。
3. 损坏的生产数据库及其实际存在的 WAL/SHM 必须保留取证副本。
4. 备份失败不得停止正在运行的 RFID 系统。
5. 恢复期间 RFID 业务绝不启动。
6. 失败的恢复不得静默创建空库。
7. 现有 Passage schema v3、WAL 和 RFID 业务语义保持不变。

## 2. 当前代码事实与设计冲突

当前实现中，`App.OnStartup` 直接执行 `new MainWindow()`；`MainWindow` 构造函数内部再创建 `SqlitePassageRecordStore`。而 `SqlitePassageRecordStore` 构造函数会：

```text
规范化数据库路径
→ 创建父目录
→ OpenConnection
→ InitializeSchema / migration
```

因此损坏数据库可能在启动门禁之前被打开，甚至进入 schema 初始化路径。这与本设计的“先健康检查、后创建 Store”冲突。

当前 `SqlitePassageRecordStore` 已使用以下连接参数，本设计保持不变：

```text
PRAGMA journal_mode=WAL;
PRAGMA synchronous=FULL;
PRAGMA foreign_keys=ON;
PRAGMA busy_timeout=5000;
```

当前 `MainWindow` 还直接拥有 `SqlitePassageRecordStore` 字段。本设计要求后续实现由启动门禁创建并注入已通过门禁的 Store 或等价依赖；具体构造函数形态留给 implementation 阶段决定。

Acceptance 模式当前通过命令行传入独立 `DatabasePath` 和 `LogDirectory`。本设计要求 Acceptance 不触碰生产 `Data/`、`Backups/`、`Data/Corrupt/`，并绕过生产备份调度和恢复 UI；Acceptance 自己的 SQLite 数据路径仍由现有命令行参数控制。

## 3. 范围与非目标

### 目标

- 每日一份经过验证的本地 SQLite 在线备份。
- 启动时 quick check、foreign key check 和必要时的 integrity check。
- 损坏数据库检测与诊断日志。
- 损坏数据库、WAL、SHM 的取证保留。
- 恢复前和替换后的再次验证。
- 最近 14 个本地自然日的健康正式备份保留。
- 恢复期间不创建 MainWindow，不启动 YardCommunicationManager。

### 非目标

本版本不设计：

- 云备份、NAS、Redis、MQ 或数据库主从。
- 自动上传、多机容灾。
- 自动创建空数据库绕过损坏。
- 可配置的复杂备份策略。
- Passage schema 或任何 RFID 协议修改。
- 数据库管理页、备份历史页、设置页备份配置。
- 手工导入导出和云同步。

所有数据保护文件均保存在软件目录内。

## 4. 文件布局与命名

正常数据库：

```text
<AppContext.BaseDirectory>/
└─ Data/
   ├─ MineRailMonitor.db
   ├─ MineRailMonitor.db-wal
   └─ MineRailMonitor.db-shm
```

备份：

```text
<AppContext.BaseDirectory>/
└─ Backups/
   └─ SQLite/
      └─ 2026-09-18/
         └─ MineRailMonitor_20260918_020000.db
```

临时备份使用同一日期目录和 `.tmp.db` 后缀，例如：

```text
MineRailMonitor_20260918_020000.tmp.db
```

损坏数据库取证：

```text
<AppContext.BaseDirectory>/
└─ Data/
   └─ Corrupt/
      └─ 20260918_081523/
         ├─ MineRailMonitor.db
         ├─ MineRailMonitor.db-wal
         └─ MineRailMonitor.db-shm
```

只复制实际存在的 sidecar 文件。缺少 WAL 或 SHM 不应伪造空文件。

正式备份以 `.db` 结尾且位于日期目录中；`.tmp.db` 永远不是健康备份候选，也不参与 retention 计数。

## 5. 健康检查模型

建议组件名：`SqliteDatabaseHealthChecker`。

它只负责打开指定数据库并执行检查，不调用 `SqlitePassageRecordStore.InitializeSchema`，不执行 schema migration，不创建业务 Store。

健康结果至少包含：

- 数据库路径。
- 是否不存在。
- 检查时间。
- quick check 是否通过及返回摘要。
- foreign key check 是否通过及违规摘要。
- integrity check 是否执行、是否通过及详细摘要。
- 总体状态：Missing、Healthy 或 Corrupt。
- 面向日志和 Dialog 的错误摘要。

### 检查规则

数据库不存在：

- 不视为损坏。
- 允许后续创建新数据库。
- 不触发恢复 Dialog。

数据库存在时：

1. 用独立连接执行 `PRAGMA quick_check;`。
2. 执行 `PRAGMA foreign_key_check;`。
3. 只有 quick check 返回 `ok` 且 foreign key check 无结果时才判定 Healthy。
4. quick check 异常、返回非 `ok` 或 foreign key check 有结果时，继续执行 `PRAGMA integrity_check;`，用于确认和记录详细问题。
5. 打开失败、锁定超时或其他 SQLite 异常均判为 Corrupt/Unavailable，不把异常数据库交给 Store 初始化。

健康检查不得因为“能打开文件”就判定健康，也不得只检查主 `.db` 文件的存在。

健康检查连接应沿用现有 SQLite 连接参数，尤其保持 WAL、`synchronous=FULL`、外键和 `busy_timeout=5000` 语义。

## 6. SqliteBackupService 设计

建议组件名：`SqliteBackupService`。

职责：

- 使用 `System.Data.SQLite` Online Backup API。
- 创建临时备份。
- 验证临时备份。
- 将验证通过的临时文件提升为正式 `.db`。
- 扫描健康备份候选。
- 在新备份成功后执行 retention。

### 创建过程

```text
生产 MineRailMonitor.db
→ Online Backup API
→ 同一备份日期目录下的 *.tmp.db
→ integrity_check
→ foreign_key_check
→ 验证通过
→ 原子提升为正式 *.db
```

禁止对正在运行的主数据库使用简单 `File.Copy` 作为备份方案。Online Backup 必须从 SQLite 连接读取一致性内容，并能正确处理 WAL 数据。

临时文件和正式文件放在同一文件系统目录，以便使用原子 rename/move 语义。正式目标存在时，应由每日备份判定逻辑先跳过，而不是盲目覆盖。

### 失败规则

任何打开、Online Backup、写入、验证或 rename 失败：

- 删除当前临时文件（删除失败只记录日志）。
- 记录 Logger.Error 或 Logger.Warning。
- 不修改生产数据库。
- 不清理旧的健康正式备份。
- 不停止正在运行的 RFID 系统。
- 不把失败文件加入恢复候选。

## 7. 每日备份与调度

默认备份时间是本地时间每天 02:00。

启动补备份规则：

- 启动时当天没有正式健康备份：立即创建一份。
- 当天已经有正式健康备份：跳过。
- 例如 01:00 启动并立即备份，02:00 不再重复创建。
- 08:00 启动且当天没有备份：08:00 创建。

判断“当天已有备份”只认验证通过并成功 rename 的正式 `.db`，不认 `.tmp.db` 或失败日志。

所有备份操作必须串行。建议 `DatabaseMaintenanceCoordinator` 持有 `SemaphoreSlim`，启动补备份、02:00 调度和其他未来入口都通过同一互斥区，并在锁内再次检查当天备份是否已经存在。

调度不使用忙循环：

```text
计算下一次本地 02:00
→ 可取消的 Task.Delay / 等价一次性等待
→ 获取备份互斥
→ 检查当天正式备份
→ 必要时创建并验证
→ 释放互斥
→ 计算下一天 02:00
```

协调器必须可停止和 Dispose。备份运行期间不阻塞 RFID Poller、UDP 接收或业务识别线程。

## 8. Retention

只保留最近 14 个本地自然日的健康正式备份。

Retention 只在“一份新备份成功、验证通过并提升为正式文件”之后执行。不得因为启动失败、备份失败、完整性失败、临时 IO 异常而提前清理旧备份。

清理规则：

- 日期目录以本地自然日解释。
- 保留今天以及之前 13 个自然日，共 14 天。
- 超出窗口的日期目录中正式健康备份可以删除。
- 非日期目录必须保留，不作猜测性删除。
- `.tmp.db` 不算健康备份；启动时可以清理明确属于本应用且满足 stale 条件的临时文件。
- `Data/Corrupt/` 完全不受 14 天备份 retention 影响，不自动删除事故证据。

时间计算应使用可注入本地时钟，便于边界测试和避免直接依赖系统当前时间。

## 9. 启动数据库门禁

建议由 `App.OnStartup` 协调 `SqliteDatabaseHealthChecker`、`SqliteBackupService`、`SqliteRecoveryService` 和 `DatabaseMaintenanceCoordinator`。门禁必须在 `new MainWindow()` 前完成。

### 正常数据库路径

对于已存在且 Healthy 的数据库：

```text
health check
→ 当天没有正式健康备份时执行 startup catch-up backup
→ new SqlitePassageRecordStore
→ Store 执行现有 schema version check / migration
→ new MainWindow(store 或等价依赖)
```

备份发生在 Store 创建和 migration 之前，保证版本升级失败时仍有升级前的一致性数据库备份。

对于不存在的数据库：

- 不是故障，不显示损坏 Dialog。
- 允许 Store 创建正常新数据库。
- 初始数据库在成功初始化后可由维护协调器创建第一份正式备份；该路径不需要伪造“迁移前备份”。

### 损坏数据库路径

```text
health check → Corrupt
→ 不创建 MainWindow
→ 不创建 YardCommunicationManager
→ 扫描 Backups/SQLite
→ 按最新到最旧逐个验证候选
→ DatabaseRecoveryDialog
```

如果没有健康备份：

- 显示“数据库损坏且未找到可用备份”。
- 允许打开数据目录、打开备份目录或退出。
- 不允许忽略错误继续运行。
- 不允许自动创建空库。

如果存在健康候选：

- Dialog 展示最近可用候选及验证摘要。
- 用户只能选择恢复、打开目录或退出。
- 不提供“忽略错误继续运行”。

用户取消或退出时显式 Shutdown，不能落入创建 MainWindow 的默认路径。

## 10. 备份候选扫描与再次验证

`SqliteBackupService` 扫描 `Backups/SQLite` 下的正式 `.db`，按日期目录和文件时间从新到旧排序。

每个候选在恢复前都必须重新执行：

- `integrity_check`。
- `foreign_key_check`。

不能因为文件名、目录日期或上次写入成功就跳过再次验证。最新候选损坏时继续尝试下一个候选；候选验证失败只能记录并排除，不得破坏生产数据库。

Dialog 显示的“最近健康备份”必须是这次扫描和复核后实际通过的候选。

## 11. SqliteRecoveryService 设计

建议组件名：`SqliteRecoveryService`。

职责：

- 将用户选中的正式备份复制到 staging。
- 验证 staging。
- 保存生产数据库的 corrupt bundle。
- 安全替换生产数据库。
- 对替换后的生产数据库执行最终健康检查。

### staging 阶段

例如：

```text
Backups/SQLite/2026-09-18/MineRailMonitor_20260918_020000.db
→ Data/MineRailMonitor.restore.tmp.db
→ integrity_check
→ foreign_key_check
```

只有 staging 通过验证才允许进入替换阶段。验证失败时删除或保留可识别的 staging 证据，但不能触碰生产 `.db` 和 sidecar。

### 保存损坏原库

在替换前必须确保所有数据库连接已关闭，然后创建唯一时间戳的 `Data/Corrupt/<timestamp>/` 目录：

- 复制当前 `MineRailMonitor.db`。
- 如果存在，复制 `MineRailMonitor.db-wal`。
- 如果存在，复制 `MineRailMonitor.db-shm`。

只有实际存在的文件才复制。主库复制失败时停止恢复，不得继续覆盖生产数据库。

### 替换阶段

```text
staging 已验证
→ corrupt bundle 已保存
→ 确保旧 WAL/SHM 不会污染新库
→ 替换 MineRailMonitor.db
→ 对新生产库执行最终 health check
```

恢复来源是正式 `.db`，不应把备份目录中的 sidecar 带入生产目录。替换前旧生产 WAL/SHM 已在 corrupt bundle 中保存，并从生产路径移走或清理，防止与恢复后的主库混用。

最终健康检查失败时：

- 不创建 Store。
- 不启动 RFID。
- Logger.Error 记录失败原因。
- 保留原 corrupt bundle。
- 保留恢复失败证据（失败 staging 或最终文件的可追溯副本）。
- UI 显示恢复失败，并只允许退出或打开目录。

不自动回退为“新空库”。

## 12. DatabaseRecoveryDialog

这是 MainWindow 创建前的启动级 modal，不挂在业务页面或已启动的通信管理器上。

至少显示：

- 标题：数据库损坏。
- 当前数据库路径：`Data/MineRailMonitor.db`。
- quick check / foreign key check / integrity check 摘要。
- 最近健康备份的本地时间。
- 最近健康备份文件路径。
- 明确警告：恢复后，备份时间之后产生的历史记录可能丢失。

第一版只提供：

- 恢复此备份。
- 打开备份/数据目录。
- 退出程序。

不提供：

- 忽略错误继续运行。
- 自动创建空数据库。
- 未经再次验证的备份直接恢复。

没有可用备份时，Dialog 变为“数据库损坏且未找到可用备份”，隐藏恢复按钮，只保留打开目录和退出。

## 13. 组件边界

### SqliteDatabaseHealthChecker

只负责 SQLite 健康检查和结构化结果，不负责 migration、不创建 Store、不显示 UI。

### SqliteBackupService

负责 Online Backup、临时文件、备份验证、正式文件提升、健康候选扫描和 retention。它不启动或停止 RFID。

### SqliteRecoveryService

负责 staging、候选复核后的恢复、corrupt bundle、生产替换和最终健康检查。它不创建 MainWindow，不启动 YardCommunicationManager。

### DatabaseMaintenanceCoordinator

负责正常运行期间的 startup catch-up、每日 02:00 调度、备份串行化、可取消停止和日志协调。它不把备份失败转换为 RFID 故障。

### SqlitePassageRecordStore

继续专注：

- CRUD。
- 现有 schema version check。
- 现有 v1/v2/v3 migration。
- 现有 Passage、PendingClear、报警确认/恢复语义。

健康检查和恢复决策不得塞进 Store 构造函数，也不得让 Store 自己弹 Dialog。

## 14. MainWindow 与 App 的所有权

目标结构：

```text
App.OnStartup
→ DatabaseStartupGate / maintenance orchestration
→ 成功后创建 SqlitePassageRecordStore
→ 将 Store 或等价已验证依赖传给 MainWindow
→ MainWindow LoadProject
→ 恢复 PendingClear / 未确认报警
→ YardCommunicationManager Start
```

MainWindow 不再负责决定数据库是否损坏，也不应在构造早期自行打开未经门禁的生产数据库。

完整退出时，仍需保持现有业务关闭顺序：先停止通信和业务运行，再关闭 Store；本设计不改变报警恢复、Raw Packet Black Box 或 560/620 Context 隔离。

## 15. 正常运行期维护

维护协调器在业务运行后计算下一次本地 02:00，并通过可取消的单次等待调度备份。到点后：

```text
检查当天正式健康备份
→ 已存在：skip
→ 不存在：Online Backup + 验证
→ 成功：执行 retention
→ 失败：记录日志，不影响 RFID
```

备份和 retention 不应在 UDP 线程、poller 线程或 WPF UI 线程执行阻塞磁盘操作。UI 只读取维护状态快照或接收日志状态，不直接持有备份锁。

## 16. Acceptance 模式

现有 Acceptance 已使用独立数据库路径、运行时状态路径和日志目录。

第一版处理规则：

- Acceptance 使用自己的 `DatabasePath`。
- 不创建生产 `Backups/SQLite`。
- 不创建生产 `Data/Corrupt`。
- 不启动生产备份 scheduler。
- 不弹生产数据库 recovery UI。
- 现有 8 个场景继续通过独立数据库验证 RFID 业务。

SQLite backup / recovery 本身通过 Infrastructure 层的隔离测试验证，不把生产维护任务混入 Acceptance 流程。

## 17. 日志要求

至少记录以下事件：

- startup health check start/result。
- quick check failure。
- integrity check result。
- foreign key check failure。
- backup start/success/failure。
- 正式备份文件名。
- backup validation failure。
- retention cleanup 结果。
- database corruption detected。
- recovery candidate selected。
- user cancelled / exited。
- corrupt bundle location。
- restore start/success/failure。
- final health result。

日志只记录路径、状态、摘要、时间和错误，不记录整个数据库内容，不输出 RFID Passage 全量数据。

## 18. 失败安全与并发约束

- 启动门禁是唯一允许决定“是否创建业务运行环境”的边界。
- 恢复操作期间禁止创建 Store 和启动通信管理器。
- 日常备份与 02:00 调度共享一个串行互斥，不允许 startup backup 与定时 backup 并发。
- 备份失败只影响维护状态，不改变 `YardCommunicationManager`、`RfidRuntimeCoordinator` 或报警状态。
- 恢复失败不覆盖原库，不启动业务，并保留证据。
- 生产数据库连接关闭后才允许保存 corrupt bundle 和替换文件。
- schema migration 仍由 Store 执行；已有健康库的 startup backup 必须先于 Store 构造。

## 19. 后续实现必须覆盖的测试契约

本设计阶段不添加测试代码。后续实现计划必须覆盖以下行为。

### HealthChecker

- 健康数据库。
- 无数据库文件。
- 无效/损坏数据库。
- quick check 失败。
- foreign key violation。
- integrity check 摘要可记录。

### Backup

- Online Backup 内容完整。
- 正在 WAL 运行的数据库备份一致。
- 验证前只存在 `.tmp.db`。
- 验证失败的临时文件不会变成正式备份。
- 每天最多一份正式备份。
- startup backup 与 02:00 backup 串行化。
- 14 天边界正确。
- 备份失败不会触发破坏性 retention。

### Recovery

- 选择最新健康备份。
- 最新候选损坏时回退到下一个健康候选。
- staging 验证通过前生产库不改变。
- 原始 `.db` 被保存。
- 已存在的 WAL/SHM 被保存。
- 恢复后的生产库再次验证。
- 恢复失败不启动 runtime。
- 无健康备份不创建空数据库。

### Startup ordering

- health gate 发生在 `SqlitePassageRecordStore` 构造前。
- Store 构造发生在 MainWindow 业务运行环境创建前。
- 损坏 DB 阻止 MainWindow / RFID runtime 启动。

### Regression

- 现有 v1/v2/v3 migration。
- 报警确认/恢复。
- PendingClear 启动恢复。
- Raw Packet Black Box。
- Core。
- Infrastructure。
- Acceptance 8/8。

## 20. 现有能力保持不变

实现阶段必须明确验证以下内容没有改变：

- WAL 保持。
- `synchronous=FULL` 保持。
- `foreign_keys=ON` 保持。
- `busy_timeout=5000` 保持。
- Passage schema v3 保持。
- 现有 RFID runtime 保持。
- 报警确认/恢复生命周期保持。
- Raw Packet Black Box 保持。
- 560/620 独立 Context 和相同 ProtocolAddress 隔离保持。

## 21. 待 implementation 阶段确认的边界

以下是设计约束，而不是本阶段的实现任务：

1. `MainWindow` 最终采用 Store 构造函数注入、启动上下文对象，还是等价的受控工厂，需要在不扩大 UI 重构范围的前提下确定。
2. WPF recovery Dialog 的样式应复用现有深色工业 UI 和启动级 modal 样式，不新增数据库管理页面。
3. SQLite Online Backup API 的具体 overload、连接生命周期和 atomic rename 实现需要通过 Infrastructure 测试固定。
4. stale `.tmp.db` 的时间阈值需要在实现阶段以可注入时钟和明确默认值确定，不能把临时文件误删为正式证据。

## 22. 当前设计中已发现的代码冲突

已确认的冲突只有启动所有权和 Store 初始化时机：

- 当前 `App.OnStartup` 直接创建 MainWindow；设计要求先执行启动门禁。
- 当前 `MainWindow` 构造函数直接创建 `SqlitePassageRecordStore`；设计要求 Store 创建推迟到健康检查、备份/恢复决策之后。
- 当前 Store 构造函数同时承担目录创建、连接打开和 schema migration；设计要求健康检查器不调用这些业务初始化路径。
- 当前没有生产级备份 scheduler、recovery Dialog 或 corrupt bundle 流程；这些是新增边界，不应塞进现有 Store。

未发现需要改变的 SQLite pragma、schema、RFID 协议、报警生命周期、Raw Packet Black Box 或双站场通信设计。

Acceptance 的独立 `DatabasePath` / `LogDirectory` 是现有特殊路径，需要在实现阶段显式旁路生产维护目录；否则会违反“不污染真实 Backups/Data/Corrupt”的要求。

本设计不改变这些现有行为，也不在本阶段修改代码。
