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
- SQLite WAL / FULL / health / backup / recovery / marker 语义不变。
- 不重新设计 SQLite，不修改 RFID protocol、CRC、Byte7、业务报警逻辑。
- 升级默认复用上次安装 Root。
- 不静默迁移跨目录 Data / Projects / Backups / Logs。
- 升级不得覆盖现场 Projects，不得删除 Data / Backups / Logs。
- 卸载默认保留 Projects、Data、Backups、Logs。
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
  - 当前存在 `project.json`、`stations/560.json`、`stations/620.json`，开发输出仍需要保留模板复制。
- 当前没有 `installer/` 目录；Task 4 创建它。

## Review Focus

1. BaseDirectory 最终目录名为 `App` 时必须取 parent，且 `App` 大小写不敏感；BaseDirectory 为开发输出目录时不能错误取 parent。测试归属 Task 1。
2. 显式 ApplicationRoot 必须覆盖自动推导，且相对/绝对路径都必须先标准化；测试归属 Task 1。
3. 开发输出必须继续包含 `Projects`，正式 staging 的 `App` 不能再包含 `Projects`；测试归属 Task 3。
4. 用户选中的目录必须直接成为 Root，不能生成重复 `MineRailMonitor` 子目录；测试归属 Task 4。
5. 升级必须复用上次 Root，改变 Root 时不得静默复制或覆盖现场数据；测试归属 Task 5。
6. 普通 Users 只能 Modify 数据目录，不能 Modify App 中的 exe/dll；测试归属 Task 5。
7. Acceptance 显式 DatabasePath / LogDirectory 必须继续隔离真实安装目录；测试归属 Task 2 和 Task 6。

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
- Produces: `artifacts/desktop-package` with `App`, `Projects`, `Data`, `Backups`, `Logs`, `Docs` siblings.

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
    public void Package_script_publishes_App_and_copies_Projects_to_Root()
    {
        var script = ReadSource("scripts", "build-desktop-package.ps1");

        Assert.Contains("dotnet publish", script);
        Assert.Contains("desktop-package", script);
        Assert.Contains("App", script);
        Assert.Contains("Projects", script);
        Assert.Contains("App\\Projects", script);
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

Copy-Item (Join-Path $repoRoot "Projects\*") $projectsDirectory -Recurse -Force
Write-Host "Desktop package created at $stagingRoot"
```

The script owns only the artifact directory; it must not touch the live Root, Data, Logs, Backups or Projects directories.

- [ ] **Step 5: Run staging tests and inspect the real package**

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DesktopPublishLayoutMarkupTests"
powershell -ExecutionPolicy Bypass -File scripts/build-desktop-package.ps1 -Configuration Release
Test-Path artifacts/desktop-package/App/MineRailMonitor.exe
Test-Path artifacts/desktop-package/Projects/Example/project.json
Test-Path artifacts/desktop-package/App/Projects
```

Expected: markup tests pass; the first two `Test-Path` calls return `True`; the last returns `False`; all root-level runtime directories exist.

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
AppId={{8C8B1CB5-4A4B-4B4A-9D48-6A3C93D2F0E1}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName=C:\MineRailMonitor
UsePreviousAppDir=yes
PrivilegesRequired=admin
OutputDir=..\artifacts\installer
OutputBaseFilename=MineRailMonitor-Setup
DisableProgramGroupPage=yes
Uninstallable=yes

[Files]
Source: "..\artifacts\desktop-package\App\*"; DestDir: "{app}\App"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\artifacts\desktop-package\Projects\*"; DestDir: "{app}\Projects"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist
Source: "..\artifacts\desktop-package\Docs\*"; DestDir: "{app}\Docs"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist skipifsourcedoesntexist

[Dirs]
Name: "{app}"; Permissions: users-readexec
Name: "{app}\App"; Permissions: users-readexec
Name: "{app}\Data"; Permissions: users-modify
Name: "{app}\Backups\SQLite"; Permissions: users-modify
Name: "{app}\Logs\BlackBox"; Permissions: users-modify
Name: "{app}\Projects"; Permissions: users-modify
Name: "{app}\Docs"; Permissions: users-modify

[Icons]
Name: "{autodesktop}\矿车编组监控系统"; Filename: "{app}\App\MineRailMonitor.exe"

[Run]
Filename: "{app}\App\MineRailMonitor.exe"; Description: "立即启动 MineRailMonitor"; Flags: postinstall nowait skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}\App"

[Registry]
Root: HKLM; Subkey: "Software\MineRailMonitor"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletekeyifempty
```

The default Root is `C:\MineRailMonitor`, but the standard directory selection page allows `D:\MineRailMonitor` or `E:\Software\MineRailMonitor`; `{app}` remains exactly the selected directory. Do not add service, startup, scheduled-task or Registry Run entries.

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
- Produces: explicit ACL and retention behavior for install, upgrade, changed-root install and uninstall.

- [ ] **Step 1: Add failing retention and ACL tests**

Add these source-contract tests:

```csharp
[Fact]
public void Installer_grants_Modify_only_to_runtime_data_directories()
{
    var script = ReadSource("installer", "MineRailMonitor.iss");

    Assert.Contains("{app}\\Data\"; Permissions: users-modify", script);
    Assert.Contains("{app}\\Backups\\SQLite\"; Permissions: users-modify", script);
    Assert.Contains("{app}\\Logs\\BlackBox\"; Permissions: users-modify", script);
    Assert.Contains("{app}\\Projects\"; Permissions: users-modify", script);
    Assert.Contains("{app}\"; Permissions: users-readexec", script);
    Assert.Contains("{app}\\App\"; Permissions: users-readexec", script);
    Assert.DoesNotContain("{app}\"; Permissions: users-modify", script);
    Assert.DoesNotContain("{app}\\App\"; Permissions: users-modify", script);
}

[Fact]
public void Installer_reuses_previous_Root_on_upgrade()
{
    var script = ReadSource("installer", "MineRailMonitor.iss");

    Assert.Contains("UsePreviousAppDir=yes", script);
    Assert.Contains("DefaultDirName=C:\\MineRailMonitor", script);
    Assert.Contains("旧 Data / Projects / Backups / Logs 不自动迁移", script);
}

[Fact]
public void Uninstall_keeps_field_data_by_default_and_requires_explicit_confirmation()
{
    var script = ReadSource("installer", "MineRailMonitor.iss");

    Assert.Contains("[UninstallDelete]", script);
    Assert.Contains("{app}\\App", script);
    Assert.DoesNotContain("Name: \"{app}\\Data\"", script);
    Assert.DoesNotContain("Name: \"{app}\\Projects\"", script);
    Assert.Contains("InitializeUninstall", script);
    Assert.Contains("是否同时删除现场数据和历史记录", script);
    Assert.Contains("DelTree(ExpandConstant('{app}\\Data')", script);
    Assert.Contains("DelTree(ExpandConstant('{app}\\Projects')", script);
}
```

- [ ] **Step 2: Run the new tests and verify RED**

```powershell
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DesktopInstallerMarkupTests"
```

Expected: the new upgrade/uninstall contract fails because the initial installer script has no explicit cross-directory warning or optional field-data deletion hook.

- [ ] **Step 3: Keep App read-only and add explicit uninstall data choice**

Keep ACL entries only on Data, Backups, Logs, Projects and Docs. Do not add `Permissions: users-modify` to Root or App.

Add a real Inno Setup uninstall hook after `[UninstallDelete]`:

```pascal
[Code]
var
  DeleteFieldData: Boolean;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteFieldData := MsgBox(
    '是否同时删除现场数据和历史记录？',
    mbConfirmation,
    MB_YESNO) = IDYES;
  if DeleteFieldData then
    Result := MsgBox(
      '将删除 Projects、Data、Backups 和 Logs，是否继续？',
      mbError,
      MB_YESNO) = IDYES;
end;

procedure CurUninstallStepChanged(Changes: TUninstallStep);
begin
  if (Changes = usPostUninstall) and DeleteFieldData then begin
    DelTree(ExpandConstant('{app}\Projects'), True, True, True);
    DelTree(ExpandConstant('{app}\Data'), True, True, True);
    DelTree(ExpandConstant('{app}\Backups'), True, True, True);
    DelTree(ExpandConstant('{app}\Logs'), True, True, True);
  end;
end;
```

The default `No` response leaves field data in place. The second confirmation is required before any field-data deletion. The normal uninstall list still removes App and the shortcut only.

- [ ] **Step 4: Add changed-root upgrade warning without migration**

Add an Inno Setup `NextButtonClick` guard for the directory selection page. It must compare the selected `{app}` with the previous install directory and warn when they differ:

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
    Result := MsgBox(
      '已选择新的安装目录。旧 Data、Projects、Backups 和 Logs 不会自动迁移或覆盖。是否继续？',
      mbConfirmation,
      MB_YESNO) = IDYES;
  end;
end;
```

The exact registry value name used by the installer must be kept stable with its AppId registration. This hook only warns and permits/blocks the install; it must not copy field data. `UsePreviousAppDir=yes` remains the default same-root upgrade path.

- [ ] **Step 5: Compile and test the installer contract**

```powershell
iscc.exe installer\MineRailMonitor.iss
dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DesktopInstallerMarkupTests"
```

Expected: Inno Setup compilation succeeds and all ACL/upgrade/uninstall tests pass.

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
    (Join-Path $root "Projects"),
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

1. First install with default `C:\MineRailMonitor`.
2. Confirm the desktop shortcut is `矿车编组监控系统` and targets `<Root>\App\MineRailMonitor.exe`.
3. Start as a normal user; confirm no elevation prompt and no service/autostart/scheduled task/Registry Run entry.
4. Confirm Data can create/open SQLite, Logs and BlackBox can write, Backups can be generated, and Projects can load.
5. Confirm ordinary users cannot modify an App exe/dll.
6. Install a disposable second copy into `D:\MineRailMonitor` and confirm no `D:\MineRailMonitor\MineRailMonitor` is created.
7. Upgrade the first installation without changing the directory; confirm the previous Root is reused and Data, Projects, Logs and Backups remain unchanged.
8. Re-run upgrade with a deliberately changed Root; confirm the warning appears and no old field data is copied or overwritten.
9. Uninstall with the default field-data choice; confirm App and shortcut are removed while Projects, Data, Backups and Logs remain.
10. Repeat uninstall with both explicit confirmations; confirm only the selected disposable field-data directory is removed.
11. Reinstall using the retained data directory and confirm the existing database and project configuration are usable.

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

The implementation worker should keep each Task commit independently reviewable. The plan itself is committed now with:

```powershell
git add docs/superpowers/plans/2026-09-21-desktop-install-layout.md
git commit -m "docs: plan desktop installation layout"
git push origin feat/desktop-install-layout
```

Do not execute Task 1 from the plan in the plan-writing phase. Do not create a new implementation branch. Do not merge main.

## Plan Self-Review

- Spec sections 1–4 map to Global Constraints, Repo Mapping, Task 1 and Task 2.
- Spec section 5 maps to Task 3 publish handling and Task 5 upgrade protection.
- Spec sections 6–8 map to Task 2 runtime paths and Task 6 verification; SQLite implementation remains unchanged.
- Spec sections 9–11 map to Task 4 installer layout and Task 5 upgrade/uninstall behavior.
- Spec section 12 maps to Task 1 root resolution and Task 6 recovery-marker migration boundary.
- Spec section 13 maps to Task 5 ACL checks.
- Spec section 14 maps to Task 4 Inno Setup selection.
- Spec section 15 maps to the six Tasks in this plan.
- Spec section 16 maps to Task 6 automated and manual acceptance.
- Spec section 17 maps to the scope constraints in this plan.
- Placeholder scan must find no placeholder markers, vague implementation steps, or unassigned Task reference.
- Type consistency: `ApplicationPaths` is created in Task 1, exposed as `App.Paths` in Task 2, consumed by staging/runtime wiring in Task 3, and never referenced by installer Pascal code.
- Review Focus items are assigned to Task 1, Task 2, Task 3, Task 4 and Task 5 as listed above.
```
