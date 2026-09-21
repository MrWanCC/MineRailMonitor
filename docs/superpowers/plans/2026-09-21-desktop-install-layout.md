# MineRailMonitor Desktop Installation Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 MineRailMonitor 从当前以 AppContext.BaseDirectory 为运行数据根目录的开发布局，迁移为可安装的 `<Root>\App + Projects + Data + Backups + Logs + Docs` 桌面软件布局，并使用 Inno Setup 构建普通 Windows 安装包。

**Architecture:** 使用统一 `ApplicationPaths` / `ApplicationRoot` abstraction 解析运行目录。正式安装状态下 exe 位于 `<Root>\App`，运行数据位于 `<Root>` 同级子目录；开发/F5 状态继续以 BaseDirectory 为 Root。Installer 只负责部署、ACL、升级目录复用和卸载保留，不重新实现 SQLite、backup、recovery 或项目配置业务逻辑。

**Tech Stack:** .NET Framework 4.8, WPF, C#, xUnit, PowerShell, System.Data.SQLite.Core 1.0.118, Inno Setup

**Spec:** `docs/superpowers/specs/2026-09-21-desktop-install-layout-design.md`

## Global Constraints

- 默认首次安装目录：`C:\MineRailMonitor`
- 用户选择的安装目录本身就是 `ApplicationRoot` / `<Root>`。
- 不得额外生成 `<Root>\MineRailMonitor`。
- 正式 exe：`<Root>\App\MineRailMonitor.exe`。
- Root 下固定包含：`App`、`Projects`、`Data`、`Backups`、`Logs`、`Docs`。
- 当前发布源只允许使用仓库中已跟踪的脱敏 `Projects\Example`；不得把整个仓库 `Projects` 根目录或本地未跟踪的 `Default`、maps、stations、客户项目复制进安装包。
- 首次 clean staging 只部署 `Projects\Example`；`Projects\Default` 由现场配置流程提供，不由发布脚本创建或打包。
- Installed：BaseDirectory 最终目录名为 `App` → `ApplicationRoot = parent(BaseDirectory)`。
- Development/F5：BaseDirectory 最终目录名不是 `App` → `ApplicationRoot = BaseDirectory`。
- 测试显式 `ApplicationRoot` injection 优先于自动推导。
- Acceptance 显式 `DatabasePath` / `LogDirectory` 保持原语义。
- Acceptance 不允许碰真实安装 `Data` / `Logs`。
- 不做 Windows Service、开机自启、watchdog、Scheduled Task、Registry Run。
- 应用正常启动不要求管理员权限。
- `<Root>` 和 `<Root>\App` 只授予普通用户 Read / Execute。
- `<Root>\Data`、`Backups`、`Logs`、`Projects` 授予普通用户 Modify。
- `<Root>\Docs` 按现场文档需求授予 Write / Modify。
- 普通用户不得修改 App 下 exe/dll。
- 安装器必须规范化 effective ACL：Root 先 `/reset`，managed subtree 先 `/reset /T /C` 清除已有 explicit DACL，再分别关闭继承并用稳定 SID 明确授予 SYSTEM/Administrators Full Control、Users Read/Execute；数据目录明确授予 Users Modify，不依赖父目录权限或单纯追加 Inno `Permissions` ACE。
- ACL 实现可调用 Windows 自带 `icacls.exe`，每条命令非零都必须使安装失败；不得修改 Root 之外的父目录、系统目录，不得授予 `Everyone` Full Control 或保留未知 explicit write ACE。
- 安装器必须检查 .NET Framework 4.8 Full Release；`Release >= 528040` 才允许安装，首版不自动联网下载。
- SQLite WAL / FULL / health / backup / recovery / marker 语义不变。
- 不重新设计 SQLite，不修改 RFID protocol、CRC、Byte7、业务报警逻辑。
- 升级默认复用上次安装 Root。
- 不静默迁移跨目录 Data / Projects / Backups / Logs。
- 升级不得覆盖现场 Projects，不得删除 Data / Backups / Logs。
- 卸载默认保留 Projects、Data、Backups、Logs。
- 首版禁止已安装产品跨 Root 升级；previous Root 与当前选择不一致时必须阻止继续。
- 卸载第二次删除确认选择 No 时仍继续卸载，但保留 Projects、Data、Backups、Logs、Docs；仅两次 Yes 才删除这些具体目录，不删除 Root。
- Acceptance 8/8 必须保持通过。

## Repo Mapping

以下事实来自当前仓库，后续 Task 使用这些实际路径：

- `src/MineRailMonitor/App.xaml.cs`
  - 构造函数从 `AppContext.BaseDirectory\Logs` 创建生产 logger。
  - `StartProductionAsync` 从 `AppContext.BaseDirectory\Data` 和 `AppContext.BaseDirectory\Backups\SQLite` 构造生产路径。
  - Acceptance 分支使用 `AcceptanceOptions.DatabasePath`，生产启动与 Acceptance 启动分开。
  - `TryRunStartupCatchUpAsync` 已经通过 `Task.Run` 将 startup maintenance 调度到 ThreadPool；本计划保留该行为。
- `src/MineRailMonitor/MainWindow.xaml.cs`
  - Acceptance 项目目录当前使用 `AppContext.BaseDirectory\Projects\Example`。
  - 生产项目目录由 `ResolveProjectDirectory` 从 `AppContext.BaseDirectory\Projects` 选择 `Default` 或 `Example`。
  - Acceptance 黑匣子目录使用 `AcceptanceOptions.LogDirectory\BlackBox`，生产黑匣子目录使用 `AppContext.BaseDirectory\Logs\BlackBox`。
- `src/MineRailMonitor/MineRailMonitor.csproj`
  - `Projects\**\*` 当前 `CopyToOutputDirectory=PreserveNewest`。
  - 同一项当前 `CopyToPublishDirectory=PreserveNewest`，会使 publish 输出包含 `App\Projects`，Task 3 必须调整。
- `src/MineRailMonitor.Infrastructure/Logging/FileLogger.cs`
  - 接收显式 log directory，不自行依赖 AppContext；Task 2 只改变 App 传入的生产目录。
- `src/MineRailMonitor.Infrastructure/Configuration/ProjectConfigService.cs`
  - 接收显式 project directory，不自行推导安装根目录；Task 2 只改变 MainWindow 传入的生产项目目录。
- `src/MineRailMonitor.Infrastructure/Persistence/`
  - 已包含 SQLite backup、health、recovery、retention、startup gate 和 maintenance coordinator；本计划不修改其数据库业务语义。
- `tests/MineRailMonitor.Core.Tests/`
  - 已有 `DatabaseStartupOwnershipMarkupTests.cs`，其中当前仍断言 AppContext 拼接 Data/Backups；Task 2 更新这些 contract tests。
- `tests/MineRailMonitor.Infrastructure.Tests/`
  - 已引用 Infrastructure 和 Core，可直接承载 `ApplicationPathsTests.cs`。
- `scripts/run-rfid-acceptance.ps1`
  - 现有 Acceptance 入口，Task 6 原样运行，不修改其 isolation contract。
- `Projects/Example/`
  - 当前唯一受发布白名单允许的项目源，存在 `project.json`、`stations/560.json`、`stations/620.json`；开发输出仍需要保留模板复制。
- `Projects/Default/`、`Projects/*/maps/`、非 `Example` 现场站场配置以及客户项目
  - 不属于当前仓库发布输入；不得由 Task 3 自动发现或收集。
- 当前没有 `installer/` 目录；Task 4 创建它。
- 已执行真实 Release publish 检查：publish 输出包含 `System.Data.SQLite.dll`，但没有复制 native `SQLite.Interop.dll` 子目录。
- 当前 Release build 输出实际包含 `src/MineRailMonitor/bin/Release/net48/x86/SQLite.Interop.dll` 和 `src/MineRailMonitor/bin/Release/net48/x64/SQLite.Interop.dll`；Task 3 必须显式把这两个实际文件复制到最终 staging 的 `App\x86`、`App\x64`，不能假设 publish 已带出它们。

## Review Focus

1. BaseDirectory 最终目录名为 `App` 时必须取 parent，且 `App` 大小写不敏感；BaseDirectory 为开发输出目录时不能错误取 parent。测试归属 Task 1。
2. 显式 ApplicationRoot 必须覆盖自动推导，且相对/绝对路径都必须先标准化；测试归属 Task 1。
3. 开发输出必须继续包含 `Projects`，正式 staging 的 `App` 不能再包含 `Projects`，且正式 staging 只能包含白名单 `Projects\Example`；测试归属 Task 3。
4. 用户选中的目录必须直接成为 Root，不能生成重复 `MineRailMonitor` 子目录；测试归属 Task 4。
5. 升级必须复用上次 Root，改变 Root 时必须阻止继续且不得复制或覆盖现场数据；测试归属 Task 5。
6. 普通 Users 的 effective 权限只能在数据目录为 Modify，不能 Modify App 中的 exe/dll；测试归属 Task 5，且必须覆盖安装前已存在 `Everyone:(M)` 的 hostile Root。
7. Acceptance 显式 DatabasePath / LogDirectory 必须继续隔离真实安装目录；测试归属 Task 2 和 Task 6。
8. 当前 contract 的已知边界：Development output 最终目录名正好为 `App` 时，与 installed layout 无法区分，会按 installed 规则取 parent；测试和实现不得假设该情况能被自动识别为 development。
9. ACL 必须验证 effective 权限而非只检查脚本文本；Task 5 负责脚本契约，Task 6 负责真实 `icacls` 和非管理员文件操作。
10. 已安装后跨 Root 选择必须被阻止，不能用警告后 Yes 放行；Task 5 和 Task 6 负责验证。
11. .NET Framework 4.8 prerequisite 和 `System.Data.SQLite.dll`/x86/x64 `SQLite.Interop.dll` 必须进入 Task 4/Task 6 验证。

---

### Task 1: Add ApplicationPaths and root resolution contract

**Files:**

- Create: `src/MineRailMonitor.Infrastructure/Configuration/ApplicationPaths.cs`
- Create: `tests/MineRailMonitor.Infrastructure.Tests/ApplicationPathsTests.cs`
- Modify: none

**Interfaces:**

- Consumes: `baseDirectory` and optional explicit `applicationRoot` strings.
- Produces: normalized `RootDirectory`, derived directory properties, and `EnsureWritableDirectories()`.

The type must remain a path/layout helper. It must not contain SQLite, logging, JSON project loading, installer, ACL, or migration behavior.

- [ ] **Step 1: Write the failing tests**

Create `ApplicationPathsTests.cs` with real filesystem path assertions and no machine-specific D: assumption:

```csharp
public sealed class ApplicationPathsTests
{
    [Fact]
    public void Installed_layout_uses_parent_of_App_directory()
    {
        var paths = new ApplicationPaths(
            @"D:\MineRailMonitor\App\",
            explicitRootDirectory: null);

        Assert.Equal(
            Path.GetFullPath(@"D:\MineRailMonitor"),
            paths.RootDirectory,
            ignoreCase: true);
    }

    [Fact]
    public void Installed_layout_App_name_is_case_insensitive()
    {
        var paths = new ApplicationPaths(
            @"C:\Deploy\aPp\",
            explicitRootDirectory: null);

        Assert.Equal(Path.GetFullPath(@"C:\Deploy"), paths.RootDirectory, ignoreCase: true);
    }

    [Fact]
    public void Development_layout_uses_base_directory_as_root()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "MineRailMonitor", "bin", "Debug", "net48");

        var paths = new ApplicationPaths(baseDirectory, explicitRootDirectory: null);

        Assert.Equal(Path.GetFullPath(baseDirectory), paths.RootDirectory, ignoreCase: true);
    }

    [Fact]
    public void Development_output_named_App_is_indistinguishable_from_installed_layout()
    {
        var paths = new ApplicationPaths(@"C:\Build\App\", explicitRootDirectory: null);

        Assert.Equal(Path.GetFullPath(@"C:\Build"), paths.RootDirectory, ignoreCase: true);
    }

    [Fact]
    public void Explicit_root_override_wins_over_base_directory()
    {
        var paths = new ApplicationPaths(
            @"C:\Build\bin\Debug\net48\",
            @"D:\Field\MineRailMonitor\");

        Assert.Equal(Path.GetFullPath(@"D:\Field\MineRailMonitor"), paths.RootDirectory, ignoreCase: true);
    }

    [Fact]
    public void Derived_directories_are_under_application_root()
    {
        var paths = new ApplicationPaths(@"C:\Field\MineRailMonitor\App\", null);

        Assert.Equal(Path.Combine(paths.RootDirectory, "App"), paths.AppDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Projects"), paths.ProjectsDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Data"), paths.DataDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Backups"), paths.BackupsDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Logs"), paths.LogsDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Docs"), paths.DocsDirectory);
        Assert.Equal(Path.Combine(paths.LogsDirectory, "BlackBox"), paths.BlackBoxDirectory);
        Assert.Equal(Path.Combine(paths.BackupsDirectory, "SQLite"), paths.SqliteBackupDirectory);
    }

    [Fact]
    public void Database_path_is_Root_Data_MineRailMonitor_db()
    {
        var paths = new ApplicationPaths(@"C:\Field\MineRailMonitor\", null);

        Assert.Equal(
            Path.Combine(paths.RootDirectory, "Data", "MineRailMonitor.db"),
            paths.DatabasePath);
    }

    [Fact]
    public void Paths_are_normalized_without_manual_dot_dot_segments()
    {
        var paths = new ApplicationPaths(
            @"C:\Build\bin\Debug\net48\",
            @".\artifacts\..\field\MineRailMonitor\");

        Assert.Equal(
            Path.GetFullPath(@"field\MineRailMonitor"),
            paths.RootDirectory,
            ignoreCase: true);
        Assert.DoesNotContain("..", paths.RootDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureWritableDirectories_creates_only_runtime_data_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));
        var paths = new ApplicationPaths(root, root);

        paths.EnsureWritableDirectories();

        Assert.True(Directory.Exists(paths.ProjectsDirectory));
        Assert.True(Directory.Exists(paths.DataDirectory));
        Assert.True(Directory.Exists(paths.SqliteBackupDirectory));
        Assert.True(Directory.Exists(paths.BlackBoxDirectory));
        Assert.True(Directory.Exists(paths.DocsDirectory));
        Assert.False(File.Exists(Path.Combine(paths.AppDirectory, "MineRailMonitor.exe")));
    }
}
```

The `Development_output_named_App_is_indistinguishable_from_installed_layout` test is intentional: under the approved contract, a base directory whose final name is exactly `App` is treated as installed layout, even if a development tool happens to use that directory name. Do not add heuristic detection beyond the explicit root override.

- [ ] **Step 2: Run the tests and verify the expected RED**

Run:

```powershell
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --filter "FullyQualifiedName~ApplicationPathsTests"
```

Expected result: compilation fails because `ApplicationPaths` does not exist. Do not add a test-only fake with the same public name.

- [ ] **Step 3: Implement the minimal path type**

Implement the following public surface:

```csharp
namespace MineRailMonitor.Infrastructure.Configuration;

public sealed class ApplicationPaths
{
    public ApplicationPaths(string baseDirectory, string? explicitRootDirectory);

    public string RootDirectory { get; }
    public string AppDirectory { get; }
    public string ProjectsDirectory { get; }
    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string BackupsDirectory { get; }
    public string SqliteBackupDirectory { get; }
    public string LogsDirectory { get; }
    public string BlackBoxDirectory { get; }
    public string DocsDirectory { get; }

    public void EnsureWritableDirectories();
}
```

Use `Path.GetFullPath` and `DirectoryInfo.Name`; do not concatenate `..\`. The resolution algorithm must be:

```csharp
var normalizedBase = NormalizeDirectory(baseDirectory);
var root = !string.IsNullOrWhiteSpace(explicitRootDirectory)
    ? NormalizeDirectory(explicitRootDirectory)
    : string.Equals(
        new DirectoryInfo(normalizedBase).Name,
        "App",
        StringComparison.OrdinalIgnoreCase)
        ? new DirectoryInfo(normalizedBase).Parent?.FullName
            ?? throw new ArgumentException("Installed App directory has no parent.", nameof(baseDirectory))
        : normalizedBase;

RootDirectory = NormalizeDirectory(root);
AppDirectory = Path.Combine(RootDirectory, "App");
ProjectsDirectory = Path.Combine(RootDirectory, "Projects");
DataDirectory = Path.Combine(RootDirectory, "Data");
DatabasePath = Path.Combine(DataDirectory, "MineRailMonitor.db");
BackupsDirectory = Path.Combine(RootDirectory, "Backups");
SqliteBackupDirectory = Path.Combine(BackupsDirectory, "SQLite");
LogsDirectory = Path.Combine(RootDirectory, "Logs");
BlackBoxDirectory = Path.Combine(LogsDirectory, "BlackBox");
DocsDirectory = Path.Combine(RootDirectory, "Docs");
```

`NormalizeDirectory` must preserve a drive root such as `C:\` while removing only trailing separators from non-root paths. `EnsureWritableDirectories()` may create Root, Projects, Data, Backups, SqliteBackupDirectory, Logs, BlackBoxDirectory and Docs; it must not grant ACLs or write to App.

- [ ] **Step 4: Run the Task 1 tests and verify GREEN**

Run the same targeted command. Expected: all `ApplicationPathsTests` pass with zero skipped tests.

- [ ] **Step 5: Run the Infrastructure regression suite**

```powershell
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build
```

Expected: existing Infrastructure tests remain green; no SQLite production file is changed by this task.

- [ ] **Step 6: Check the diff**

```powershell
git diff --check
git diff --name-only
```

Only `ApplicationPaths.cs` and `ApplicationPathsTests.cs` are allowed in this Task.

- [ ] **Step 7: Commit the Task 1 slice**

```powershell
git add src/MineRailMonitor.Infrastructure/Configuration/ApplicationPaths.cs tests/MineRailMonitor.Infrastructure.Tests/ApplicationPathsTests.cs
git commit -m "feat: add application path layout abstraction"
```

### Task 2: Wire production paths while preserving Acceptance isolation

**Files:**

- Modify: `src/MineRailMonitor/App.xaml.cs`
- Modify: `src/MineRailMonitor/MainWindow.xaml.cs`
- Modify: `tests/MineRailMonitor.Core.Tests/DatabaseStartupOwnershipMarkupTests.cs`

**Interfaces:**

- Consumes: `ApplicationPaths` from Task 1 and existing `AcceptanceCommandLineOptions`.
- Produces: `App.Paths`, production paths from `ApplicationPaths`, and unchanged Acceptance explicit paths.

- [ ] **Step 1: Add failing markup contracts**

Extend `DatabaseStartupOwnershipMarkupTests.cs` with assertions that pin both branches:

```csharp
[Fact]
public void App_exposes_application_paths_for_production_layout()
{
    var app = ReadApp();

    Assert.Contains("public ApplicationPaths Paths { get; }", app);
    Assert.Contains("new ApplicationPaths(AppContext.BaseDirectory, null)", app);
    Assert.Contains("Paths.DataDirectory", app);
    Assert.Contains("Paths.SqliteBackupDirectory", app);
    Assert.Contains("Paths.LogsDirectory", app);
}

[Fact]
public void Acceptance_keeps_explicit_database_and_log_paths()
{
    var app = ReadApp();

    Assert.Contains("AcceptanceOptions.DatabasePath!", app);
    Assert.Contains("AcceptanceOptions.LogDirectory!", ReadMainWindow());
}

[Fact]
public void MainWindow_uses_application_paths_for_production_projects_and_black_box()
{
    var mainWindow = ReadMainWindow();

    Assert.Contains("Paths.ProjectsDirectory", mainWindow);
    Assert.Contains("Paths.BlackBoxDirectory", mainWindow);
}
```

Update the existing direct AppContext assertions so they require `Paths.DataDirectory`, `Paths.SqliteBackupDirectory` and `Paths.LogsDirectory` for production, while retaining the Acceptance explicit-path assertions.

- [ ] **Step 2: Run the markup tests and verify RED**

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DatabaseStartupOwnershipMarkupTests"
```

Expected: the new path-wiring assertions fail because the current App and MainWindow still construct production paths directly from AppContext.BaseDirectory.

- [ ] **Step 3: Wire App without changing startup order**

Add the Infrastructure.Configuration using and property:

```csharp
public ApplicationPaths Paths { get; }

public App()
{
    AcceptanceOptions = AcceptanceCommandLineOptions.Parse(Environment.GetCommandLineArgs());
    Paths = new ApplicationPaths(AppContext.BaseDirectory, explicitRootDirectory: null);
    var logDirectory = AcceptanceOptions.Enabled
        ? AcceptanceOptions.LogDirectory!
        : Paths.LogsDirectory;
    Logger = new FileLogger(logDirectory);
    // Existing AdminModeService and event subscriptions remain unchanged.
}
```

In `StartProductionAsync`, replace only production path construction:

```csharp
Paths.EnsureWritableDirectories();
var dataDirectory = Paths.DataDirectory;
var productionDatabasePath = Paths.DatabasePath;
var backupRootDirectory = Paths.SqliteBackupDirectory;
```

Keep the existing health checker, backup service, recovery service, startup gate, coordinator construction, `TryRunStartupCatchUpAsync`, `StartHealthyAsync` and `CreateNewAsync` ordering unchanged. Acceptance continues constructing its Store with `AcceptanceOptions.DatabasePath!`.

- [ ] **Step 4: Wire MainWindow production directories only**

Change the production branch of the existing constructor and resolver without changing Acceptance:

```csharp
var appPaths = ((App)Application.Current).Paths;
_projectDirectory = _acceptanceOptions.Enabled
    ? Path.Combine(AppContext.BaseDirectory, "Projects", "Example")
    : ResolveProjectDirectory(appPaths.ProjectsDirectory);

var blackBoxRootDirectory = _acceptanceOptions.Enabled
    ? Path.Combine(_acceptanceOptions.LogDirectory!, "BlackBox")
    : appPaths.BlackBoxDirectory;
```

Keep `ResolveProjectDirectory` behavior identical after changing its parameter:

```csharp
private static string ResolveProjectDirectory(string projectsDirectory)
{
    var defaultDirectory = Path.Combine(projectsDirectory, "Default");
    return File.Exists(Path.Combine(defaultDirectory, "project.json"))
        ? defaultDirectory
        : Path.Combine(projectsDirectory, "Example");
}
```

- [ ] **Step 5: Run the targeted contracts and verify GREEN**

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DatabaseStartupOwnershipMarkupTests"
```

Expected: the updated startup ownership, production path, Acceptance isolation and MainWindow path tests pass with zero skipped tests.

- [ ] **Step 6: Run both test projects**

```powershell
dotnet build MineRailMonitor.sln -c Debug
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --no-build
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --no-build
```

Acceptance-specific tests must still use their explicit temporary DatabasePath and LogDirectory; no test may write to a real installed Root.

- [ ] **Step 7: Commit the Task 2 slice**

```powershell
git add src/MineRailMonitor/App.xaml.cs src/MineRailMonitor/MainWindow.xaml.cs tests/MineRailMonitor.Core.Tests/DatabaseStartupOwnershipMarkupTests.cs
git commit -m "feat: wire runtime paths through application layout"
```

### Task 3: Produce App-rooted Release staging while preserving Development/F5 Projects

**Files:**

- Modify: `src/MineRailMonitor/MineRailMonitor.csproj`
- Create: `scripts/build-desktop-package.ps1`
- Create: `tests/MineRailMonitor.Core.Tests/DesktopPublishLayoutMarkupTests.cs`

**Interfaces:**

- Consumes: Task 2 `ApplicationPaths` runtime layout and existing `dotnet publish` output.
- Produces: `artifacts/desktop-package` with `App`, `Projects\Example`, `Data`, `Backups`, `Logs`, `Docs` siblings; `Projects` is a filtered staging tree, not a copy of the repository Projects root.

- [ ] **Step 1: Add failing publish layout contracts**

Create markup tests that read the actual csproj and script:

```csharp
public sealed class DesktopPublishLayoutMarkupTests
{
    [Fact]
    public void Projects_remain_in_development_output_but_are_excluded_from_publish()
    {
        var project = ReadSource("src", "MineRailMonitor", "MineRailMonitor.csproj");

        Assert.Contains("CopyToOutputDirectory=\"PreserveNewest\"", project);
        Assert.Contains("CopyToPublishDirectory=\"Never\"", project);
    }

    [Fact]
    public void Package_script_publishes_App_and_copies_only_Example_to_Root()
    {
        var script = ReadSource("scripts", "build-desktop-package.ps1");

        Assert.Contains("dotnet publish", script);
        Assert.Contains("desktop-package", script);
        Assert.Contains("App", script);
        Assert.Contains("Projects\\Example", script);
        Assert.DoesNotContain("Join-Path $repoRoot \"Projects\\*\"", script);
        Assert.DoesNotContain("Copy-Item (Join-Path $repoRoot \"Projects\\*\")", script);
        Assert.Contains("App\\Projects", script);
        Assert.Contains("System.Data.SQLite.dll", script);
        Assert.Contains("x86\\SQLite.Interop.dll", script);
        Assert.Contains("x64\\SQLite.Interop.dll", script);
        Assert.Contains("throw", script);
    }
}
```

- [ ] **Step 2: Run the tests and verify RED**

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DesktopPublishLayoutMarkupTests"
```

Expected: the tests fail because publish currently includes Projects and the package script does not exist.

- [ ] **Step 3: Keep development copy and exclude publish copy**

Change only the existing `None Include="..\..\Projects\**\*"` item in `MineRailMonitor.csproj`:

```xml
<None Include="..\..\Projects\**\*"
      Link="Projects\%(RecursiveDir)%(Filename)%(Extension)"
      CopyToOutputDirectory="PreserveNewest"
      CopyToPublishDirectory="Never" />
```

This preserves `bin\Debug\...\Projects` for F5 while preventing `App\Projects` in publish output.

- [ ] **Step 4: Create the deterministic staging script**

Create `scripts/build-desktop-package.ps1` with this contract:

```powershell
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputRoot = (Join-Path $PSScriptRoot "..\artifacts\desktop-package")
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$stagingRoot = [IO.Path]::GetFullPath($OutputRoot)
$appDirectory = Join-Path $stagingRoot "App"
$projectsDirectory = Join-Path $stagingRoot "Projects"

if (Test-Path $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $appDirectory, $projectsDirectory,
    (Join-Path $stagingRoot "Data"),
    (Join-Path $stagingRoot "Backups\SQLite"),
    (Join-Path $stagingRoot "Logs\BlackBox"),
    (Join-Path $stagingRoot "Docs") | Out-Null

dotnet publish (Join-Path $repoRoot "src\MineRailMonitor\MineRailMonitor.csproj") `
    -c $Configuration -o $appDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$publishedProjects = Join-Path $appDirectory "Projects"
if (Test-Path $publishedProjects) {
    throw "Publish output must not contain App\Projects: $publishedProjects"
}

$managedSqlite = Join-Path $appDirectory "System.Data.SQLite.dll"
if (-not (Test-Path -LiteralPath $managedSqlite)) {
    throw "Missing managed SQLite runtime: $managedSqlite"
}

$nativeBuildRoot = Join-Path $repoRoot "src\MineRailMonitor\bin\$Configuration\net48"
$nativeFiles = @{
    x86 = Join-Path $nativeBuildRoot "x86\SQLite.Interop.dll"
    x64 = Join-Path $nativeBuildRoot "x64\SQLite.Interop.dll"
}
foreach ($architecture in $nativeFiles.Keys) {
    $nativeSource = $nativeFiles[$architecture]
    if (-not (Test-Path -LiteralPath $nativeSource)) {
        throw "Missing System.Data.SQLite native runtime: $nativeSource"
    }

    $nativeTargetDirectory = Join-Path $appDirectory $architecture
    New-Item -ItemType Directory -Force -Path $nativeTargetDirectory | Out-Null
    Copy-Item -LiteralPath $nativeSource -Destination (Join-Path $nativeTargetDirectory "SQLite.Interop.dll") -Force
}

$exampleSource = Join-Path $repoRoot "Projects\Example"
$exampleTarget = Join-Path $projectsDirectory "Example"

git -C $repoRoot diff --quiet -- "Projects/Example"
if ($LASTEXITCODE -ne 0) {
    throw "Tracked Projects/Example files contain uncommitted changes. Commit and review them before building a release package."
}

git -C $repoRoot diff --cached --quiet -- "Projects/Example"
if ($LASTEXITCODE -ne 0) {
    throw "Tracked Projects/Example files contain uncommitted changes. Commit and review them before building a release package."
}

$trackedExampleFiles = @(git -C $repoRoot ls-files -- "Projects/Example")
if ($LASTEXITCODE -ne 0) {
    throw "Failed to enumerate tracked Example project files."
}
if ($trackedExampleFiles.Count -eq 0) {
    throw "No tracked Example project files were found."
}
if ($trackedExampleFiles -notcontains "Projects/Example/project.json") {
    throw "Missing tracked sanitized Example project: Projects/Example/project.json"
}

foreach ($trackedPath in $trackedExampleFiles) {
    if (-not $trackedPath.StartsWith("Projects/Example/", [StringComparison]::Ordinal)) {
        throw "Unexpected tracked Example path: $trackedPath"
    }

    $relativeExamplePath = $trackedPath.Substring("Projects/Example/".Length).Replace("/", "\")
    $source = Join-Path $repoRoot ($trackedPath.Replace("/", "\"))
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Tracked Example source file is missing: $source"
    }

    $target = Join-Path $exampleTarget $relativeExamplePath
    $targetDirectory = Split-Path -Parent $target
    New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
}
Write-Host "Desktop package created at $stagingRoot"
```

The real publish inspection showed that `System.Data.SQLite.dll` is emitted by publish while the native files are emitted under the Release build output. The staging script therefore copies the exact existing `x86\SQLite.Interop.dll` and `x64\SQLite.Interop.dll` files into `App\x86` and `App\x64`; it must fail if either source is missing. The project copy is a tracked-file whitelist from `git ls-files -- Projects/Example`, so ignored/untracked files and local maps are excluded; tracked Example changes must be clean in both the worktree and index before packaging. The script owns only the artifact directory; it must not touch the live Root, Data, Logs, Backups or Projects directories.

- [ ] **Step 5: Run staging tests and inspect the real package**

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DesktopPublishLayoutMarkupTests"
powershell -ExecutionPolicy Bypass -File scripts/build-desktop-package.ps1 -Configuration Release
Test-Path artifacts/desktop-package/App/MineRailMonitor.exe
Test-Path artifacts/desktop-package/App/System.Data.SQLite.dll
Test-Path artifacts/desktop-package/App/x86/SQLite.Interop.dll
Test-Path artifacts/desktop-package/App/x64/SQLite.Interop.dll
Test-Path artifacts/desktop-package/Projects/Example/project.json
Test-Path artifacts/desktop-package/Projects/Default
Test-Path artifacts/desktop-package/App/Projects
```

Expected: markup tests pass; the executable/runtime and `Projects/Example/project.json` paths return `True`; `Projects/Default` and `App/Projects` return `False`; all root-level runtime directories exist.

- [ ] **Step 6: Run the complete regression set**

```powershell
dotnet build MineRailMonitor.sln -c Release
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Release --no-build
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Release --no-build
powershell -ExecutionPolicy Bypass -File scripts/run-rfid-acceptance.ps1
```

Expected: build has 0 warnings / 0 errors, both suites have 0 skipped tests, and Acceptance is 8/8.

- [ ] **Step 7: Commit the Task 3 slice**

```powershell
git add src/MineRailMonitor/MineRailMonitor.csproj scripts/build-desktop-package.ps1 tests/MineRailMonitor.Core.Tests/DesktopPublishLayoutMarkupTests.cs
git commit -m "feat: add desktop release staging layout"
```

### Task 4: Add the Inno Setup installer for the Root layout

**Files:**

- Create: `installer/MineRailMonitor.iss`
- Create: `tests/MineRailMonitor.Core.Tests/DesktopInstallerMarkupTests.cs`

**Interfaces:**

- Consumes: `artifacts/desktop-package` from Task 3.
- Produces: an installer whose `{app}` is the user-selected `<Root>` and whose shortcut targets `{app}\App\MineRailMonitor.exe`.

The installer consumes only the generated staging tree. It may copy the filtered staging `Projects\*` directory to `{app}\Projects`, but it must never read the repository `Projects` root directly. The staging whitelist in Task 3 is the control that prevents `Default`, local maps/stations and customer projects from entering the installer input.

- [ ] **Step 1: Add failing installer contract tests**

Create source-contract tests:

```csharp
public sealed class DesktopInstallerMarkupTests
{
    [Fact]
    public void Installer_uses_C_default_and_selected_directory_as_Root()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("DefaultDirName=C:\\MineRailMonitor", script);
        Assert.Contains("{app}\\App\\MineRailMonitor.exe", script);
        Assert.DoesNotContain("{app}\\MineRailMonitor", script);
    }

    [Fact]
    public void Installer_deploys_App_and_root_level_Projects()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("desktop-package\\App", script);
        Assert.Contains("{app}\\App", script);
        Assert.Contains("desktop-package\\Projects", script);
        Assert.Contains("{app}\\Projects", script);
        Assert.Contains("onlyifdoesntexist", script);
    }

    [Fact]
    public void Installer_creates_only_the_documented_desktop_shortcut()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("矿车编组监控系统", script);
        Assert.Contains("{autodesktop}", script);
        Assert.DoesNotContain("Service", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Registry Run", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installer_uses_stable_AppId_and_checks_DotNet_Framework_48()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("AppId={{8C8B1CB5-4A4B-4B4A-9D48-6A3C93D2F0E1}", script);
        Assert.Contains("InitializeSetup", script);
        Assert.Contains("NET Framework Setup\\NDP\\v4\\Full", script);
        Assert.Contains("Release", script);
        Assert.Contains("528040", script);
        Assert.Contains("请先安装 .NET Framework 4.8", script);
    }
}
```

- [ ] **Step 2: Run the tests and verify RED**

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DesktopInstallerMarkupTests"
```

Expected: tests fail because `installer/MineRailMonitor.iss` does not exist.

- [ ] **Step 3: Create the minimal Inno Setup script**

Create `installer/MineRailMonitor.iss` with a stable AppId and `{app}` as the selected Root:

```ini
#define MyAppName "MineRailMonitor"
#define MyAppVersion "1.0.0"

[Setup]
AppId={{8C8B1CB5-4A4B-4B4A-9D48-6A3C93D2F0E1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName=C:\MineRailMonitor
UsePreviousAppDir=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\artifacts\installer
OutputBaseFilename=MineRailMonitor-Setup
DisableProgramGroupPage=yes
Uninstallable=yes

[Files]
Source: "..\artifacts\desktop-package\App\*"; DestDir: "{app}\App"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\artifacts\desktop-package\Projects\*"; DestDir: "{app}\Projects"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist
Source: "..\artifacts\desktop-package\Docs\*"; DestDir: "{app}\Docs"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist skipifsourcedoesntexist

[Dirs]
Name: "{app}"
Name: "{app}\App"
Name: "{app}\Data"
Name: "{app}\Backups\SQLite"
Name: "{app}\Logs\BlackBox"
Name: "{app}\Projects"
Name: "{app}\Docs"

[Icons]
Name: "{autodesktop}\矿车编组监控系统"; Filename: "{app}\App\MineRailMonitor.exe"

[Run]
Filename: "{app}\App\MineRailMonitor.exe"; Description: "立即启动 MineRailMonitor"; Flags: postinstall nowait skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}\App"

[Registry]
Root: HKLM; Subkey: "Software\MineRailMonitor"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletekeyifempty

[Code]
const
  DotNet48MinimumRelease = 528040;

var
  DeleteFieldData: Boolean;

function HasDotNet48(): Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if IsWin64 then
    Result := RegQueryDWordValue(HKLM64,
      'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
      'Release', Release) and (Release >= DotNet48MinimumRelease);
  if not Result then
    Result := RegQueryDWordValue(HKLM,
      'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
      'Release', Release) and (Release >= DotNet48MinimumRelease);
end;

function InitializeSetup(): Boolean;
begin
  Result := HasDotNet48;
  if not Result then
    MsgBox('本机未检测到 .NET Framework 4.8，请先安装 .NET Framework 4.8。',
      mbCriticalError, MB_OK);
end;
```

The default Root is `C:\MineRailMonitor`, but the standard directory selection page allows `D:\MineRailMonitor` or `E:\Software\MineRailMonitor`; `{app}` remains exactly the selected directory. The AppId line intentionally has two opening braces and one closing brace because the GUID is a literal Inno Setup value. Do not add service, startup, scheduled-task or Registry Run entries. Task 5 extends this same `[Code]` section; it must not add a second `[Code]` section.

- [ ] **Step 4: Compile and inspect the installer script**

On a Windows machine with Inno Setup installed, run:

```powershell
iscc.exe installer\MineRailMonitor.iss
```

Expected: `artifacts\installer\MineRailMonitor-Setup.exe` is created without compiler errors. The script must not be executed against a real field Root during this Task.

- [ ] **Step 5: Run installer contract tests**

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DesktopInstallerMarkupTests"
```

Expected: all installer contract tests pass with zero skipped tests.

- [ ] **Step 6: Check scope**

```powershell
git diff --check
git diff --name-only
```

Only `installer/MineRailMonitor.iss` and `DesktopInstallerMarkupTests.cs` are allowed in this Task.

- [ ] **Step 7: Commit the Task 4 slice**

```powershell
git add installer/MineRailMonitor.iss tests/MineRailMonitor.Core.Tests/DesktopInstallerMarkupTests.cs
git commit -m "feat: add desktop installer layout"
```

### Task 5: Enforce ACL, upgrade directory reuse and uninstall retention

**Files:**

- Modify: `installer/MineRailMonitor.iss`
- Modify: `tests/MineRailMonitor.Core.Tests/DesktopInstallerMarkupTests.cs`

**Interfaces:**

- Consumes: Task 4 stable AppId, `{app}` Root and staging layout.
- Produces: effective ACL normalization, same-Root upgrade reuse, changed-Root blocking and uninstall retention behavior.

- [ ] **Step 1: Add failing retention and ACL tests**

Add these source-contract tests:

```csharp
[Fact]
public void Installer_grants_Modify_only_to_runtime_data_directories()
{
    var script = ReadSource("installer", "MineRailMonitor.iss");

    Assert.Contains("icacls.exe", script);
    Assert.Contains("/reset", script);
    Assert.Contains("/reset /T /C", script);
    Assert.Contains("/inheritance:r", script);
    Assert.Contains("*S-1-5-18", script);
    Assert.Contains("*S-1-5-32-544", script);
    Assert.Contains("*S-1-5-32-545", script);
    Assert.Contains("(OI)(CI)(RX)", script);
    Assert.Contains("(OI)(CI)(M)", script);
    Assert.DoesNotContain("Permissions: users-modify", script);
}

[Fact]
public void Installer_reuses_previous_Root_on_upgrade()
{
    var script = ReadSource("installer", "MineRailMonitor.iss");

    Assert.Contains("UsePreviousAppDir=yes", script);
    Assert.Contains("DefaultDirName=C:\\MineRailMonitor", script);
    Assert.Contains("当前版本不支持升级时迁移安装目录", script);
    Assert.Contains("Result := False", script);
}

[Fact]
public void Uninstall_keeps_field_data_by_default_and_requires_explicit_confirmation()
{
    var script = ReadSource("installer", "MineRailMonitor.iss");

    Assert.Contains("[UninstallDelete]", script);
    Assert.Contains("{app}\\App", script);
    Assert.DoesNotContain("Type: filesandordirs; Name: \"{app}\\Data\"", script);
    Assert.DoesNotContain("Type: filesandordirs; Name: \"{app}\\Projects\"", script);
    Assert.Contains("InitializeUninstall", script);
    Assert.Contains("是否同时删除现场数据和历史记录", script);
    Assert.Contains("将删除 Projects、Data、Backups、Logs 和 Docs 中的现场文件", script);
    Assert.Contains("DeleteFieldData := False", script);
    Assert.Contains("DelTree(ExpandConstant('{app}\\Data')", script);
    Assert.Contains("DelTree(ExpandConstant('{app}\\Projects')", script);
    Assert.Contains("DelTree(ExpandConstant('{app}\\Docs')", script);
    Assert.DoesNotContain("DelTree(ExpandConstant('{app}')", script);
}
```

- [ ] **Step 2: Run the new tests and verify RED**

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DesktopInstallerMarkupTests"
```

Expected: the new upgrade/uninstall contract fails because the initial installer script has no explicit cross-directory warning or optional field-data deletion hook.

- [ ] **Step 3: Normalize effective ACL and add explicit uninstall data choice**

Do not use Inno `Permissions` entries as the ACL implementation. Extend the existing `[Code]` section with helpers that invoke the Windows system `icacls.exe` using stable SIDs. First run `icacls <Root> /reset`; then run `/inheritance:r` and `/grant:r` on Root. For App, Data, Backups, Logs, Projects and Docs, first run `icacls <directory> /reset /T /C` so stale explicit ACEs in the subtree are removed, then run `/inheritance:r` and `/grant:r` on that top-level directory. The helper must fail the installation if any command returns a non-zero exit code. It must never assume that `/grant:r` removes ACEs for principals not named in the new grants.

Use these exact rights: Root and App receive SYSTEM/Administrators `(OI)(CI)(F)` plus Users `(OI)(CI)(RX)`; Data, Backups, Logs, Projects and Docs receive SYSTEM/Administrators `(OI)(CI)(F)` plus Users `(OI)(CI)(M)`. The helper may be called from `CurStepChanged(ssPostInstall)` after the directories exist. It must not modify any parent or system directory. The required order is Root reset/protect first, then each managed subtree reset recursively and protect; every non-zero exit code is an installation failure.

Keep the explicit uninstall data choice in the same `[Code]` section.

Extend the existing `[Code]` section with the following effective-ACL and uninstall logic. Add the SID constants to Task 4's existing `const` block, keep `DeleteFieldData` in its existing `var` block, and add the functions below after the prerequisite functions; do not create a second `[Code]`, `const` or `var` section. The ACL helper uses only the target directory paths; it never calls `icacls` on a parent of `{app}`:

```pascal
  SidSystem = '*S-1-5-18';
  SidAdministrators = '*S-1-5-32-544';
  SidUsers = '*S-1-5-32-545';

function RunIcacls(const Parameters: String): Boolean;
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{sys}\icacls.exe'), Parameters, '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := False
  else
    Result := ResultCode = 0;
end;

function SetEffectiveAcl(const DirectoryName, UserRights: String;
  ResetTree: Boolean): Boolean;
var
  ResetParameters: String;
  Parameters: String;
begin
  ResetParameters := '"' + DirectoryName + '" /reset';
  if ResetTree then
    ResetParameters := ResetParameters + ' /T /C';
  if not RunIcacls(ResetParameters) then begin
    Result := False;
    exit;
  end;

  if not RunIcacls('"' + DirectoryName + '" /inheritance:r') then begin
    Result := False;
    exit;
  end;

  Parameters := '"' + DirectoryName + '" /grant:r ' +
    '"' + SidSystem + ':(OI)(CI)(F)" ' +
    '"' + SidAdministrators + ':(OI)(CI)(F)" ' +
    '"' + SidUsers + ':(OI)(CI)(' + UserRights + ')"';
  Result := RunIcacls(Parameters);
end;

function ApplyMineRailMonitorAcl(): Boolean;
begin
  Result :=
    SetEffectiveAcl(ExpandConstant('{app}'), 'RX', False) and
    SetEffectiveAcl(ExpandConstant('{app}\App'), 'RX', True) and
    SetEffectiveAcl(ExpandConstant('{app}\Data'), 'M', True) and
    SetEffectiveAcl(ExpandConstant('{app}\Backups'), 'M', True) and
    SetEffectiveAcl(ExpandConstant('{app}\Logs'), 'M', True) and
    SetEffectiveAcl(ExpandConstant('{app}\Projects'), 'M', True) and
    SetEffectiveAcl(ExpandConstant('{app}\Docs'), 'M', True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not ApplyMineRailMonitorAcl then begin
    MsgBox('无法规范化 MineRailMonitor 目录权限，安装将停止。',
      mbCriticalError, MB_OK);
    Abort;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteFieldData := MsgBox(
    '是否同时删除现场数据和历史记录？',
    mbConfirmation,
    MB_YESNO) = IDYES;
  if DeleteFieldData and
     (MsgBox(
       '将删除 Projects、Data、Backups、Logs 和 Docs 中的现场文件，是否继续？',
       mbConfirmation,
       MB_YESNO) = IDNO) then
    DeleteFieldData := False;
  Result := True;
end;

procedure CurUninstallStepChanged(Changes: TUninstallStep);
begin
  if (Changes = usPostUninstall) and DeleteFieldData then begin
    DelTree(ExpandConstant('{app}\Projects'), True, True, True);
    DelTree(ExpandConstant('{app}\Data'), True, True, True);
    DelTree(ExpandConstant('{app}\Backups'), True, True, True);
    DelTree(ExpandConstant('{app}\Logs'), True, True, True);
    DelTree(ExpandConstant('{app}\Docs'), True, True, True);
  end;
end;
```

The first `No` response leaves field data in place and continues uninstall. If the second confirmation is `No`, the code explicitly resets `DeleteFieldData := False` and still returns `Result := True`; it does not cancel uninstall. Only two `Yes` responses delete the five concrete field-data directories. There is no `DelTree(ExpandConstant('{app}'))`; the normal uninstall list removes App, shortcut and registration, and Inno may remove an empty Root afterward.

- [ ] **Step 4: Add changed-root upgrade warning without migration**

Add an Inno Setup `NextButtonClick` guard for the directory selection page. It must compare the selected `{app}` with the previous install directory and block when they differ:

```pascal
function NextButtonClick(CurPageID: Integer): Boolean;
var
  PreviousRoot: String;
begin
  Result := True;
  if CurPageID <> wpSelectDir then
    exit;

  PreviousRoot := ExpandConstant('{reg:HKLM\\Software\\MineRailMonitor,InstallLocation|}');
  if (PreviousRoot <> '') and
     (CompareText(ExpandConstant('{app}'), PreviousRoot) <> 0) then begin
    MsgBox(
      '已安装版本位于 ' + PreviousRoot + '。当前版本不支持升级时迁移安装目录。' +
      '请继续使用原安装目录；如需迁移，请先完成独立的数据迁移流程。',
      mbCriticalError,
      MB_OK);
    Result := False;
  end;
end;
```

The exact registry value name used by the installer must be kept stable with its AppId registration. This hook blocks a changed Root; it must not copy field data, move Backups or rewrite recovery markers. `UsePreviousAppDir=yes` remains the default same-root upgrade path.

- [ ] **Step 5: Compile and test the installer contract**

```powershell
iscc.exe installer\MineRailMonitor.iss
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DesktopInstallerMarkupTests"
```

Expected: Inno Setup compilation succeeds and all ACL/upgrade/uninstall tests pass.

Before installing on a disposable Root, deliberately create a hostile pre-existing ACL. The root must already exist before the installer runs:

```powershell
$Root = 'D:\MineRailMonitor-ACL-test'
New-Item -ItemType Directory -Force -Path $Root | Out-Null
icacls $Root /grant:r "*S-1-1-0:(OI)(CI)(M)"

# Run the installer and select $Root exactly; do not create a nested MineRailMonitor directory.
icacls $Root
icacls (Join-Path $Root 'App')
icacls (Join-Path $Root 'Data')
icacls (Join-Path $Root 'Backups')
icacls (Join-Path $Root 'Logs')
icacls (Join-Path $Root 'Projects')
icacls (Join-Path $Root 'Docs')
```

After installation, use `icacls` to confirm Root/App have no `Everyone:(M)` and no unknown explicit write ACE, and that Users has only RX there; confirm Users has M on Data/Backups/Logs/Projects/Docs. Using a standard non-administrator account, create a file in Data and Logs, create a backup fixture, modify a Projects configuration, and create a Docs document; then verify attempts to modify and delete `App\MineRailMonitor.exe` and an App DLL fail. The ACL text assertions are not sufficient evidence; these commands and file operations are required on the disposable installation.

- [ ] **Step 6: Run the application regression set**

```powershell
dotnet build MineRailMonitor.sln -c Release
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Release --no-build
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Release --no-build
powershell -ExecutionPolicy Bypass -File scripts/run-rfid-acceptance.ps1
```

Expected: 0 warnings / 0 errors, zero skipped tests and Acceptance 8/8. Do not run uninstall deletion against real field data.

- [ ] **Step 7: Commit the Task 5 slice**

```powershell
git add installer/MineRailMonitor.iss tests/MineRailMonitor.Core.Tests/DesktopInstallerMarkupTests.cs
git commit -m "feat: preserve desktop field data across upgrades"
```

### Task 6: Verify the complete package on clean Windows

**Files:**

- Create: `scripts/verify-desktop-package.ps1`
- Modify: none
- Test: clean Windows VM or target industrial PC manual checklist

**Interfaces:**

- Consumes: compiled installer from Task 5 and `artifacts/desktop-package`.
- Produces: repeatable staging-layout checks and an installation acceptance record; it does not change the live repository or field data.

- [ ] **Step 1: Add the package verifier**

Create a verifier that fails fast on the required layout and prohibited nesting:

```powershell
param([Parameter(Mandatory = $true)][string]$StagingRoot)

$root = [IO.Path]::GetFullPath($StagingRoot)
$required = @(
    (Join-Path $root "App\MineRailMonitor.exe"),
    (Join-Path $root "App\System.Data.SQLite.dll"),
    (Join-Path $root "App\x86\SQLite.Interop.dll"),
    (Join-Path $root "App\x64\SQLite.Interop.dll"),
    (Join-Path $root "Projects"),
    (Join-Path $root "Projects\Example\project.json"),
    (Join-Path $root "Data"),
    (Join-Path $root "Backups"),
    (Join-Path $root "Logs"),
    (Join-Path $root "Docs")
)

foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing desktop package path: $path"
    }
}

$nestedProjects = Join-Path $root "App\Projects"
if (Test-Path -LiteralPath $nestedProjects) {
    throw "Projects must be a Root sibling, not App\Projects"
}

$defaultProject = Join-Path $root "Projects\Default"
if (Test-Path -LiteralPath $defaultProject) {
    throw "Clean staging must not contain Projects\Default"
}

$projectDirectories = @(Get-ChildItem -LiteralPath (Join-Path $root "Projects") -Directory)
if ($projectDirectories.Count -ne 1 -or $projectDirectories[0].Name -ne "Example") {
    throw "Clean staging must contain only the Example project"
}

Write-Host "Desktop package layout verified: $root"
```

- [ ] **Step 2: Run the automated verification commands**

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-desktop-package.ps1 -Configuration Release
powershell -ExecutionPolicy Bypass -File scripts/verify-desktop-package.ps1 -StagingRoot artifacts/desktop-package
dotnet build MineRailMonitor.sln -c Release
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Release --no-build
dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Release --no-build
powershell -ExecutionPolicy Bypass -File scripts/run-rfid-acceptance.ps1
```

Expected: Release build has 0 warnings / 0 errors, Core and Infrastructure suites have 0 skipped tests, and Acceptance reports 8/8 PASS.

- [ ] **Step 3: Execute clean Windows installation acceptance**

On a clean Windows VM or target machine, record each result without using the real production directory for destructive tests:

1. On clean Snapshot A, run the installer with the default `C:\MineRailMonitor`.
2. On a machine/snapshot without .NET Framework 4.8 Full Release, confirm `InitializeSetup` blocks installation and shows the Chinese prerequisite message; on a machine satisfying `Release >= 528040`, confirm installation proceeds.
3. Confirm the desktop shortcut is `矿车编组监控系统` and targets `<Root>\App\MineRailMonitor.exe`.
4. Start as a normal user; confirm no elevation prompt and no service/autostart/scheduled task/Registry Run entry.
5. On a disposable pre-created Root such as `D:\MineRailMonitor-ACL-test`, grant `*S-1-1-0:(OI)(CI)(M)` to simulate hostile `Everyone:(M)` before installation. Install into that exact Root, then use `icacls` to confirm the hostile and other unknown explicit write ACEs are gone, Root/App Users has only RX, and Data/Logs/Projects/Docs Users has M.
6. Before installation, add an untracked disposable `Projects\Default` and a customer project under a separate local checkout copy; build staging and confirm only `Projects\Example\project.json` is present, `Projects\Default` and customer content are absent, and `App\Projects` is absent. The same staging check must use the real whitelist script, not only markup assertions.
7. As the same non-admin user in that hostile-ACL installation, confirm creating files in Data, Logs and Docs and modifying a Projects configuration succeeds, while modifying or deleting `App\MineRailMonitor.exe` and an App DLL fails. Confirm the package contains `System.Data.SQLite.dll`, `App\x86\SQLite.Interop.dll` and `App\x64\SQLite.Interop.dll`.
8. On independent clean Snapshot B, choose `D:\MineRailMonitor` during first install and confirm `D:\MineRailMonitor\App`, `Data`, `Backups`, `Logs`, `Projects\Example` and `Docs` exist without `D:\MineRailMonitor\MineRailMonitor` or `Projects\Default`.
9. Start the clean installation before any field `Default` project is supplied; confirm existing project selection falls back to `Example`. Then add a field-provided `Projects\Default` through the supported configuration process and confirm existing selection logic prefers `Default`; do not alter MainWindow fallback code for this acceptance.
10. Upgrade the first installation without changing the directory; confirm the previous Root is reused and Data, Projects, Logs and Backups remain unchanged.
11. During that same-AppId upgrade, deliberately select a different Root; confirm the installer blocks with the migration-not-supported message and no old field data is copied or overwritten.
12. Uninstall with the default field-data choice; confirm App and shortcut are removed while Projects, Data, Backups, Logs and Docs remain.
13. Repeat on a disposable snapshot with both explicit confirmations; confirm only the concrete field-data directories are removed and no unsafe whole-Root deletion occurs.
14. Reinstall using the retained data directory and confirm the existing database and project configuration are usable.

- [ ] **Step 4: Inspect migration/recovery boundaries**

Before any directory-copy test, stop the application cleanly. If a recovery marker exists, record it as a blocked migration case; the installer must not rewrite absolute marker paths or silently resume a moved recovery. Validate SQLite health, backup and recovery behavior using the existing application contracts rather than adding installer-specific database logic.

- [ ] **Step 5: Run final repository checks**

```powershell
git diff --check
git status --short --branch
git diff main...HEAD -- src scripts installer tests
```

Expected: all code and installer changes are explicitly tied to this plan, no SQLite/RFID/alarm production semantics changed, and the worktree is clean before the final review.

- [ ] **Step 6: Commit the Task 6 slice**

```powershell
git add scripts/verify-desktop-package.ps1
git commit -m "test: verify desktop installation package"
```

## Commit Strategy

The implementation worker should keep each Task commit independently reviewable. This documentation follow-up is committed with:

```powershell
git add docs/superpowers/specs/2026-09-21-desktop-install-layout-design.md docs/superpowers/plans/2026-09-21-desktop-install-layout.md
git commit -m "docs: restrict desktop package to example project"
git push origin feat/desktop-install-layout
```

Do not execute Task 1 from the plan in the plan-writing phase. Do not create a new implementation branch. Do not merge main.

## Plan Self-Review

- Spec sections 1–4 map to Global Constraints, Repo Mapping, Task 1 and Task 2.
- Spec section 5 maps to Task 3 publish handling and Task 5 upgrade protection.
- Spec sections 6–8 map to Task 2 runtime paths and Task 6 verification; SQLite implementation remains unchanged.
- Spec sections 9–11 map to Task 4 installer layout and Task 5 upgrade/uninstall behavior.
- Spec section 12 maps to Task 1 root resolution and Task 6 recovery-marker migration boundary.
- Spec section 13 maps to Task 5 SID-based effective ACL normalization and Task 6 real permission checks.
- Spec section 14 maps to Task 4 Inno Setup selection, fixed AppId and .NET Framework 4.8 prerequisite.
- Spec section 15 maps to the six Tasks in this plan.
- Spec section 16 maps to Task 6 automated and manual acceptance.
- Spec section 17 maps to the scope constraints in this plan.
- Placeholder scan must find no placeholder markers, vague implementation steps, or unassigned Task reference.
- Type consistency: `ApplicationPaths` is created in Task 1, exposed as `App.Paths` in Task 2, consumed by staging/runtime wiring in Task 3, and never referenced by installer Pascal code.
- Review Focus items are assigned to Task 1, Task 2, Task 3, Task 4, Task 5 and Task 6 as listed above.
- Effective-permission coverage is based on Root `/reset`, managed-subtree `/reset /T /C`, then protected `/inheritance:r /grant:r` with stable SIDs plus real non-admin file-operation checks; it includes a pre-existing `Everyone:(M)` hostile ACL and no markup test is treated as final ACL evidence.
- Upgrade coverage blocks changed Root for the same stable AppId; no same-AppId side-by-side test or cross-Root migration is planned.
- Publish coverage records the observed output: `System.Data.SQLite.dll` is in publish, while x86/x64 `SQLite.Interop.dll` are copied from the actual Release build output into staging and then verified.
- Publish coverage uses an explicit `Projects\Example` whitelist, verifies `Projects\Example\project.json`, rejects `Projects\Default`/customer content and `App\Projects`, and never reads the raw repository Projects root; the installer consumes only this filtered staging tree.
- ApplicationRoot boundary coverage explicitly documents that a development BaseDirectory named `App` is indistinguishable from installed layout under the approved contract.
