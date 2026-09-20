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

1. 在形成完整可验证的 corrupt bundle 前，从不对生产数据库执行破坏性替换；替换后原始数据安全由该 bundle 提供。
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
   ├─ MineRailMonitor.db-shm
   └─ .sqlite-recovery-in-progress
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

正式备份必须符合精确命名：

```text
MineRailMonitor_yyyyMMdd_HHmmss.db
```

并且位于对应的本地日期目录中。`.tmp.db` 永远不是健康备份候选，也不参与
retention 计数。异常命名文件不根据猜测纳入候选；`LastWriteTime` 只能作为诊断
信息，不能作为主排序依据。

## 5. 健康检查模型

建议组件名：`SqliteDatabaseHealthChecker`。

它只负责打开指定数据库并执行检查，不调用 `SqlitePassageRecordStore.InitializeSchema`，不执行 schema migration，不创建业务 Store。

健康结果至少包含：

- 数据库路径。
- 状态：`Missing`、`Healthy`、`Corrupt`、`Unavailable` 或 `UnsupportedSchema`。
- 检查时间。
- quick check 是否通过及返回摘要。
- foreign key check 是否通过及违规摘要。
- integrity check 是否执行、是否通过及详细摘要。
- `PRAGMA user_version` 读取到的 `SchemaVersion`；文件不存在时为 `null`。
- 原始异常类型、SQLite/IO 错误码和可供日志使用的摘要。
- 面向日志和 Dialog 的错误摘要。

### 检查规则

`Missing`：

- 主数据库文件不存在，且不存在 `.sqlite-recovery-in-progress`；
- 不视为损坏。
- 允许后续创建新数据库。
- 不触发恢复 Dialog。

`Healthy`：

1. 用 Inspection Connection 执行 `PRAGMA quick_check;`。
2. 执行 `PRAGMA foreign_key_check;`。
3. 只有 quick check 返回 `ok` 且 foreign key check 无结果时才判定 Healthy。

4. 读取 `PRAGMA user_version`；当前 Store 支持的版本由
   `SqlitePassageRecordStore.CurrentSchemaVersion` 提供，不在 HealthChecker 中复制版本常量。
5. `user_version` 为 0、1、2 或当前支持版本时，物理检查通过即可保持 `Healthy`，并返回对应
   `SchemaVersion`。v0/v1/v2/v3 继续遵循 Store 现有初始化和 migration 语义。

`UnsupportedSchema`：

- 数据库物理完整性检查通过；
- `SchemaVersion` 大于 `SqlitePassageRecordStore.CurrentSchemaVersion`；
- 表示应用与数据库版本不兼容，不表示数据库损坏或暂时不可访问；
- 阻止 Store 创建，不执行 migration，不创建空数据库；
- 不进入 corrupt backup recovery，不移动或覆盖生产 `.db`、WAL、SHM；
- 启动级 UI 只提供升级提示、重试、打开数据目录和退出。

`Corrupt` 只表示已经能够确定数据库内容或 SQLite 文件结构损坏，包括：

- quick check 返回确定的完整性错误；
- integrity check 确认完整性错误；
- foreign key check 确认数据完整性违规；
- 其他可以明确判定为 SQLite corruption 的结果。

quick check 异常、返回非 `ok` 或 foreign key check 有结果时，继续执行
`PRAGMA integrity_check;`，用于确认并记录详细问题。只有得到上述确定性证据时才进入
`Corrupt` 和 backup recovery 流程。

`Unavailable` 表示无法可靠判断内容是否损坏，包括：

- database locked、busy timeout；
- access denied、sharing violation；
- 路径、磁盘或文件暂时不可访问；
- 无法归类为确定 corruption 的 IO/SQLite 异常。

`Unavailable` 的行为是：

- 阻止业务启动，不创建 MainWindow；
- 不移动或覆盖生产数据库；
- 不进入自动或人工 backup recovery 候选流程；
- 启动级 UI 只提供重试、打开数据目录和退出。

`Unavailable` 不能被降级成 `Corrupt`，也不能通过“再创建一个空数据库”绕过。

健康状态的最终优先级固定为：

```text
Corrupt → Unavailable → UnsupportedSchema → Healthy
```

因此不能因为 `user_version` 超前就跳过物理检查；确定的 corruption 或无法可靠读取
优先于兼容性判断。读取 `PRAGMA user_version` 本身失败时，按现有 SQLite/IO 异常分类为
`Corrupt` 或 `Unavailable`，不能猜测为 `UnsupportedSchema`。

健康检查不得因为“能打开文件”就判定健康，也不得只检查主 `.db` 文件的存在。

### Business Connection 与 Inspection Connection

业务 `SqlitePassageRecordStore` 继续使用现有 Business Connection 语义：

```text
PRAGMA journal_mode=WAL;
PRAGMA synchronous=FULL;
PRAGMA foreign_keys=ON;
PRAGMA busy_timeout=5000;
```

HealthChecker、backup candidate validation 和 staging validation 使用独立的
Inspection Connection。它不是业务连接，必须满足：

- 不执行 `PRAGMA journal_mode=WAL`；
- 不修改 journal mode、`user_version` 或 schema；
- 不执行 schema migration；
- 只采用非破坏性的只读/只查询检查策略；
- 检查正式 backup 时不产生 `-wal` / `-shm` sidecar；
- 不把备份文件或 staging 文件变成可写业务数据库。

生产数据库的 startup inspection 必须能看到已有 WAL 中的已提交数据，但 inspection
自身不能主动修改 journal mode。System.Data.SQLite connection string/overload、只读
选项和 WAL 可见性组合留 implementation 阶段通过测试确定；本设计不把业务
`ConnectionPragmas` 复用为 inspection 配置。

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

任何打开、Online Backup、写入、验证或正式备份 rename 失败：

- 删除当前临时文件（删除失败只记录日志）。
- 记录 Logger.Error 或 Logger.Warning。
- 不进入生产数据库恢复替换流程；正常备份失败不修改生产数据库。
- 不清理旧的健康正式备份。
- 不停止正在运行的 RFID 系统。
- 不把失败文件加入恢复候选。

## 7. 每日备份与调度

默认备份时间是本地时间每天 02:00。

启动补备份规则：

- `ScanCandidates` 只负责严格正式文件发现、日期目录一致性和规范文件名排序，
  不代表候选内容当前仍 Healthy。
- 启动时扫描全部 `candidate.LocalTimestamp.Date == localNow.Date` 的正式 candidate，
  对每个执行 `healthChecker.Inspect(candidate.Path, SqliteInspectionMode.FullValidation)`。
- 只有本次 FullValidation 返回 `Healthy` 的今日 candidate 才允许跳过备份。
- 今日 candidate 为 `Corrupt`、`Unavailable` 或 `UnsupportedSchema` 时，不能视为已有
  健康备份；继续创建新的 validated backup，不删除损坏的正式文件。
- 例如 01:00 启动并立即备份，02:00 不再重复创建。
- 08:00 启动且当天没有备份：08:00 创建。

判断“当天已有备份”只认本次 FullValidation 仍通过并成功 rename 的正式 `.db`，不认
仅文件名合法的 candidate、`.tmp.db` 或失败日志。多个今日 candidate 中只要有一个当前
Healthy，即可 skip；必须检查到该结论，不能只看最新 candidate。

所有备份操作必须串行。建议 `DatabaseMaintenanceCoordinator` 持有 `SemaphoreSlim`，启动补备份、02:00 调度和其他未来入口都通过同一互斥区，并在锁内再次检查当天备份是否已经存在。

调度不使用忙循环：

```text
计算下一次本地 02:00
→ 可取消的 Task.Delay / 等价一次性等待
→ 获取备份互斥
→ 扫描当天正式 candidate
→ 对今日 candidate 逐个 FullValidation
→ 存在当前 Healthy candidate：skip
→ 否则创建并验证
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

- 只有在不存在 recovery marker 时，才按首次安装处理：
  - 不是故障，不显示损坏 Dialog；
  - 允许 Store 创建正常新数据库；
  - 初始数据库在成功初始化后可由维护协调器创建第一份正式备份；该路径不需要伪造“迁移前备份”。
- 如果存在 recovery marker，即使生产 `.db` 不存在，也绝不能创建空数据库；必须进入“上一次恢复未完成”路径。

启动决策门禁的返回状态固定为：

```text
Missing                 → CreateNew
Healthy                 → StartHealthy
Corrupt                 → RecoverCorrupt
Unavailable             → Unavailable
UnsupportedSchema       → UnsupportedSchema
valid marker            → InterruptedRecovery
marker read/parse error → RecoveryStateError
```

门禁先读取 recovery marker，再检查生产数据库。合法 marker 无论生产 `.db` 是否存在，
都返回 `InterruptedRecovery`，并携带 marker 详情；不能因为生产库存在而直接返回
`StartHealthy`，也不能因为生产库缺失而返回 `CreateNew`。marker malformed、不可读或
contract/hash 无效返回 `RecoveryStateError`，不能降级为 marker absent。

无 marker 时才执行生产数据库 `StartupFast` 检查。只有 `Corrupt` 才扫描备份目录，
并按新到旧对每个候选执行本次 `FullValidation`；只有 `Healthy` 候选进入可恢复列表。
`Unavailable`、`UnsupportedSchema`、`Missing` 候选均排除，并记录排除原因。最新候选
损坏时继续检查较旧候选。真正恢复入口仍必须再次 `FullValidation`，不能把门禁结果当作
最终恢复验证。

`acceptanceMode` 是显式旁路：只检查 acceptance database 的 `StartupFast`，不读取
production recovery marker，不扫描生产备份，不执行 recovery 或 maintenance scheduler。
即使 acceptance 数据库返回 `Corrupt`，也只返回空 candidates 的 `RecoverCorrupt`，由
Acceptance 流程自行处理，不接入生产恢复 UI。

### Unavailable 启动路径

健康检查结果为 `Unavailable` 时：

- 不扫描或选择 backup recovery 候选；
- 不移动、删除、替换或创建生产数据库文件；
- 不创建 Store、MainWindow 或 YardCommunicationManager；
- 启动级 UI 只提供重试、打开数据目录和退出。

重试仍得到 `Unavailable` 时保持该路径，不能把暂时不可访问解释为 Corrupt。

### UnsupportedSchema 启动路径

健康检查结果为 `UnsupportedSchema` 时：

- 显示“数据库版本高于当前应用支持版本，请升级应用程序”，同时显示当前支持版本和实际版本；
- 不创建 `SqlitePassageRecordStore`、MainWindow 或 YardCommunicationManager；
- 不执行 migration、backup recovery、候选扫描或空数据库初始化；
- 不移动、覆盖或删除生产 `.db`、WAL、SHM；
- 启动级 UI 只提供重试、打开数据目录和退出。

### RecoveryStateError 启动路径

recovery marker 无法可信读取或校验时：

- 返回 `RecoveryStateError`，不能按 marker 缺失继续启动；
- 不创建 Store、MainWindow、YardCommunicationManager、RFID 或空数据库；
- 不扫描或选择 recovery candidate，不自动 Resume；
- 启动级 UI 只提供重试、打开数据目录和退出。

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

## 10. Interrupted Recovery / Recovery Marker

恢复 marker 路径为：

```text
<AppContext.BaseDirectory>/Data/.sqlite-recovery-in-progress
```

它是恢复事务边界的一部分，不是普通日志文件。marker 至少持久化以下信息：

- `recoveryStarted` (`bool`)；
- `sourceBackupPath` (`string`)；
- `corruptBundlePath` (`string`)；
- `stagingPath` (`string`)；
- `startedAt` (`DateTimeOffset`)；
- `bundleManifestSha256` (`string`)。

对应共享 contract 为：

```csharp
public sealed class SqliteRecoveryMarker
{
    public bool RecoveryStarted { get; }
    public string SourceBackupPath { get; }
    public string CorruptBundlePath { get; }
    public string StagingPath { get; }
    public DateTimeOffset StartedAt { get; }
    public string BundleManifestSha256 { get; }
}

public sealed class SqliteRecoveryResult
{
    public bool Succeeded { get; }
    public string? ErrorMessage { get; }
    public SqliteRecoveryMarker? Marker { get; }
}
```

恢复状态机为：

```text
candidate validated
→ staging validated
→ corrupt bundle 完整保存
→ 持久化 recovery marker
→ 开始 sidecar / 生产文件替换
→ final health check
→ success
→ 删除 recovery marker
```

在第一次破坏性操作（sidecar 移动/删除或生产文件替换）前，marker 必须已经写入并
持久化。marker 写入失败时停止恢复，不执行任何破坏性操作。corrupt bundle 保存失败
时同样停止，不得继续覆盖生产文件。

启动时必须先检查 marker，再解释生产数据库是否 `Missing`：

- marker 存在且生产 `.db` 缺失：进入“上一次恢复未完成”路径，禁止首次安装初始化；
- marker 存在且生产 `.db` 仍存在：仍阻止普通 Healthy 启动，不能直接忽略 marker；
- marker 存在时，不自动把任意 staging 或生产文件当作已完成恢复。marker JSON malformed、
  unreadable、path contract invalid 或 manifest hash invalid 都不是“marker absent”，
  不得进入首次安装或 `CreateNew`。

第一版不要求自动续跑恢复。可以安全地进入 startup recovery/error UI，重新检查
staging、corrupt bundle、source backup 和 production DB，之后由明确流程完成恢复或
退出。无论哪种中断边界（保存 corrupt bundle 后、sidecar 移除后、生产替换中），
都必须保留 marker 和现有证据，直到最终 health check 成功。只有最终 health check
成功且恢复后的路径已明确成为新的生产数据库时，才允许删除 marker。

## 11. 备份候选扫描与再次验证

`SqliteBackupService` 扫描 `Backups/SQLite` 下符合精确命名的正式 `.db`。候选排序优先
解析规范文件名 `MineRailMonitor_yyyyMMdd_HHmmss.db` 中的本地时间，从新到旧排序；
日期目录只作为路径一致性校验。文件名无法按规范解析的文件不作为候选，也不能靠
`LastWriteTime` 猜测为最新健康备份。

每个候选在恢复前都必须重新执行：

- `integrity_check`。
- `foreign_key_check`。

不能因为文件名、目录日期或上次写入成功就跳过再次验证。最新候选损坏时继续尝试下一个候选；候选验证失败只能记录并排除，不得破坏生产数据库。

Dialog 显示的“最近健康备份”必须是这次扫描和复核后实际通过的候选。

## 12. SqliteRecoveryService 设计

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
- 写入 `bundle-manifest.json`，为每个实际 evidence 记录文件名、length 和 SHA-256；
  db/WAL/SHM 的实际文件集合必须与 manifest 完全一致。
- evidence 和 manifest 使用 durable `WriteThrough` 写入并 `Flush(true)`；marker 的
  `BundleManifestSha256` 锚定 manifest 内容。
- staging 和 production 路径必须限制在约定的 Data 文件名；corrupt bundle 必须限制在
  Data/Corrupt 的直接子目录。

只有实际存在的文件才复制。optional WAL/SHM 只有打开时得到
`FileNotFoundException` 或 `DirectoryNotFoundException` 才能解释为 truly absent；权限、
sharing、目录路径或其它 IO 异常都必须停止恢复。主库或实际存在的 WAL/SHM 复制失败时
不得继续进入破坏性替换。

### 替换阶段

```text
staging 已验证
→ corrupt bundle 已保存
→ recovery marker 已持久化
→ 确保旧 WAL/SHM 不会污染新库
→ 替换 MineRailMonitor.db
→ 对新生产库执行最终 health check
```

破坏性替换开始之前，生产 `.db`、WAL、SHM 不得被修改；此时 staging 必须已验证、
corrupt bundle 必须完整保存、recovery marker 必须成功持久化。恢复来源是正式 `.db`，
不应把备份目录中的 sidecar 带入生产目录。

破坏性替换开始之后，旧生产数据库可能已经不再位于原生产路径；原始生产数据的安全
保证来自替换前保存的完整 corrupt bundle，而不是“原生产路径永远不被覆盖”。旧生产
WAL/SHM 已在 bundle 中保存后，才允许从生产路径移走或清理，防止与恢复后的主库混用。

最终健康检查失败时：

- 不创建 Store。
- 不创建 MainWindow 正常业务环境。
- 不启动 RFID。
- 不创建空数据库。
- Logger.Error 记录失败原因。
- 保留完整 corrupt bundle。
- 保留 recovery marker。
- 保留恢复失败证据（失败 staging 或最终文件的可追溯副本）。
- 进入明确的恢复失败 UI，只允许进入明确的恢复/错误处理流程、打开目录或退出。

不自动回退为“新空库”。

## 13. DatabaseRecoveryDialog

这是 MainWindow 创建前的启动级 modal，不挂在业务页面或已启动的通信管理器上。
`Corrupt`、`Unavailable`、`UnsupportedSchema` 和“recovery marker 存在”是不同的 UI 状态，不能共用会
误导用户的损坏提示。

至少显示：

- 标题：数据库损坏。
- 当前数据库路径：`Data/MineRailMonitor.db`。
- quick check / foreign key check / integrity check 摘要。
- 最近健康备份的本地时间。
- 最近健康备份文件路径。
- 明确警告：恢复后，备份时间之后产生的历史记录可能丢失。

正常 `Corrupt` 恢复状态提供：

- 恢复此备份。
- 重试。
- 打开备份/数据目录。
- 退出程序。

不提供：

- 忽略错误继续运行。
- 自动创建空数据库。
- 未经再次验证的备份直接恢复。

没有可用备份时，Dialog 变为“数据库损坏且未找到可用备份”，隐藏恢复按钮，只保留打开目录和退出。

对于 `Unavailable`：

- 标题和正文明确显示“数据库当前不可访问，无法判断是否损坏”；
- 隐藏恢复候选和恢复按钮；
- 只显示重试、打开数据目录和退出；
- 重试前后都不移动生产文件，也不创建空数据库。

对于 `UnsupportedSchema`：

- 标题和正文显示“数据库版本高于当前应用支持版本，请升级应用程序”；
- 显示当前应用支持版本和数据库实际 `SchemaVersion`；
- 隐藏恢复候选、恢复和继续恢复按钮；
- 只显示重试、打开数据目录和退出；
- 不创建 Store、不执行 migration、不创建空数据库。

对于 `RecoveryStateError`：

- 标题和正文明确显示 recovery marker 无法可信读取或校验；
- 隐藏恢复候选、恢复和继续恢复按钮；
- 只显示重试、打开数据目录和退出；
- 不创建 Store、不执行 migration、不创建空数据库。

对于 recovery marker：

- 显示“上一次恢复未完成”及 marker、staging、corrupt bundle、source backup 路径；
- 不把生产 DB 缺失显示为首次安装；
- 第一版不自动续跑，用户只能进入明确的恢复/错误处理流程或退出；
- 在最终 health check 成功前不允许删除 marker。

## 13.1 DatabaseRecoveryDialog contract

`DatabaseRecoveryDialog` 是 `MainWindow` 创建前的 startup Window，不是业务页子窗口：

- `WindowStartupLocation=CenterScreen`；
- `ShowInTaskbar=True`；
- 不设置 `Owner=MainWindow`，不依赖 `CenterOwner`；
- `WindowStyle=None`、`ResizeMode=NoResize`，复用现有 `Colors.xaml`、`Typography.xaml`、
  `Cards.xaml` 工业深色资源。

Dialog 只消费 `DatabaseStartupDecision`：

```csharp
public enum DatabaseRecoveryDialogAction
{
    Recover,
    ResumeRecovery,
    Retry,
    OpenDataDirectory,
    Exit
}

public sealed class DatabaseRecoveryDialogResult
{
    public DatabaseRecoveryDialogAction Action { get; }
    public SqliteBackupCandidate? Candidate { get; }
}
```

默认 `Result.Action` 必须是 `Exit`。X、Alt+F4、系统关闭和未选择动作关闭窗口都返回
`Exit`。Retry、OpenDataDirectory、Exit 只返回动作，不自行重新 Inspect、扫描目录、打开
目录或退出应用。Recover 只返回用户从 `decision.Candidates` 选中的 candidate；默认选择
Gate 已按新到旧排序列表的第一项。ResumeRecovery 的 Candidate 必须为 `null`。

`Corrupt` 只显示 Gate 提供的候选和健康摘要；无候选时隐藏或禁用 Recover。`Unavailable`
只显示 ErrorType、ErrorCode/ErrorCodeName、ErrorMessage、Retry、打开目录和退出。
`UnsupportedSchema` 显示 `decision.Health.SchemaVersion` 与公开的
`SqlitePassageRecordStore.CurrentSchemaVersion`，不在 UI 复制版本常量，也不提供恢复动作。
`InterruptedRecovery` 显示 marker 的 source backup、corrupt bundle、staging、startedAt 和
manifest hash，但不在 UI 重新检查这些路径；真正 Resume 入口再次验证。`RecoveryStateError`
显示 `ErrorMessage`，只允许 Retry、打开目录和退出。

Recover/ResumeRecovery 附近只显示提示“执行恢复前需要管理员验证”。Task 6 不打开
`AdminPasswordDialog`，不调用 `AdminModeService`，不执行认证；Task 7 的 App orchestration
在调用 `Recover` 或 `ResumeInterruptedRecovery` 前检查 `AdminModeService.IsAdmin`，未验证
时先显示现有管理员密码窗口，验证失败或取消不得调用 recovery service。

## 14. 组件边界

### SqliteDatabaseHealthChecker

只负责 SQLite 健康检查和结构化结果，不负责 migration、不创建 Store、不显示 UI。

### SqliteBackupService

负责 Online Backup、临时文件、备份验证、正式文件提升、健康候选扫描和 retention。它不启动或停止 RFID。

### SqliteRecoveryService

负责 staging、候选复核后的恢复、corrupt bundle、生产替换和最终健康检查。它不创建 MainWindow，不启动 YardCommunicationManager。

### DatabaseMaintenanceCoordinator

负责正常运行期间的 startup catch-up、每日 02:00 调度、备份串行化、可取消停止和日志协调。它不把备份失败转换为 RFID 故障。构造函数必须接收
`ISqliteDatabaseHealthChecker`。进入共享 `SemaphoreSlim` 后，Coordinator 调用
`ScanCandidates` 找到当天全部正式 candidate，再对每个调用
`Inspect(candidate.Path, SqliteInspectionMode.FullValidation)`；只有当前 `Healthy`
的 candidate 才能抑制新备份。`Corrupt`、`Unavailable` 和 `UnsupportedSchema` 的今日
candidate 不计为已有健康备份；多个 candidate 中任意一个当前 Healthy 即可 skip，
不能只按最新文件名决定。

### SqlitePassageRecordStore

继续专注：

- CRUD。
- 现有 schema version check。
- 现有 v1/v2/v3 migration。
- 现有 Passage、PendingClear、报警确认/恢复语义。

健康检查和恢复决策不得塞进 Store 构造函数，也不得让 Store 自己弹 Dialog。

## 15. MainWindow 与 App 的所有权

数据库基础设施的唯一 owner 是 `App`。`MainWindow` 只使用依赖，不拥有、不 Dispose
数据库基础设施。具体 ownership 为：

- `App` 创建并持有 `SqlitePassageRecordStore`；
- `App` 创建并持有 `DatabaseMaintenanceCoordinator`；
- `App` 负责二者的退出顺序和最终 Dispose；
- `MainWindow` 接收 Store 或受控启动上下文并使用它，但不能再自行创建 Store，
  也不能创建第二个 coordinator。

目标结构：

```text
App.OnStartup
→ startup gate / recovery decision
→ 成功后 App 创建 SqlitePassageRecordStore
→ App 创建 DatabaseMaintenanceCoordinator
→ App 将 Store / coordinator 或等价受控依赖传给 MainWindow
→ MainWindow LoadProject
→ 恢复 PendingClear / 未确认报警
→ YardCommunicationManager Start
```

由于 recovery dialog 在 `MainWindow` 之前显示，App startup gate 阶段必须使用
`ShutdownMode.OnExplicitShutdown`（或等价且可测试的实现），不能让 startup dialog 被 WPF
自动当作最终 `Application.MainWindow`。只有正常业务 `MainWindow` 创建并赋值给
`Application.MainWindow` 后，才切换回 `ShutdownMode.OnMainWindowClose`。Retry、打开目录和
Exit 的结果由 App 处理，关闭 dialog 本身不能意外结束或绕过 startup 决策流程。

Recover/ResumeRecovery 是 destructive operation。App 在调用 recovery service 前检查
`AdminModeService.IsAdmin`；未验证时显示现有 `AdminPasswordDialog`，验证失败或取消不得
调用 `Recover` 或 `ResumeInterruptedRecovery`，已处于管理员模式时不重复弹窗。

MainWindow 不再负责决定数据库是否损坏，也不应在构造早期自行打开未经门禁的生产数据库。

完整退出顺序必须是：

```text
停止 YardCommunicationManager / runtime（确认 StopAllAsync 完成）
→ Dispose YardCommunicationManager
→ Acceptance final snapshot
→ Raw Packet Black Box drain / Dispose
→ DatabaseMaintenanceCoordinator.StopAsync()
→ 等待正在运行的 backup 安全结束
→ Dispose DatabaseMaintenanceCoordinator
→ Dispose SqlitePassageRecordStore / DB infrastructure
→ application exit
```

`StopAsync` 必须等待或安全取消 in-flight backup，不能在 backup 仍使用数据库时
Dispose Store。任何退出路径都不得让 MainWindow 和 App 同时 Dispose Store，也不得
让 backup scheduler 在 Store Dispose 后继续运行。MainWindow 在 manager 停止失败时
不得继续 Black Box 或数据库释放，必须保留 manager 以允许用户重试；不能用 `finally`
强制绕过该边界。本设计不改变报警恢复、Raw Packet Black Box 或 560/620 Context 隔离。

App 的 `StopDatabaseInfrastructureAsync()` 在独立同步锁内缓存 shutdown Task：并发或
进行中的调用共享同一个 Task，成功完成后重复调用保持幂等；如果该 Task faulted 或
cancelled，下一次调用必须创建新的 shutdown Task，不能让一次失败永久毒化后续重试。
每次 shutdown 仍固定执行 `Coordinator.StopAsync()` → coordinator Dispose → Store
Dispose；Coordinator 停止失败时不得 Dispose Store。

## 16. 正常运行期维护

维护协调器在业务运行后计算下一次本地 02:00，并通过可取消的单次等待调度备份。到点后：

```text
检查当天正式健康备份
→ 已存在：skip
→ 不存在：Online Backup + 验证
→ 成功：执行 retention
→ 失败：记录日志，不影响 RFID
```

备份和 retention 不应在 UDP 线程、poller 线程或 WPF UI 线程执行阻塞磁盘操作。UI 只读取维护状态快照或接收日志状态，不直接持有备份锁。

## 17. Acceptance 模式

现有 Acceptance 已使用独立数据库路径、运行时状态路径和日志目录。

第一版处理规则：

- Acceptance 使用自己的 `DatabasePath`。
- 不创建生产 `Backups/SQLite`。
- 不创建生产 `Data/Corrupt`。
- 不启动生产备份 scheduler。
- 不弹生产数据库 recovery UI。
- 现有 8 个场景继续通过独立数据库验证 RFID 业务。

SQLite backup / recovery 本身通过 Infrastructure 层的隔离测试验证，不把生产维护任务混入 Acceptance 流程。

## 18. 日志要求

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

## 19. 失败安全与并发约束

- 启动门禁是唯一允许决定“是否创建业务运行环境”的边界。
- `Unavailable` 只允许重试、打开数据目录或退出，永远不进入 recovery candidate 流程。
- `UnsupportedSchema` 只允许升级提示、重试、打开数据目录或退出，永远不进入 recovery
  candidate 流程，也不创建 Store。
- 恢复操作期间禁止创建 Store 和启动通信管理器。
- 日常备份与 02:00 调度共享一个串行互斥，不允许 startup backup 与定时 backup 并发。
- 备份失败只影响维护状态，不改变 `YardCommunicationManager`、`RfidRuntimeCoordinator` 或报警状态。
- 破坏性替换前不修改生产 `.db`、WAL、SHM；替换后以完整 corrupt bundle 保证原始数据可追溯。
- final health check 失败时不启动业务，保留 corrupt bundle、recovery marker 和失败证据。
- 生产数据库连接关闭后才允许保存 corrupt bundle 和替换文件。
- recovery marker 写入并持久化前，不允许执行 sidecar 删除、生产文件移动或生产文件替换。
- marker 存在时禁止把缺失的生产 DB 当成首次安装，也禁止忽略 marker 直接 Healthy 启动。
- schema migration 仍由 Store 执行；已有健康库的 startup backup 必须先于 Store 构造。

## 20. 后续实现必须覆盖的测试契约

本设计阶段不添加测试代码。后续实现计划必须覆盖以下行为。

### HealthChecker

- 健康数据库。
- 无数据库文件。
- 无效/损坏数据库。
- quick check 失败。
- foreign key violation。
- integrity check 摘要可记录。
- locked/busy 数据库判定为 `Unavailable`，而不是 `Corrupt`。
- access denied 判定为 `Unavailable`。
- `Unavailable` 不进入 recovery candidate 流程。
- v0、v1、v2、v3 物理健康库分别返回对应 `SchemaVersion` 并保持 `Healthy`。
- `user_version` 超过当前 Store 支持版本返回 `UnsupportedSchema`，而不是 `Corrupt` 或
  `Unavailable`。
- 物理 corruption 与后续 unavailable 错误同时存在时最终状态仍为 `Corrupt`。
- `FullValidation` 同样拒绝超前 schema 版本。
- 生产数据库已有 WAL 时，inspection 能读到已提交数据。
- backup/staging inspection 不执行 WAL pragma、不产生 WAL/SHM、不修改文件。

### Backup

- Online Backup 内容完整。
- 正在 WAL 运行的数据库备份一致。
- 验证前只存在 `.tmp.db`。
- 验证失败的临时文件不会变成正式备份。
- 每天最多一份正式备份。
- 今日 formal candidate 必须经过本次 `FullValidation`；`Corrupt`、`Unavailable`、
  `UnsupportedSchema` 不能 suppress 新备份；多个今日 candidate 中任一当前 Healthy
  即可 suppress。
- startup backup 与 02:00 backup 串行化。
- 14 天边界正确。
- 备份失败不会触发破坏性 retention。

### Recovery

- 选择最新健康备份。
- 只按规范文件名中的本地时间排序；异常命名文件和单纯 LastWriteTime 不得决定候选顺序。
- 最新候选损坏时回退到下一个健康候选。
- staging 验证通过前生产库不改变。
- 原始 `.db` 被保存。
- 已存在的 WAL/SHM 被保存。
- 非文件 WAL/SHM 路径不能被 `File.Exists` 当作缺失；只有明确 not-found 才可缺省，
  其它访问错误必须在 marker 前失败。
- candidate/staging 验证使用非破坏性的 Inspection Connection。
- recovery marker 至少包含 source backup、corrupt bundle、staging 和 started timestamp。
- marker + 缺失生产 DB 永远不创建空库。
- marker + 仍存在生产 DB 也阻止普通启动。
- corrupt bundle 完成后、sidecar 移除后、生产替换中断后，marker 和证据仍保留。
- marker 只在最终 health check 成功后删除。
- marker JSON contract 包含 `RecoveryStarted`、`SourceBackupPath`、`CorruptBundlePath`、
  `StagingPath`、`StartedAt`、`BundleManifestSha256`；Resume 必须校验 manifest hash 和
  evidence set。
- 恢复后的生产库再次验证。
- 恢复失败不启动 runtime。
- 无健康备份不创建空数据库。

### Startup ordering

- health gate 发生在 `SqlitePassageRecordStore` 构造前。
- Store 构造发生在 MainWindow 业务运行环境创建前。
- 损坏 DB 阻止 MainWindow / RFID runtime 启动。

### Shutdown / ownership

- App 是 Store 和 DatabaseMaintenanceCoordinator 的唯一 owner。
- shutdown 等待或安全终止 in-flight backup 后才 Dispose Store。
- coordinator 停止后不再启动新的 backup。
- 不发生 Store 双重 Dispose 或 backup/shutdown race。

### Regression

- 现有 v1/v2/v3 migration。
- 报警确认/恢复。
- PendingClear 启动恢复。
- Raw Packet Black Box。
- Core。
- Infrastructure。
- Acceptance 8/8。

## 21. 现有能力保持不变

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

## 22. 待 implementation 阶段确认的边界

以下是设计约束，而不是本阶段的实现任务：

1. `MainWindow` 最终采用 Store 构造函数注入、启动上下文对象，还是等价的受控工厂，需要在不扩大 UI 重构范围的前提下确定。
2. WPF recovery Dialog 的样式应复用现有深色工业 UI 和启动级 modal 样式，不新增数据库管理页面。
3. SQLite Online Backup API 的具体 overload、连接生命周期和 atomic rename 实现需要通过 Infrastructure 测试固定。
4. stale `.tmp.db` 的时间阈值需要在实现阶段以可注入时钟和明确默认值确定，不能把临时文件误删为正式证据。

## 23. 当前设计中已发现的代码冲突

已确认的冲突只有启动所有权和 Store 初始化时机：

- 当前 `App.OnStartup` 直接创建 MainWindow；设计要求先执行启动门禁。
- 当前 `MainWindow` 构造函数直接创建 `SqlitePassageRecordStore`；设计要求 Store 创建推迟到健康检查、备份/恢复决策之后。
- 当前 Store 构造函数同时承担目录创建、连接打开和 schema migration；设计要求健康检查器不调用这些业务初始化路径。
- 当前没有生产级备份 scheduler、recovery Dialog 或 corrupt bundle 流程；这些是新增边界，不应塞进现有 Store。

未发现需要改变的 SQLite pragma、schema、RFID 协议、报警生命周期、Raw Packet Black Box 或双站场通信设计。

Acceptance 的独立 `DatabasePath` / `LogDirectory` 是现有特殊路径，需要在实现阶段显式旁路生产维护目录；否则会违反“不污染真实 Backups/Data/Corrupt”的要求。

本设计不改变这些现有行为，也不在本阶段修改代码。
