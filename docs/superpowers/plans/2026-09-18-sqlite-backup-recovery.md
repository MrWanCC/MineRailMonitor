# SQLite Backup / Integrity / Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变现有 SQLite schema、RFID 协议、报警语义和双站场通信的前提下，为 MineRailMonitor 增加可验证的 SQLite 备份、健康检查、恢复和启动门禁。

**Architecture:** 健康检查、备份、恢复和维护调度全部位于 Infrastructure Persistence 边界，`SqlitePassageRecordStore` 继续只负责 CRUD、schema 和 migration。`App` 在创建 `MainWindow` 前完成数据库门禁，并作为 Store 与维护协调器的唯一 owner；`MainWindow` 只接收已通过门禁的 Store。生产 Business Connection 与只读 Inspection Connection 完全分离。

**Tech Stack:** C# / .NET Framework 4.8、WPF、System.Data.SQLite.Core 1.0.118、xUnit 2.9.3、Microsoft.NET.Test.Sdk 17.14.1、现有 `IRfidTimeProvider`、`ILogger` 和 Acceptance PowerShell 入口。

**Spec:** `docs/superpowers/specs/2026-09-18-sqlite-backup-recovery-design.md`

## Global Constraints

- 业务连接继续使用 `journal_mode=WAL`、`synchronous=FULL`、`foreign_keys=ON`、`busy_timeout=5000`。
- Inspection Connection 不执行 `journal_mode=WAL`，不修改 journal mode、`user_version`、schema，不执行 migration。
- 健康状态必须区分 `Missing`、`Healthy`、`Corrupt`、`Unavailable`。
- `Unavailable` 阻止业务启动，但不进入 recovery candidate 流程，只允许重试、打开数据目录、退出。
- destructive replacement 前必须完成 staging 验证、corrupt bundle 保存和 recovery marker 持久化。
- destructive replacement 后原始生产数据安全由完整 corrupt bundle 提供，不声称生产路径永远不被覆盖。
- final health check 失败时不创建 Store、MainWindow 正常业务或空数据库，不启动 RFID，并保留 marker、bundle 和失败证据。
- App 是 `SqlitePassageRecordStore` 和 `DatabaseMaintenanceCoordinator` 的唯一 owner。
- Acceptance 使用独立 `DatabasePath` / `LogDirectory`，旁路生产 `Backups/`、`Data/Corrupt/`、recovery UI 和 production scheduler。
- 不修改 Passage schema v3、v1/v2/v3 migration、RFID 40 Byte 协议、识别逻辑、报警恢复语义、Raw Packet Black Box 或 560/620 Context 隔离。
- `TreatWarningsAsErrors=true` 保持有效；所有 GREEN 和回归命令必须保持 0 warnings、0 errors、0 skipped。

---

## Current Code Map

实施者开始前必须以当前 `main` 为准，不假定设计文档中的组件已经存在。

- `src/MineRailMonitor/App.xaml.cs:12-58`：构造函数解析 Acceptance 参数并创建 Logger；当前 `OnStartup` 直接 `new MainWindow()`；当前 `OnExit` 不拥有或停止 Store。
- `src/MineRailMonitor/MainWindow.xaml.cs:30-108`：构造函数创建 `SqlitePassageRecordStore`，创建 Raw Packet Black Box Writer 并注册窗口事件。
- `src/MineRailMonitor/MainWindow.xaml.cs:625-712`：`RecreateYardCommunicationManagerAsync` 负责创建/启动 Yard manager，并从 Store 恢复 PendingClear 与未确认报警。
- `src/MineRailMonitor/MainWindow.xaml.cs:841-858`：`OnWindowClosed` 当前 Dispose manager、Black Box、Acceptance writer 和 Store；需要改为 manager/runtime 停止后由 App 停止维护器并 Dispose Store。
- `src/MineRailMonitor/MainWindow.xaml.cs:914-944`：已有异步 `OnWindowClosing` 和 `_allowWindowClose`，可复用以等待安全关闭。
- `src/MineRailMonitor.Infrastructure/Persistence/SqlitePassageRecordStore.cs:7-35`：Store 规范化路径、创建目录、打开业务连接并初始化 schema；构造函数不能被健康检查和恢复流程调用。
- `src/MineRailMonitor.Infrastructure/Persistence/SqlitePassageRecordStore.cs:304-312`：每次业务操作打开连接并执行现有四条 PRAGMA；`Dispose()` 当前为空实现。
- `src/MineRailMonitor.Infrastructure/Persistence/SqlitePassageRecordStore.cs:314-415`：schema v1/v2/v3 初始化和 migration，必须保持原逻辑。
- `src/MineRailMonitor.Infrastructure/MineRailMonitor.Infrastructure.csproj`：已经引用 `System.Data.SQLite.Core 1.0.118`、Newtonsoft.Json、System.Text.Json，无需引入大型第三方库。
- `tests/MineRailMonitor.Infrastructure.Tests/SqlitePassageRecordStoreTests.cs`：已有 SQLite 临时目录、schema v3、WAL、migration、报警字段测试模式。
- `tests/MineRailMonitor.Infrastructure.Tests/Phase32SqliteIntegrationTests.cs`：已有 Infrastructure 级 Store/runtime 集成测试模式。
- `tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj`：已引用 Infrastructure、Core、SQLite 和 xUnit，SDK 默认编译新测试文件，无需新增 ProjectReference。
- `tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj`：只引用 Core 和 Simulator；现有 WPF 相关测试通过读取 XAML/C# 文本做 markup contract，不直接引用 WPF 主程序。
- `tests/MineRailMonitor.Core.Tests/*MarkupTests.cs`：新增 App/MainWindow ownership 与启动顺序 contract 时沿用文本断言方式。
- `scripts/run-rfid-acceptance.ps1`：先 build、Core、Infrastructure，再启动双站场 Simulator 和 Acceptance 上位机；上位机通过 `--acceptance --database --log-dir` 等参数运行。

## Shared Contracts Used by All Tasks

以下类型名和接口在任务之间固定，不在后续任务中重新命名：

```csharp
public enum SqliteDatabaseHealthState
{
    Missing,
    Healthy,
    Corrupt,
    Unavailable
}

public enum SqliteInspectionMode
{
    StartupFast,
    FullValidation
}

public interface ISqliteDatabaseHealthChecker
{
    SqliteDatabaseHealthResult Inspect(
        string databasePath,
        SqliteInspectionMode mode);
}

public sealed class SqliteDatabaseHealthResult
{
    public string DatabasePath { get; }
    public SqliteDatabaseHealthState State { get; }
    public DateTimeOffset CheckedAt { get; }
    public bool QuickCheckPassed { get; }
    public string QuickCheckSummary { get; }
    public bool IntegrityCheckExecuted { get; }
    public bool IntegrityCheckPassed { get; }
    public string IntegrityCheckSummary { get; }
    public bool ForeignKeyCheckPassed { get; }
    public string ForeignKeyCheckSummary { get; }
    public string? ErrorType { get; }
    public string? ErrorMessage { get; }
}

public sealed class SqliteBackupCandidate
{
    public string Path { get; }
    public DateTimeOffset LocalTimestamp { get; }
}

public sealed class SqliteBackupResult
{
    public bool Succeeded { get; }
    public string? FinalPath { get; }
    public string? FailureReason { get; }
}

public sealed class SqliteRecoveryMarker
{
    public string SourceBackupPath { get; }
    public string CorruptBundlePath { get; }
    public string StagingPath { get; }
    public DateTimeOffset StartedAt { get; }
}

public sealed class SqliteRecoveryResult
{
    public bool Succeeded { get; }
    public string? CorruptBundlePath { get; }
    public string? FailureReason { get; }
}

public enum DatabaseStartupDecisionKind
{
    CreateNew,
    StartHealthy,
    RecoverCorrupt,
    Unavailable,
    InterruptedRecovery
}

public sealed class DatabaseStartupDecision
{
    public DatabaseStartupDecisionKind Kind { get; }
    public SqliteDatabaseHealthResult Health { get; }
    public IReadOnlyList<SqliteBackupCandidate> Candidates { get; }
    public SqliteRecoveryMarker? RecoveryMarker { get; }
}

public interface ISqliteBackupRunner
{
    Task<SqliteBackupResult> CreateValidatedBackupAsync(
        string productionDatabasePath,
        string backupRootDirectory,
        DateTimeOffset localNow,
        CancellationToken cancellationToken);
}
```

`SqliteDatabaseHealthChecker`, `SqliteBackupService`、`SqliteRecoveryService` 和
`DatabaseStartupGate` 均放在 `MineRailMonitor.Infrastructure.Persistence`。所有路径
使用 `Path.GetFullPath`，所有时间从注入的 `IRfidTimeProvider.UtcNow` 转换为本地时间。

`SqliteInspectionMode` 是所有 health-check 调用的强制选择：

- `StartupFast` 执行 `quick_check` 和 `foreign_key_check`；只有 quick/FK 出现异常时
  才追加 `integrity_check`，用于正常生产 startup gate。
- `FullValidation` 必须执行 `integrity_check` 和 `foreign_key_check`，不得因为
  `quick_check=ok` 跳过 `integrity_check`；用于 backup tmp、recovery candidate、
  staging 以及 restored production 的 final validation。

正式 `.db` backup 在 promotion 前必须已经通过 `FullValidation`。

## Task 1: SQLite Health Model and Inspection Connection

**Files:**

- Create: `src/MineRailMonitor.Infrastructure/Persistence/SqliteDatabaseHealthState.cs`
- Create: `src/MineRailMonitor.Infrastructure/Persistence/SqliteDatabaseHealthResult.cs`
- Create: `src/MineRailMonitor.Infrastructure/Persistence/SqliteDatabaseHealthChecker.cs`
- Create: `tests/MineRailMonitor.Infrastructure.Tests/SqliteDatabaseHealthCheckerTests.cs`
- Modify: none

**Interfaces:**

- Consumes: `IRfidTimeProvider.UtcNow`、`System.Data.SQLite.SQLiteConnection`、`ILogger`。
- Produces: `SqliteDatabaseHealthState`、`SqliteDatabaseHealthResult`、
  `SqliteInspectionMode`、`ISqliteDatabaseHealthChecker.Inspect(string, SqliteInspectionMode)`。

- [ ] **Step 1: Write the failing tests**

在 `SqliteDatabaseHealthCheckerTests.cs` 添加以下测试，使用文件级临时目录和现有
`SqlitePassageRecordStore` 创建有效 schema；测试文件内定义 `TestLogger` 和
`FixedRfidTimeProvider`：

```csharp
[Fact]
public void Missing_database_is_reported_as_missing()
{
    using var directory = new TemporaryDirectory();
    var result = new SqliteDatabaseHealthChecker(new FixedRfidTimeProvider(), new TestLogger()).Inspect(
        Path.Combine(directory.Path, "MineRailMonitor.db"), SqliteInspectionMode.StartupFast);

    Assert.Equal(SqliteDatabaseHealthState.Missing, result.State);
}

[Fact]
public void Healthy_database_reports_quick_integrity_and_foreign_key_results()
{
    using var database = new TemporaryDatabase();
    var result = new SqliteDatabaseHealthChecker(new FixedRfidTimeProvider(), new TestLogger()).Inspect(
        database.Path, SqliteInspectionMode.StartupFast);

    Assert.Equal(SqliteDatabaseHealthState.Healthy, result.State);
    Assert.True(result.QuickCheckPassed);
    Assert.True(result.ForeignKeyCheckPassed);
}

[Fact]
public void Full_validation_always_runs_integrity_check()
{
    using var database = new TemporaryDatabase();
    var result = new SqliteDatabaseHealthChecker(new FixedRfidTimeProvider(), new TestLogger()).Inspect(
        database.Path, SqliteInspectionMode.FullValidation);

    Assert.Equal(SqliteDatabaseHealthState.Healthy, result.State);
    Assert.True(result.IntegrityCheckExecuted);
    Assert.True(result.IntegrityCheckPassed);
    Assert.True(result.ForeignKeyCheckPassed);
}

[Fact]
public void Locked_database_is_unavailable_not_corrupt()
{
    using var database = new TemporaryDatabase();
    using var exclusiveLock = database.OpenExclusiveTransaction();

    var result = new SqliteDatabaseHealthChecker(new FixedRfidTimeProvider(), new TestLogger()).Inspect(
        database.Path, SqliteInspectionMode.StartupFast);

    Assert.Equal(SqliteDatabaseHealthState.Unavailable, result.State);
    Assert.NotEqual(SqliteDatabaseHealthState.Corrupt, result.State);
}

[Fact]
public void Corrupt_database_is_corrupt_after_integrity_confirmation()
{
    using var database = TemporaryDatabase.CreateCorruptPageFile();

    var result = new SqliteDatabaseHealthChecker(new FixedRfidTimeProvider(), new TestLogger()).Inspect(
        database.Path, SqliteInspectionMode.StartupFast);

    Assert.Equal(SqliteDatabaseHealthState.Corrupt, result.State);
    Assert.True(result.IntegrityCheckExecuted);
    Assert.False(result.IntegrityCheckPassed);
}

[Fact]
public void Inspection_reads_committed_wal_data_without_creating_sidecars_for_backup_copy()
{
    using var database = new TemporaryDatabase();
    database.EnableWalAndCommitKnownRow();
    var before = database.SnapshotFiles();

    var result = new SqliteDatabaseHealthChecker(new FixedRfidTimeProvider(), new TestLogger()).Inspect(
        database.Path, SqliteInspectionMode.StartupFast);

    Assert.Equal(SqliteDatabaseHealthState.Healthy, result.State);
    Assert.Equal(before, database.SnapshotFiles());
}
```

`TemporaryDatabase` 必须公开 `Path`、`OpenExclusiveTransaction()`、
`EnableWalAndCommitKnownRow()` 和 `SnapshotFiles()`；`FixedRfidTimeProvider` 返回固定
的 UTC 时间。另加 `Access_denied_is_unavailable`：通过可选的 inspection connection
factory 抛出 `UnauthorizedAccessException`，断言 access-denied 分类仍为 `Unavailable`，
不得把权限异常映射为 `Corrupt`。

- [ ] **Step 2: Run RED**

Run:

```powershell
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --filter "FullyQualifiedName~SqliteDatabaseHealthCheckerTests"
```

Expected: FAIL，原因是 `SqliteDatabaseHealthChecker`、状态枚举和结果类型尚未定义。

- [ ] **Step 3: Implement the minimum health checker**

实现 `SqliteDatabaseHealthChecker`：

```csharp
public sealed class SqliteDatabaseHealthChecker : ISqliteDatabaseHealthChecker
{
    public SqliteDatabaseHealthChecker(
        IRfidTimeProvider timeProvider,
        ILogger logger,
        Func<string, SQLiteConnection>? inspectionConnectionFactory = null);

    public SqliteDatabaseHealthResult Inspect(
        string databasePath,
        SqliteInspectionMode mode);
}
```

具体规则：

1. 检查 recovery marker 由 `DatabaseStartupGate` 负责；HealthChecker 单独看到不存在
   的主库时只返回 `Missing`。
2. 主库存在时使用独立 Inspection Connection，连接字符串只包含绝对 `Data Source`、
   `Version=3` 和只读/查询策略；绝不执行业务四条 PRAGMA 中的 `journal_mode=WAL`，
   也不调用 Store 构造函数。
3. `StartupFast` 执行 `PRAGMA quick_check;` 和 `PRAGMA foreign_key_check;`；返回 `ok`
   且无外键行时返回 `Healthy`。quick/FK 异常时再执行 `PRAGMA integrity_check;`。
4. `FullValidation` 无条件执行 `PRAGMA integrity_check;` 和
   `PRAGMA foreign_key_check;`，即使 quick check 为 `ok` 也不能跳过 integrity check；
   只有得到明确完整性证据才返回 `Corrupt`。
5. locked、busy timeout、access denied、sharing violation、路径/磁盘不可用和无法
   确定内容是否损坏的 SQLite/IO 异常返回 `Unavailable`，记录 `ErrorType`、错误码
   和摘要，不进入 recovery。
6. Inspection Connection 只读查询，关闭连接后不产生 backup/staging 的 `-wal`、
   `-shm`，并且生产 DB inspection 能看到已有 WAL 的已提交数据。

- [ ] **Step 4: Run GREEN and regression**

Run:

```powershell
dotnet build MineRailMonitor.sln -c Debug
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~SqliteDatabaseHealthCheckerTests"
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build
```

Expected: 新增健康检查测试和现有 Infrastructure tests 全部通过，0 warnings、0 errors、0 skipped。

- [ ] **Step 5: Commit**

```powershell
git add src/MineRailMonitor.Infrastructure/Persistence/SqliteDatabaseHealthState.cs src/MineRailMonitor.Infrastructure/Persistence/SqliteDatabaseHealthResult.cs src/MineRailMonitor.Infrastructure/Persistence/SqliteDatabaseHealthChecker.cs tests/MineRailMonitor.Infrastructure.Tests/SqliteDatabaseHealthCheckerTests.cs
git commit -m "feat: add sqlite health inspection model"
```

## Task 2: SqliteBackupService and Strict Candidate Scanning

**Files:**

- Create: `src/MineRailMonitor.Infrastructure/Persistence/SqliteBackupCandidate.cs`
- Create: `src/MineRailMonitor.Infrastructure/Persistence/SqliteBackupResult.cs`
- Create: `src/MineRailMonitor.Infrastructure/Persistence/SqliteBackupService.cs`
- Create: `tests/MineRailMonitor.Infrastructure.Tests/SqliteBackupServiceTests.cs`
- Modify: none

**Interfaces:**

- Consumes: `ISqliteDatabaseHealthChecker`、`System.Data.SQLite` Online Backup API、`ILogger`。
- Produces: `ISqliteBackupRunner.CreateValidatedBackupAsync(...)`、
  `SqliteBackupService.ScanCandidates(string)`。

`ScanCandidates` 只负责严格文件名/日期目录筛选和按规范文件名时间排序，不宣称候选内容
健康；启动 gate 会在展示候选前逐个执行 `FullValidation`。

- [ ] **Step 1: Write the failing tests**

添加以下测试：

```csharp
[Fact]
public async Task CreateValidatedBackup_promotes_only_after_validation()
{
    using var database = new TemporaryDatabaseWithKnownRow();
    using var directories = new BackupDirectories();
    var result = await CreateService().CreateValidatedBackupAsync(
        database.Path, directories.Root, LocalTime, CancellationToken.None);

    Assert.True(result.Succeeded);
    Assert.Equal(
        Path.Combine(directories.Root, "2026-09-18", "MineRailMonitor_20260918_020000.db"),
        result.FinalPath);
    Assert.Empty(Directory.GetFiles(Path.Combine(directories.Root, "2026-09-18"), "*.tmp.db"));
}

[Fact]
public async Task CreateValidatedBackup_preserves_committed_wal_rows()
{
    using var database = new TemporaryDatabaseWithKnownRow();
    database.EnableWalAndCommitKnownRow();
    using var directories = new BackupDirectories();

    var result = await CreateService().CreateValidatedBackupAsync(
        database.Path, directories.Root, LocalTime, CancellationToken.None);

    Assert.True(result.Succeeded);
    Assert.True(ContainsKnownRow(result.FinalPath!));
}

[Fact]
public void ScanCandidates_orders_by_filename_timestamp_and_ignores_malformed_names()
{
    using var directories = CandidateDirectory.Create(
        "MineRailMonitor_20260918_020000.db",
        "MineRailMonitor_20260917_230000.db",
        "copy-of-MineRailMonitor.db");
    File.SetLastWriteTimeUtc(directories.Path("2026-09-17", "MineRailMonitor_20260917_230000.db"), DateTime.UtcNow.AddDays(2));

    var candidates = CreateService().ScanCandidates(directories.Root);

    Assert.Equal(2, candidates.Count);
    Assert.EndsWith("MineRailMonitor_20260918_020000.db", candidates[0].Path);
    Assert.DoesNotContain(candidates, item => item.Path.EndsWith("copy-of-MineRailMonitor.db"));
}
```

另加 `Formal_backup_promotion_requires_full_validation`：注入 recording health checker，
断言 tmp 文件 promotion 前收到的 mode 必须是 `SqliteInspectionMode.FullValidation`，并且
只有 `Healthy` 才生成正式 `.db`。

另加 `CreateValidatedBackup_validation_failure_does_not_promote_tmp`：注入一个返回
`Corrupt` 的 `ISqliteDatabaseHealthChecker`，断言没有正式 `.db`，临时文件按失败规则
删除或保留为明确 `.tmp.db` 证据，且不删除旧健康备份。

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --filter "FullyQualifiedName~SqliteBackupServiceTests"
```

Expected: FAIL，原因是 BackupService、candidate、result 和 runner interface 尚未存在。

- [ ] **Step 3: Implement the minimum backup service**

实现严格文件约束：

- 文件名只能是 `MineRailMonitor_yyyyMMdd_HHmmss.db`；临时文件为同目录同时间戳的
  `MineRailMonitor_yyyyMMdd_HHmmss.tmp.db`。
- source connection 通过 SQLite Online Backup API 复制一致性内容，不调用
  `SqlitePassageRecordStore`，不触发 schema migration。
- 复制目标在同一文件系统；先写 `.tmp.db`，调用
  `healthChecker.Inspect(tmpPath, SqliteInspectionMode.FullValidation)`，验证成功后再
  原子 rename/move 为正式 `.db`。
- 验证失败不得加入 candidate；失败路径不删除旧正式备份。
- `ScanCandidates` 只接受精确文件名和匹配的 `Backups/SQLite/yyyy-MM-dd/` 日期目录，
  按规范文件名解析出的本地时间降序排序；`LastWriteTime` 只能作为日志信息，不能参与
  主排序，也不能把异常命名文件猜测成最新备份。
- Inspection 验证不执行 WAL pragma、不修改文件、不产生 `.tmp.db-wal` 或
  `.tmp.db-shm`。

`SqliteBackupService` 构造函数固定为：

```csharp
public SqliteBackupService(
    ISqliteDatabaseHealthChecker healthChecker,
    ILogger logger);
```

- [ ] **Step 4: Run GREEN and regression**

```powershell
dotnet build MineRailMonitor.sln -c Debug
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~SqliteBackupServiceTests"
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build
```

Expected: BackupService tests和现有 Infrastructure tests全部通过，0 warnings、0 errors、0 skipped。

- [ ] **Step 5: Commit**

```powershell
git add src/MineRailMonitor.Infrastructure/Persistence/SqliteBackupCandidate.cs src/MineRailMonitor.Infrastructure/Persistence/SqliteBackupResult.cs src/MineRailMonitor.Infrastructure/Persistence/SqliteBackupService.cs tests/MineRailMonitor.Infrastructure.Tests/SqliteBackupServiceTests.cs
git commit -m "feat: add validated sqlite backup service"
```

## Task 3: Retention and Daily Backup Coordination

**Files:**

- Create: `src/MineRailMonitor.Infrastructure/Persistence/SqliteRetentionService.cs`
- Create: `src/MineRailMonitor.Infrastructure/Persistence/DatabaseMaintenanceCoordinator.cs`
- Create: `tests/MineRailMonitor.Infrastructure.Tests/SqliteRetentionServiceTests.cs`
- Create: `tests/MineRailMonitor.Infrastructure.Tests/DatabaseMaintenanceCoordinatorTests.cs`
- Modify: none

**Interfaces:**

- Consumes: `ISqliteBackupRunner`、`IRfidTimeProvider`、`ILogger`、`SqliteBackupService.ScanCandidates`。
- Produces:

```csharp
public sealed class SqliteRetentionService
{
    public SqliteRetentionService(int retentionDays, ILogger logger);
    public void Apply(string backupRootDirectory, DateTimeOffset localNow);
}

public interface IAsyncDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class DatabaseMaintenanceCoordinator : IDisposable
{
    public DatabaseMaintenanceCoordinator(
        string productionDatabasePath,
        string backupRootDirectory,
        ISqliteBackupRunner backupRunner,
        SqliteRetentionService retentionService,
        IRfidTimeProvider timeProvider,
        IAsyncDelay delay,
        ILogger logger);

    public Task<SqliteBackupResult> RunStartupCatchUpAsync(CancellationToken cancellationToken);
    public Task StartAsync(CancellationToken applicationStopping);
    public Task StopAsync();
    public void Dispose();
}
```

- [ ] **Step 1: Write the failing tests**

`SqliteRetentionServiceTests.cs` 添加：

- `Retention_keeps_today_and_previous_13_local_calendar_dates`：RetentionDays=14，
  保留 today、today-1、today-13，删除 today-14；非日期目录、其他文件和 `Data/Corrupt`
  不删除。
- `Retention_ignores_tmp_files_and_malformed_backup_names`：`.tmp.db` 和异常命名文件不
  计入健康备份日期，也不被当成正式候选。
- `Startup_cleanup_removes_only_application_tmp_older_than_24_hours`：使用注入时钟创建
  严格匹配 `MineRailMonitor_yyyyMMdd_HHmmss.tmp.db` 的 25 小时旧文件、24 小时边界文件、
  其它命名临时文件和未知目录文件；只有前者被删除。

`DatabaseMaintenanceCoordinatorTests.cs` 添加：

```csharp
[Fact]
public async Task Startup_catch_up_creates_at_most_one_backup_for_local_date()
{
    var runner = new RecordingBackupRunner();
    using var coordinator = CreateCoordinator(runner, LocalTime, new ImmediateAsyncDelay());

    await coordinator.RunStartupCatchUpAsync(CancellationToken.None);
    await coordinator.RunStartupCatchUpAsync(CancellationToken.None);

    Assert.Equal(1, runner.CallsFor(LocalTime.Date));
}

[Fact]
public async Task Failed_backup_does_not_run_destructive_retention()
{
    var runner = new RecordingBackupRunner { Result = FailedBackupResult };
    using var coordinator = CreateCoordinator(runner, LocalTime, new ImmediateAsyncDelay());
    CreateExpiredFormalBackup();

    await coordinator.RunStartupCatchUpAsync(CancellationToken.None);

    Assert.True(File.Exists(ExpiredFormalBackupPath));
    Assert.False(runner.RetentionWasCalled);
}

[Fact]
public async Task StopAsync_waits_for_in_flight_backup_before_returning()
{
    var runner = new BlockingBackupRunner();
    var delay = new ManualAsyncDelay();
    using var coordinator = CreateCoordinator(runner, LocalTime, delay);
    await coordinator.StartAsync(CancellationToken.None);
    await delay.WaitUntilCapturedAsync();
    delay.ReleaseNext();
    await runner.WaitUntilStartedAsync();

    var stop = coordinator.StopAsync();
    Assert.False(stop.IsCompleted);
    runner.Release();
    await stop;
}
```

另加 `Startup_and_scheduled_backup_share_one_serial_gate`：注入 `ManualAsyncDelay`，让
测试手工释放“等待到下一次 02:00”的 delay，再让 runner 第一次调用阻塞，同时触发
startup catch-up 和 scheduled tick，断言 runner 最大并发数为 1。

测试辅助类型固定为内存实现：`ManualAsyncDelay` 通过 `TaskCompletionSource` 暴露
`WaitUntilCapturedAsync()` 和 `ReleaseNext()`；测试不得使用 `Thread.Sleep`、真实时钟等待
或等待到现实中的 02:00。生产实现 `TaskAsyncDelay` 仅将 `DelayAsync` 委托给
`Task.Delay(delay, cancellationToken)`。

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --filter "FullyQualifiedName~SqliteRetentionServiceTests|FullyQualifiedName~DatabaseMaintenanceCoordinatorTests"
```

Expected: FAIL，原因是 retention service、coordinator 和 runner contracts 尚未实现。

- [ ] **Step 3: Implement retention and coordinator**

- `SqliteRetentionService` 按注入的本地日期只处理
  `Backups/SQLite/yyyy-MM-dd/` 日期目录；保留最近 14 个本地自然日，today 和 today-13
  均保留，today-14 才可删除；非日期目录、`Data/Corrupt` 和不属于本应用的文件不删除。
- 启动 catch-up 和每日 02:00 调度共享一个 `SemaphoreSlim(1, 1)`；进入锁后再次检查
  当天正式健康备份，保证一天最多一份。
- `RunStartupCatchUpAsync` 在 Store 创建前可运行，因为它只依赖数据库路径和 backup
  service；`StartAsync` 只启动后台 02:00 loop，不在 UI、UDP 或 poller 线程执行 IO。
- 只有 validated backup 成功后才调用 retention；失败 backup 不执行 destructive cleanup。
- 启动和日期轮转时只清理严格匹配
  `MineRailMonitor_yyyyMMdd_HHmmss.tmp.db` 且按注入时钟计算 age `> 24` 小时的临时文件；
  stale 默认阈值固定为 `TimeSpan.FromHours(24)`。未知临时文件、其它命名文件和恰好 24
  小时的文件都不删除，也不把任何临时文件变成正式候选。
- 调度 loop 通过注入的 `IAsyncDelay` 等待下一次本地 02:00；生产默认实现使用
  `Task.Delay`，测试实现必须可手工释放，不依赖真实时间。
- `StopAsync` 取消调度 loop，等待当前 runner 调用结束后返回；`Dispose` 只在
  `StopAsync` 完成后释放 semaphore/token source。

- [ ] **Step 4: Run GREEN and regression**

```powershell
dotnet build MineRailMonitor.sln -c Debug
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~SqliteRetentionServiceTests|FullyQualifiedName~DatabaseMaintenanceCoordinatorTests"
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build
```

Expected: 新增 retention/coordinator tests和现有 Infrastructure tests全部通过，0 warnings、0 errors、0 skipped。

- [ ] **Step 5: Commit**

```powershell
git add src/MineRailMonitor.Infrastructure/Persistence/SqliteRetentionService.cs src/MineRailMonitor.Infrastructure/Persistence/DatabaseMaintenanceCoordinator.cs tests/MineRailMonitor.Infrastructure.Tests/SqliteRetentionServiceTests.cs tests/MineRailMonitor.Infrastructure.Tests/DatabaseMaintenanceCoordinatorTests.cs
git commit -m "feat: coordinate sqlite retention and daily backups"
```

## Task 4: SqliteRecoveryService and Destructive Replacement Boundary

**Files:**

- Create: `src/MineRailMonitor.Infrastructure/Persistence/SqliteRecoveryPhase.cs`
- Create: `src/MineRailMonitor.Infrastructure/Persistence/SqliteRecoveryMarkerStore.cs`
- Create: `src/MineRailMonitor.Infrastructure/Persistence/SqliteRecoveryService.cs`
- Create: `tests/MineRailMonitor.Infrastructure.Tests/SqliteRecoveryServiceTests.cs`
- Modify: none

**Interfaces:**

- Consumes: `ISqliteDatabaseHealthChecker`、`SqliteInspectionMode.FullValidation`、
  `SqliteBackupCandidate`、`ILogger`、file system paths。
- Produces:

```csharp
public enum SqliteRecoveryPhase
{
    StagingValidated,
    CorruptBundleSaved,
    RecoveryMarkerPersisted,
    SidecarsRemoved,
    ProductionReplacementStarted,
    FinalHealthCheckCompleted
}

public sealed class SqliteRecoveryService
{
    public SqliteRecoveryService(
        ISqliteDatabaseHealthChecker healthChecker,
        string dataDirectory,
        ILogger logger,
        IRfidTimeProvider timeProvider,
        Action<SqliteRecoveryPhase>? phaseObserver = null);

    public SqliteRecoveryMarker? ReadMarker();
    public SqliteRecoveryResult Recover(string productionDatabasePath, SqliteBackupCandidate candidate);
}
```

- [ ] **Step 1: Write the failing tests**

添加以下行为测试：

- `Recover_validates_staging_before_touching_production`：source candidate 被复制到
  `Data/MineRailMonitor.restore.tmp.db`，staging health 失败时生产 `.db`、WAL、SHM
  字节内容和路径均不变。
- `Recover_saves_db_wal_shm_before_destructive_replacement`：准备生产 DB、实际存在的
  `-wal`、`-shm`，恢复后 `Data/Corrupt/<timestamp>/` 三个文件均存在且字节相同。
- `Recover_persists_marker_before_sidecar_removal`：通过 `phaseObserver` 记录顺序，断言
  `CorruptBundleSaved` → `RecoveryMarkerPersisted` → `SidecarsRemoved` →
  `ProductionReplacementStarted`。
- `Final_health_failure_keeps_bundle_marker_and_failure_evidence`：fake health checker
  按 source/staging=Healthy、final=Corrupt 返回；断言不创建 Store、不启动业务所需的
  `Succeeded=false`，并保留 bundle、marker、staging/最终失败文件证据。
- `Recover_removes_marker_only_after_final_health_succeeds`：final=Healthy 时 marker
  才消失；source backup sidecar 不被复制到生产目录。
- `Recover_revalidates_selected_candidate_with_full_validation`：即使 startup gate 已经
  验证过 candidate，恢复入口仍再次调用
  `healthChecker.Inspect(candidate.Path, SqliteInspectionMode.FullValidation)`，未通过时
  不进入 staging 或 destructive replacement。

测试使用 `TemporaryDirectory` 和 `FakeHealthChecker`，不通过 `Thread.Sleep` 制造时序；
`FakeHealthChecker` 每次 `Inspect` 按预先配置的路径队列返回确定结果。

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --filter "FullyQualifiedName~SqliteRecoveryServiceTests"
```

Expected: FAIL，原因是 recovery phase、marker store 和 service 尚未定义。

- [ ] **Step 3: Implement the minimum recovery service**

严格实现以下顺序：

```text
candidate revalidated with FullValidation
→ staging copied and validated
→ all existing production db/wal/shm copied to Data/Corrupt/<timestamp>/
→ marker atomically persisted to Data/.sqlite-recovery-in-progress
→ sidecars removed/moved only after marker exists
→ production database replaced from validated staging
→ final health check
→ marker deleted only on final Healthy
```

具体约束：

- source candidate 在恢复入口再次执行 `FullValidation`；candidate 失败时不触碰生产文件。
- corrupt bundle 只复制实际存在的 `.db`、`.db-wal`、`.db-shm`；任何必需复制失败都
  停止恢复，不开始 destructive replacement。
- marker 使用原子临时写入/rename，包含 `SourceBackupPath`、`CorruptBundlePath`、
  `StagingPath` 和 `StartedAt`；marker 写入失败时不移除 sidecar。
- replacement 前不修改生产路径；replacement 开始后原始数据安全由完整 bundle 提供，
  不声称原生产路径永远不变。
- final health 失败时不创建 Store、MainWindow 或空库，不启动 RFID；保留 marker、
  corrupt bundle 和失败证据，并返回 `SqliteRecoveryResult.Succeeded=false`。
- recovery 使用 `SqliteInspectionMode.FullValidation` 的 Inspection Connection 验证
  source/staging/final，不执行业务 PRAGMA、schema migration 或 user_version 修改。

- [ ] **Step 4: Run GREEN and regression**

```powershell
dotnet build MineRailMonitor.sln -c Debug
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~SqliteRecoveryServiceTests"
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build
```

Expected: recovery tests和现有 Infrastructure tests全部通过，0 warnings、0 errors、0 skipped。

- [ ] **Step 5: Commit**

```powershell
git add src/MineRailMonitor.Infrastructure/Persistence/SqliteRecoveryPhase.cs src/MineRailMonitor.Infrastructure/Persistence/SqliteRecoveryMarkerStore.cs src/MineRailMonitor.Infrastructure/Persistence/SqliteRecoveryService.cs tests/MineRailMonitor.Infrastructure.Tests/SqliteRecoveryServiceTests.cs
git commit -m "feat: add crash-safe sqlite recovery service"
```

## Task 5: Interrupted Recovery and Startup Decision Gate

**Files:**

- Create: `src/MineRailMonitor.Infrastructure/Persistence/DatabaseStartupDecisionKind.cs`
- Create: `src/MineRailMonitor.Infrastructure/Persistence/DatabaseStartupDecision.cs`
- Create: `src/MineRailMonitor.Infrastructure/Persistence/DatabaseStartupGate.cs`
- Create: `tests/MineRailMonitor.Infrastructure.Tests/DatabaseStartupGateTests.cs`
- Modify: none

**Interfaces:**

- Consumes: `ISqliteDatabaseHealthChecker`、`SqliteBackupService.ScanCandidates`、`SqliteRecoveryService.ReadMarker`、production/data/backup paths、Acceptance bypass flag。
- Produces:

```csharp
public sealed class DatabaseStartupGate
{
    public DatabaseStartupGate(
        string productionDatabasePath,
        string backupRootDirectory,
        ISqliteDatabaseHealthChecker healthChecker,
        SqliteBackupService backupService,
        SqliteRecoveryService recoveryService,
        bool acceptanceMode,
        ILogger logger);

    public DatabaseStartupDecision Inspect();
}
```

- [ ] **Step 1: Write the failing tests**

在 `DatabaseStartupGateTests.cs` 添加：

- `Missing_database_without_marker_returns_create_new`：允许后续 Store 建库。
- `Healthy_database_returns_start_healthy`：不扫描 recovery candidates。
- `Corrupt_database_returns_recover_corrupt_with_individually_scanned_candidates`：只
  扫描严格命名的正式备份，并只把本次 `FullValidation` 为 `Healthy` 的 candidate 放入
  decision。
- `Newest_corrupt_backup_falls_back_to_next_healthy_candidate`：最新合法命名 backup
  `FullValidation` 为 `Corrupt` 时排除它，继续选择下一个较旧且健康的 backup。
- `Unavailable_candidate_is_not_presented_for_restore`：candidate 的
  `FullValidation` 为 `Unavailable` 时排除它，Dialog 的 candidates 不包含该文件。
- `Unavailable_database_returns_unavailable_without_recovery_candidates`：即使备份目录
  有健康文件，也返回 `Unavailable` 且 candidates 为空。
- `Marker_and_missing_database_returns_interrupted_recovery_not_create_new`。
- `Marker_and_existing_database_still_blocks_normal_start`。
- `Acceptance_mode_bypasses_production_backup_recovery_and_maintenance_paths`：只返回
  acceptance 数据路径对应的可启动决策，不访问生产 `Backups`、`Data/Corrupt` 或 recovery UI。

关键断言使用：

```csharp
Assert.Equal(DatabaseStartupDecisionKind.InterruptedRecovery, decision.Kind);
Assert.NotEqual(DatabaseStartupDecisionKind.CreateNew, decision.Kind);
Assert.Empty(decision.Candidates);
```

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --filter "FullyQualifiedName~DatabaseStartupGateTests"
```

Expected: FAIL，原因是 startup decision 类型和 gate 尚未实现。

- [ ] **Step 3: Implement the minimum startup gate**

执行顺序固定为：

1. 先读取 marker。
2. marker 存在时先返回 `InterruptedRecovery`，不把缺失生产 DB 解释为 `Missing`，
   也不因生产 DB 存在而直接 Healthy。
3. 无 marker 时调用 HealthChecker。
4. `Missing` 返回 `CreateNew`；`Healthy` 返回 `StartHealthy`。
5. `Corrupt` 才调用 `ScanCandidates`；按新到旧顺序对每个严格命名 candidate 调用
   `healthChecker.Inspect(candidate.Path, SqliteInspectionMode.FullValidation)`。
6. 只有本次检查为 `Healthy` 的 candidates 才放入 `DatabaseStartupDecision.Candidates`；
   `Corrupt` 和 `Unavailable` candidate 都排除并记录原因。因而合法文件名但内容损坏
   或当前不可访问的 backup 不会展示给 Dialog。
7. `RecoveryService` 在真正恢复入口仍再次对 selected candidate 执行 `FullValidation`，
   不能因为 gate 已经验证过就跳过。
8. `Unavailable` 返回 `Unavailable`，candidates 为空，不移动生产文件、不创建空库。
9. `acceptanceMode=true` 时只使用传入 acceptance database/log 路径，跳过生产 backup、
   recovery candidate、marker UI 和 scheduler。

Gate 不创建 `SqlitePassageRecordStore`，不执行 schema migration，不启动 MainWindow、
YardCommunicationManager 或 RFID。

- [ ] **Step 4: Run GREEN and regression**

```powershell
dotnet build MineRailMonitor.sln -c Debug
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~DatabaseStartupGateTests"
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build
```

Expected: gate tests和现有 Infrastructure tests全部通过，0 warnings、0 errors、0 skipped。

- [ ] **Step 5: Commit**

```powershell
git add src/MineRailMonitor.Infrastructure/Persistence/DatabaseStartupDecisionKind.cs src/MineRailMonitor.Infrastructure/Persistence/DatabaseStartupDecision.cs src/MineRailMonitor.Infrastructure/Persistence/DatabaseStartupGate.cs tests/MineRailMonitor.Infrastructure.Tests/DatabaseStartupGateTests.cs
git commit -m "feat: add sqlite startup decision gate"
```

## Task 6: DatabaseRecoveryDialog and Startup Error UI

**Files:**

- Create: `src/MineRailMonitor/Pages/DatabaseRecoveryDialog.xaml`
- Create: `src/MineRailMonitor/Pages/DatabaseRecoveryDialog.xaml.cs`
- Create: `tests/MineRailMonitor.Core.Tests/DatabaseRecoveryDialogMarkupTests.cs`
- Modify: none

**Interfaces:**

- Consumes: `DatabaseStartupDecision`、`SqliteBackupCandidate`、现有 `Colors.xaml`、`Typography.xaml`、`Cards.xaml`、`StyledMessageDialog` 的工业深色样式。
- Produces:

```csharp
public enum DatabaseRecoveryDialogAction
{
    Recover,
    Retry,
    OpenDataDirectory,
    Exit
}

public sealed class DatabaseRecoveryDialogResult
{
    public DatabaseRecoveryDialogAction Action { get; }
    public SqliteBackupCandidate? Candidate { get; }
}

public sealed partial class DatabaseRecoveryDialog : Window
{
    public DatabaseRecoveryDialog(DatabaseStartupDecision decision);
    public DatabaseRecoveryDialogResult Result { get; }
}
```

- [ ] **Step 1: Write the failing markup tests**

创建 `DatabaseRecoveryDialogMarkupTests.cs`，沿用现有 `Locate(...)` helper，断言：

- XAML 是启动级 Window，不依赖 MainWindow、CommunicationPage 或数据库管理页。
- `Corrupt` 状态显示健康检查摘要、候选时间/路径、恢复、打开目录和退出操作。
- 无健康候选时恢复按钮隐藏或不可用。
- `Unavailable` 状态只出现重试、打开数据目录、退出，不出现恢复按钮。
- interrupted recovery 显示 marker、staging、corrupt bundle、source backup 路径，
  不显示首次安装或创建空库选项。
- 不出现“忽略错误继续运行”“创建空数据库”等文案。

示例断言：

```csharp
Assert.Contains("重试", xaml);
Assert.Contains("打开数据目录", xaml);
Assert.DoesNotContain("忽略错误继续运行", xaml);
Assert.Contains(".sqlite-recovery-in-progress", code);
```

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DatabaseRecoveryDialogMarkupTests"
```

Expected: FAIL，原因是 Dialog XAML/C# 和 markup contract 尚不存在。

- [ ] **Step 3: Implement the minimum dialog**

- 复用现有深色工业资源，不创建数据库管理页面，不改变现有主窗口布局。
- `Corrupt` 状态只允许用户选择 gate 在本次启动中已经通过 `FullValidation` 的 candidate；
  Dialog 不接受仅凭合法文件名的 backup。本身不复制、替换或删除数据库文件。
- `Unavailable` 只显示重试、打开数据目录、退出。
- marker 状态显示 interrupted recovery，不自动续跑；操作结果通过
  `DatabaseRecoveryDialogResult` 返回给 App startup gate。
- 所有按钮都只返回动作，实际恢复由 `SqliteRecoveryService` 执行。

- [ ] **Step 4: Run GREEN and regression**

```powershell
dotnet build MineRailMonitor.sln -c Debug
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~DatabaseRecoveryDialogMarkupTests"
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --no-build
```

Expected: Dialog markup tests和现有 Core tests全部通过，0 warnings、0 errors、0 skipped。

- [ ] **Step 5: Commit**

```powershell
git add src/MineRailMonitor/Pages/DatabaseRecoveryDialog.xaml src/MineRailMonitor/Pages/DatabaseRecoveryDialog.xaml.cs tests/MineRailMonitor.Core.Tests/DatabaseRecoveryDialogMarkupTests.cs
git commit -m "feat: add sqlite startup recovery dialog"
```

## Task 7: App Startup Gate and App/MainWindow Ownership

**Files:**

- Modify: `src/MineRailMonitor/App.xaml.cs`
- Modify: `src/MineRailMonitor/MainWindow.xaml.cs`
- Create: `tests/MineRailMonitor.Core.Tests/DatabaseStartupOwnershipMarkupTests.cs`

**Interfaces:**

- Consumes: `DatabaseStartupGate`、`DatabaseRecoveryDialog`、`SqlitePassageRecordStore`、`DatabaseMaintenanceCoordinator`、Acceptance options。
- Produces: `App` owns nullable `_passageRecordStore` and `_databaseMaintenanceCoordinator`; `MainWindow(SqlitePassageRecordStore passageRecordStore)` receives an already validated Store and never creates/disposes it.

- [ ] **Step 1: Write the failing ownership/startup tests**

在 `DatabaseStartupOwnershipMarkupTests.cs` 读取 `App.xaml.cs` 和 `MainWindow.xaml.cs`，添加：

- `App_runs_startup_gate_before_new_main_window`：代码中 gate decision 和 final health path
  必须出现在 `new MainWindow` 之前。
- `MainWindow_does_not_construct_sqlite_store`：MainWindow source 不包含
  `new SqlitePassageRecordStore`。
- `MainWindow_does_not_dispose_app_owned_store`：MainWindow source 不直接调用
  `_passageRecordStore.Dispose()`。
- `App_owns_store_and_maintenance_coordinator`：App source 包含两个 owner 字段和
  对应 Dispose/Stop 路径。
- `Acceptance_startup_skips_production_backup_and_recovery`：App source 在
  `AcceptanceOptions.Enabled` 分支不创建生产 Backups/Data/Corrupt 或 recovery UI。

另在 `DatabaseStartupGateTests` 中保留实际决策测试；markup tests只约束 ownership/顺序，
不代替 Infrastructure 行为测试。

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DatabaseStartupOwnershipMarkupTests"
```

Expected: FAIL，原因是当前 MainWindow 直接 new Store，App 没有 startup gate 和 owner 字段。

- [ ] **Step 3: Implement App startup orchestration and injection**

`App.OnStartup` 按以下顺序执行：

```text
resolve production/acceptance paths
→ create health checker / backup service / recovery service / startup gate
→ inspect marker and database health
→ Missing without marker: continue to Store creation
→ Healthy: StartupFast passed; run startup catch-up backup with FullValidation before Store creation
→ Corrupt: ScanCandidates, FullValidation-filter candidates, show Dialog, recover selected
  candidate with another FullValidation, final FullValidation, then continue
→ Unavailable or interrupted recovery: show error UI, do not create Store/MainWindow
→ App creates SqlitePassageRecordStore
→ App creates DatabaseMaintenanceCoordinator
→ App creates MainWindow(store)
→ coordinator.StartAsync
→ show MainWindow
```

具体规则：

- 对 Healthy 生产库，`RunStartupCatchUpAsync` 必须在 Store 构造和 migration 之前执行，
  以保留 migration 前备份；coordinator 可以先由 App 创建为 startup-only service，
  但 scheduled loop 只能在 Store 和 MainWindow 启动后开始。
- 生产 startup gate 使用 `SqliteInspectionMode.StartupFast`；backup、candidate、staging
  和 restored production final check 使用 `SqliteInspectionMode.FullValidation`。
- 对 Corrupt，Gate 先完成候选的当前启动 `FullValidation`，Dialog 只看到 Healthy 候选；
  Dialog 恢复成功后 RecoveryService 仍必须再次 `FullValidation`，只有 final Healthy 才
  创建 Store。
- 对 Unavailable 和 marker interrupted，App 不创建 Store、不创建 MainWindow、不启动 RFID，
  只按 Dialog 动作重试、打开目录或退出。
- Acceptance 使用命令行提供的独立 database/log/runtime 路径，跳过生产 backup、
  recovery UI 和 scheduler；仍创建现有业务 Store 以执行 Acceptance。
- `MainWindow` 构造函数改为接收 `SqlitePassageRecordStore`，保留现有 LoadProject、
  Yard manager、报警、Black Box 和双站场逻辑；删除构造函数中的 Store new。
- App 保存 Store 和 coordinator 字段，后续 Task 8 负责安全停止；MainWindow 不拥有
  数据库基础设施。

- [ ] **Step 4: Run GREEN and regression**

```powershell
dotnet build MineRailMonitor.sln -c Debug
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~DatabaseStartupOwnershipMarkupTests"
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --no-build
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build
```

Expected: startup ownership tests、Core、Infrastructure全部通过，0 warnings、0 errors、0 skipped。

- [ ] **Step 5: Commit**

```powershell
git add src/MineRailMonitor/App.xaml.cs src/MineRailMonitor/MainWindow.xaml.cs tests/MineRailMonitor.Core.Tests/DatabaseStartupOwnershipMarkupTests.cs tests/MineRailMonitor.Core.Tests/AppStartupMarkupTests.cs
git commit -m "feat: gate sqlite startup before main window"
```

## Task 8: Safe Shutdown and Maintenance Ownership

**Files:**

- Modify: `src/MineRailMonitor/App.xaml.cs`
- Modify: `src/MineRailMonitor/MainWindow.xaml.cs`
- Modify: `tests/MineRailMonitor.Infrastructure.Tests/DatabaseMaintenanceCoordinatorTests.cs`
- Modify: `tests/MineRailMonitor.Core.Tests/DatabaseStartupOwnershipMarkupTests.cs`

**Interfaces:**

- Consumes: `DatabaseMaintenanceCoordinator.StopAsync()`、现有 MainWindow `OnWindowClosing` / `_allowWindowClose`、`YardCommunicationManager.Dispose()`、Raw Packet Black Box lifecycle。
- Produces: `App.StopDatabaseInfrastructureAsync()`，幂等执行 coordinator stop → coordinator dispose → Store dispose；MainWindow 先停止 manager/runtime，再请求 App 完成数据库 shutdown。

- [ ] **Step 1: Write the failing tests**

在 Infrastructure tests 添加：

- `StopAsync_waits_for_in_flight_backup_before_store_can_be_disposed`：blocking runner
  未释放时 `StopAsync` 不完成；释放后才完成。
- `StopAsync_is_idempotent`：连续调用两次不重复启动或 Dispose backup。

在 markup tests 添加：

- `Shutdown_order_is_runtime_then_coordinator_then_store`：`MainWindow.xaml.cs` 中 manager
  停止发生在调用 App database shutdown 之前；`App.xaml.cs` 中 coordinator stop 发生在
  Store dispose 之前。
- `MainWindow_keeps_black_box_after_manager_stop_until_close_drain`：Black Box Dispose
  排在 manager Dispose 之后，且 MainWindow 不 Dispose Store。

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --filter "FullyQualifiedName~DatabaseMaintenanceCoordinatorTests"
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DatabaseStartupOwnershipMarkupTests"
```

Expected: FAIL，原因是 App 没有 shutdown owner 方法，MainWindow 当前在 manager 事件解除前后直接 Dispose Store，且 coordinator 停止顺序不存在。

- [ ] **Step 3: Implement the minimum safe shutdown**

- `App.StopDatabaseInfrastructureAsync()` 使用一次性状态标志，先 `await
  _databaseMaintenanceCoordinator.StopAsync()`，再 Dispose coordinator，最后 Dispose
  Store；重复调用直接返回。
- MainWindow 继续负责停止 `_yardCommunicationManager` 和 Acceptance runtime writer，
  manager 完整 Dispose 后才解除其 Datagram/Command 事件，再 Dispose Raw Packet Black Box。
- 复用现有 `OnWindowClosing` 的 `_allowWindowClose`：第一次关闭时取消关闭并执行
  `StopRuntimeThenCloseAsync`；该方法等待 manager/runtime 和 App 数据库 shutdown 后设置
  `_allowWindowClose=true` 并重新 `Close()`。
- `OnWindowClosed` 只做已完成关闭后的最后性清理，不再 Dispose App-owned Store；
  App `OnExit` 对 shutdown 方法做幂等兜底，确保没有窗口关闭路径遗漏。
- 维护 coordinator 的 `StopAsync` 取消 scheduler 并等待 in-flight backup，保证 Store
  不会在 backup 仍使用数据库时被 Dispose。

- [ ] **Step 4: Run GREEN and regression**

```powershell
dotnet build MineRailMonitor.sln -c Debug
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~DatabaseMaintenanceCoordinatorTests"
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~DatabaseStartupOwnershipMarkupTests"
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --no-build
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build
```

Expected: shutdown/ownership tests、Core、Infrastructure全部通过，0 warnings、0 errors、0 skipped。

- [ ] **Step 5: Commit**

```powershell
git add src/MineRailMonitor/App.xaml.cs src/MineRailMonitor/MainWindow.xaml.cs tests/MineRailMonitor.Infrastructure.Tests/DatabaseMaintenanceCoordinatorTests.cs tests/MineRailMonitor.Core.Tests/DatabaseStartupOwnershipMarkupTests.cs
git commit -m "feat: make sqlite maintenance shutdown safe"
```

## Task 9: Regression and Final Acceptance Contract

**Files:**

- Create: `tests/MineRailMonitor.Infrastructure.Tests/SqliteBackupRecoveryRegressionTests.cs`
- Modify: `tests/MineRailMonitor.Infrastructure.Tests/SqlitePassageRecordStoreTests.cs`
- Modify: `tests/MineRailMonitor.Core.Tests/DatabaseStartupOwnershipMarkupTests.cs`
- Modify: none in `scripts/run-rfid-acceptance.ps1`

**Interfaces:**

- Consumes: all completed Infrastructure services、current Store、existing Core runtime、existing Acceptance script。
- Produces: one final regression suite proving that database maintenance does not change schema, alarms, RFID runtime, black box, or 560/620 isolation.

- [ ] **Step 1: Write the final regression tests**

在 `SqliteBackupRecoveryRegressionTests.cs` 添加以下测试：

- `Schema_v1_v2_v3_migrations_remain_unchanged_after_health_and_backup_inspection`：使用
  现有 v1/v2 fixtures，先 health/backup inspection，再由 Store migration 到 user_version=3，
  断言原有字段和记录结果不变。
- `Alarm_ack_recovery_and_pending_clear_survive_backup_inspection_and_restart`：使用现有
  alarm/pending record，完成 backup/restore 后断言 `AlarmAcknowledgedAt`、
  `AlarmRecoveredAt`、`ClearState` 与原值一致。
- `Raw_packet_black_box_and_560_620_station_identity_are_not_touched`：只检查 backup/recovery
  路径，不创建或修改 Black Box 文件，不改变相同 ProtocolAddress 的 560/620 StationId。
- `Interrupted_recovery_crash_boundaries_never_initialize_empty_database`：分别在 corrupt
  bundle 完成后、sidecar 移除后、production replacement 开始后写入 marker，再重启 gate，
  每个阶段都返回 `InterruptedRecovery` 而不是 `CreateNew`。
- `Unavailable_never_reaches_recovery_candidate_selection`：locked/access denied 结果不会
  扫描、移动或替换 backup/production 文件。
- 回归中的 backup promotion、candidate selection、recovery source/staging/final health
  调用必须显式传入正确的 `SqliteInspectionMode`，不得恢复无 mode 的旧接口。

测试只通过 Infrastructure public contracts 和临时目录验证，不修改主程序、协议或 acceptance fixtures。

- [ ] **Step 2: Run the final regression contract**

```powershell
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Release --filter "FullyQualifiedName~SqliteBackupRecoveryRegressionTests"
```

Expected:

- 如果 FAIL，只修复前述 Tasks 1-8 暴露的最小接口 wiring 或生命周期 gap，然后重跑受影响
  的回归测试；不得借此扩大范围。
- 如果第一次运行就是 GREEN，记录为 regression contract 已被现有实现满足，不为了
  TDD 形式人为制造 RED，也不修改代码制造失败。

- [ ] **Step 3: If needed, implement only the minimum regression-facing fixes**

如果回归测试发现的是跨任务接口不一致，只修正前述任务定义的接口调用、路径或生命周期
连接；不得把业务逻辑、RFID 协议、报警、Black Box 或 schema 改动塞入本任务。所有修正
必须保持：

- Store 仍负责 CRUD/schema/migration；
- App 仍是 Store/coordinator 唯一 owner；
- Inspection Connection 与 Business Connection 分离；
- acceptance bypass 生产目录和 scheduler。

- [ ] **Step 4: Run Release full verification and Acceptance**

```powershell
dotnet restore
dotnet build MineRailMonitor.sln -c Release
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Release --no-build
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Release --no-build
powershell -ExecutionPolicy Bypass -File scripts/run-rfid-acceptance.ps1
```

Expected：

- Build 0 warnings、0 errors；
- Core、Infrastructure 全部通过，0 skipped；
- 现有 Acceptance 8/8；
- schema v1/v2/v3、报警 ack/recovery、PendingClear、Raw Packet Black Box、560/620
  相同 ProtocolAddress 隔离全部保持通过；
- Acceptance 不创建生产 `Backups/`、`Data/Corrupt/` 或 recovery marker。

- [ ] **Step 5: Commit**

```powershell
git add tests/MineRailMonitor.Infrastructure.Tests/SqliteBackupRecoveryRegressionTests.cs tests/MineRailMonitor.Infrastructure.Tests/SqlitePassageRecordStoreTests.cs tests/MineRailMonitor.Core.Tests/DatabaseStartupOwnershipMarkupTests.cs
git commit -m "test: lock sqlite backup recovery regression contract"
```

## File Change Summary

### Expected new production files

- `src/MineRailMonitor.Infrastructure/Persistence/SqliteDatabaseHealthState.cs`
- `src/MineRailMonitor.Infrastructure/Persistence/SqliteDatabaseHealthResult.cs`
- `src/MineRailMonitor.Infrastructure/Persistence/SqliteDatabaseHealthChecker.cs`
- `src/MineRailMonitor.Infrastructure/Persistence/SqliteBackupCandidate.cs`
- `src/MineRailMonitor.Infrastructure/Persistence/SqliteBackupResult.cs`
- `src/MineRailMonitor.Infrastructure/Persistence/SqliteBackupService.cs`
- `src/MineRailMonitor.Infrastructure/Persistence/SqliteRetentionService.cs`
- `src/MineRailMonitor.Infrastructure/Persistence/DatabaseMaintenanceCoordinator.cs`
- `src/MineRailMonitor.Infrastructure/Persistence/SqliteRecoveryPhase.cs`
- `src/MineRailMonitor.Infrastructure/Persistence/SqliteRecoveryMarkerStore.cs`
- `src/MineRailMonitor.Infrastructure/Persistence/SqliteRecoveryService.cs`
- `src/MineRailMonitor.Infrastructure/Persistence/DatabaseStartupDecisionKind.cs`
- `src/MineRailMonitor.Infrastructure/Persistence/DatabaseStartupDecision.cs`
- `src/MineRailMonitor.Infrastructure/Persistence/DatabaseStartupGate.cs`
- `src/MineRailMonitor/Pages/DatabaseRecoveryDialog.xaml`
- `src/MineRailMonitor/Pages/DatabaseRecoveryDialog.xaml.cs`

### Expected modified production files

- `src/MineRailMonitor/App.xaml.cs`
- `src/MineRailMonitor/MainWindow.xaml.cs`

No new package, ProjectReference, database table, schema migration, simulator file or RFID protocol file is expected.

### Expected new test files

- `tests/MineRailMonitor.Infrastructure.Tests/SqliteDatabaseHealthCheckerTests.cs`
- `tests/MineRailMonitor.Infrastructure.Tests/SqliteBackupServiceTests.cs`
- `tests/MineRailMonitor.Infrastructure.Tests/SqliteRetentionServiceTests.cs`
- `tests/MineRailMonitor.Infrastructure.Tests/DatabaseMaintenanceCoordinatorTests.cs`
- `tests/MineRailMonitor.Infrastructure.Tests/SqliteRecoveryServiceTests.cs`
- `tests/MineRailMonitor.Infrastructure.Tests/DatabaseStartupGateTests.cs`
- `tests/MineRailMonitor.Infrastructure.Tests/SqliteBackupRecoveryRegressionTests.cs`
- `tests/MineRailMonitor.Core.Tests/DatabaseRecoveryDialogMarkupTests.cs`
- `tests/MineRailMonitor.Core.Tests/DatabaseStartupOwnershipMarkupTests.cs`

### Expected modified test files

- `tests/MineRailMonitor.Infrastructure.Tests/SqlitePassageRecordStoreTests.cs` only for a
  directly adjacent schema/migration regression assertion.
- No simulator, RFID transport, alarm, black box or acceptance implementation tests are removed.

## Plan Self-Check

### Spec coverage

- Health states and Inspection Connection: Task 1。
- Online Backup、tmp、validation、strict filename、candidate scan: Task 2。
- startup catch-up、02:00、SemaphoreSlim、14 local days、stale tmp、failed backup retention: Task 3。
- staging、candidate revalidation、corrupt bundle、db/wal/shm、marker、replacement、final health: Task 4。
- marker + missing/existing DB、crash boundaries、no empty DB: Tasks 4、5、9。
- App startup gate、Corrupt、Unavailable、Missing、Acceptance bypass: Tasks 5、7。
- Recovery Dialog、retry、open directory、recover、exit、no management page: Task 6。
- App ownership、MainWindow injection、no double Dispose: Tasks 7、8。
- runtime → coordinator → Store shutdown: Task 8。
- schema/alarm/PendingClear/Black Box/560-620/Core/Infrastructure/Acceptance regression: Task 9。

### Plan self-check required by this design revision

1. 正式 backup promotion 前明确调用 `FullValidation`，并由测试锁定。
2. Dialog 看到的 candidates 已经通过本次启动逐个 `FullValidation`；RecoveryService 入口
   仍再次验证 selected candidate。
3. 最新损坏 candidate 会被排除并 fallback 到下一份健康 candidate；`Unavailable` candidate
   不会展示给恢复 UI。
4. scheduled tick 通过 `IAsyncDelay`/`ManualAsyncDelay` 手工推进，不依赖真实时间或
   `Thread.Sleep`。
5. stale tmp 默认阈值明确为 age `> 24` 小时，且只删除严格匹配本应用命名的文件。
6. 所有 backup 日期目录固定为 `Backups/SQLite/yyyy-MM-dd/`，文件固定为
   `MineRailMonitor_yyyyMMdd_HHmmss.db`。
7. `SqliteInspectionMode` 已贯穿 health checker、backup、recovery、startup gate 和
   regression contracts，接口签名前后一致。
8. Task 9 不再要求人为制造 RED；首轮 GREEN 时只记录 contract 已满足，然后执行 Release
   全量验证和 Acceptance。

### Known implementation boundary

批准的 spec 同时要求“已有 Healthy DB 的 startup catch-up backup 在 Store migration 前执行”，
并在 ownership 图中展示“App 创建 Store → App 创建 Coordinator”。本计划不静默改变该语义：
`DatabaseMaintenanceCoordinator` 的实例由 App 作为唯一 owner 管理，允许在 Store 构造前
调用不依赖 Store 的 `RunStartupCatchUpAsync`；scheduled loop 只在 Store 和 MainWindow 启动后
执行。这样同时保持 migration 前备份和 App 唯一 ownership。reviewer 需要重点确认这个
构造/启动顺序，而不是把 startup backup 移到 Store 创建之后。

当前代码的真正 blocker 是 `App.OnStartup` 与 `MainWindow` 构造职责耦合；Task 7/8 通过最小
构造函数注入和现有 `_allowWindowClose` 关闭路径拆开，不需要全项目 MVVM 重构。

本计划没有未完成标记、未定义的实现类型或泛化测试描述；Tasks 1-8 都有 RED、GREEN、
regression 和独立 commit message，Task 9 明确采用首轮 GREEN 也合法的 final regression
语义，并保留独立 commit message。
