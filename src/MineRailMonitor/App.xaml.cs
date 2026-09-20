using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MineRailMonitor.Core.Acceptance;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Infrastructure.Logging;
using MineRailMonitor.Infrastructure.Persistence;
using MineRailMonitor.Pages;

namespace MineRailMonitor;

public partial class App : Application
{
    public App()
    {
        AcceptanceOptions = AcceptanceCommandLineOptions.Parse(Environment.GetCommandLineArgs());
        var logDirectory = AcceptanceOptions.Enabled
            ? AcceptanceOptions.LogDirectory!
            : System.IO.Path.Combine(AppContext.BaseDirectory, "Logs");
        Logger = new FileLogger(logDirectory);
        AdminModeService = new AdminModeService(ReadAdminPassword());
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public ILogger Logger { get; }

    public AcceptanceCommandLineOptions AcceptanceOptions { get; }

    public AdminModeService AdminModeService { get; }

    private SqlitePassageRecordStore? _passageRecordStore;
    private DatabaseMaintenanceCoordinator? _databaseMaintenanceCoordinator;
    private readonly object _databaseShutdownSyncRoot = new();
    private Task? _databaseShutdownTask;

    private static string? ReadAdminPassword()
    {
        var environmentPassword = Environment.GetEnvironmentVariable("MINE_RAIL_ADMIN_PASSWORD");
        if (!string.IsNullOrWhiteSpace(environmentPassword))
        {
            return environmentPassword;
        }

        return ConfigurationManager.AppSettings["AdminPassword"];
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        Logger.Information("MineRailMonitor 启动。");
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        try
        {
            if (AcceptanceOptions.Enabled)
            {
                StartAcceptance();
                return;
            }

            await StartProductionAsync();
        }
        catch (OperationCanceledException)
        {
            Logger.Information("MineRailMonitor 启动已取消。");
            Shutdown();
        }
        catch (Exception exception)
        {
            Logger.Error("MineRailMonitor 启动失败。", exception);
            ShowStartupError("程序启动失败", $"程序无法启动：{exception.Message}");
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Information("MineRailMonitor 退出。");
        try
        {
            StopDatabaseInfrastructureAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Logger.Error("SQLite 数据库基础设施关闭失败。", exception);
        }

        base.OnExit(e);
    }

    public Task StopDatabaseInfrastructureAsync()
    {
        lock (_databaseShutdownSyncRoot)
        {
            if (_databaseShutdownTask is null ||
                _databaseShutdownTask.IsFaulted ||
                _databaseShutdownTask.IsCanceled)
            {
                _databaseShutdownTask = StopDatabaseInfrastructureCoreAsync();
            }

            return _databaseShutdownTask;
        }
    }

    private async Task StopDatabaseInfrastructureCoreAsync()
    {
        var coordinator = _databaseMaintenanceCoordinator;
        if (coordinator is not null)
        {
            await coordinator.StopAsync().ConfigureAwait(false);
            coordinator.Dispose();
            _databaseMaintenanceCoordinator = null;
        }

        var store = _passageRecordStore;
        if (store is not null)
        {
            store.Dispose();
            _passageRecordStore = null;
        }
    }

    private void StartAcceptance()
    {
        _passageRecordStore = new SqlitePassageRecordStore(AcceptanceOptions.DatabasePath!);
        var mainWindow = new MainWindow(_passageRecordStore!);
        MainWindow = mainWindow;
        ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    private async Task StartProductionAsync()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        var productionDatabasePath = Path.Combine(dataDirectory, "MineRailMonitor.db");
        var backupRootDirectory = Path.Combine(AppContext.BaseDirectory, "Backups", "SQLite");
        var timeProvider = new SystemRfidTimeProvider();
        var healthChecker = new SqliteDatabaseHealthChecker(timeProvider, Logger);
        var backupService = new SqliteBackupService(healthChecker, Logger);
        var recoveryService = new SqliteRecoveryService(
            healthChecker,
            dataDirectory,
            Logger,
            timeProvider);
        var startupGate = new DatabaseStartupGate(
            productionDatabasePath,
            backupRootDirectory,
            healthChecker,
            backupService,
            recoveryService,
            acceptanceMode: false,
            Logger);
        Func<DatabaseMaintenanceCoordinator> createMaintenanceCoordinator = () =>
            new DatabaseMaintenanceCoordinator(
                productionDatabasePath,
                backupRootDirectory,
                backupService,
                healthChecker,
                new SqliteRetentionService(14, Logger),
                timeProvider,
                new TaskAsyncDelay(),
                Logger);

        while (true)
        {
            var decision = startupGate.Inspect();
            switch (decision.Kind)
            {
                case DatabaseStartupDecisionKind.CreateNew:
                    if (await CreateNewAsync(productionDatabasePath, createMaintenanceCoordinator))
                    {
                        return;
                    }

                    return;
                case DatabaseStartupDecisionKind.StartHealthy:
                    if (await StartHealthyAsync(productionDatabasePath, createMaintenanceCoordinator))
                    {
                        return;
                    }

                    return;
                case DatabaseStartupDecisionKind.RecoverCorrupt:
                case DatabaseStartupDecisionKind.Unavailable:
                case DatabaseStartupDecisionKind.UnsupportedSchema:
                case DatabaseStartupDecisionKind.InterruptedRecovery:
                case DatabaseStartupDecisionKind.RecoveryStateError:
                    var dialogResult = await ShowRecoveryDecisionAsync(decision);
                    if (dialogResult.Action == DatabaseRecoveryDialogAction.Exit)
                    {
                        Shutdown();
                        return;
                    }

                    if (dialogResult.Action == DatabaseRecoveryDialogAction.OpenDataDirectory)
                    {
                        OpenDataDirectory(dataDirectory);
                        continue;
                    }

                    if (dialogResult.Action == DatabaseRecoveryDialogAction.Retry)
                    {
                        continue;
                    }

                    await HandleRecoveryDecisionAsync(
                        decision,
                        dialogResult,
                        productionDatabasePath,
                        recoveryService);
                    continue;
            }
        }
    }

    private async Task<bool> StartHealthyAsync(
        string productionDatabasePath,
        Func<DatabaseMaintenanceCoordinator> createMaintenanceCoordinator)
    {
        _databaseMaintenanceCoordinator = createMaintenanceCoordinator();
        if (!await TryRunStartupCatchUpAsync(_databaseMaintenanceCoordinator))
        {
            return false;
        }

        _passageRecordStore = new SqlitePassageRecordStore(productionDatabasePath);
        return await StartMainWindowAsync();
    }

    private async Task<bool> CreateNewAsync(
        string productionDatabasePath,
        Func<DatabaseMaintenanceCoordinator> createMaintenanceCoordinator)
    {
        _databaseMaintenanceCoordinator = createMaintenanceCoordinator();
        _passageRecordStore = new SqlitePassageRecordStore(productionDatabasePath);
        if (!await TryRunStartupCatchUpAsync(_databaseMaintenanceCoordinator))
        {
            return false;
        }

        return await StartMainWindowAsync();
    }

    private async Task<bool> TryRunStartupCatchUpAsync(
        DatabaseMaintenanceCoordinator databaseMaintenanceCoordinator)
    {
        try
        {
            var backupResult = await databaseMaintenanceCoordinator.RunStartupCatchUpAsync(CancellationToken.None);
            if (!backupResult.Succeeded)
            {
                Logger.Warning($"SQLite 启动备份未完成，将继续启动业务：{backupResult.ErrorMessage ?? "未知原因"}");
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Information("SQLite 启动备份已取消，程序将退出。");
            Shutdown();
            return false;
        }
        catch (Exception exception)
        {
            Logger.Warning("SQLite 启动备份失败，将继续启动业务。");
            Logger.Error("SQLite 启动备份异常。", exception);
        }

        return true;
    }

    private async Task<bool> StartMainWindowAsync()
    {
        var mainWindow = new MainWindow(_passageRecordStore!);
        MainWindow = mainWindow;
        await _databaseMaintenanceCoordinator!.StartAsync(CancellationToken.None);
        ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
        return true;
    }

    private Task<DatabaseRecoveryDialogResult> ShowRecoveryDecisionAsync(DatabaseStartupDecision decision)
    {
        var dialog = new DatabaseRecoveryDialog(decision)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true
        };
        dialog.ShowDialog();
        return Task.FromResult(dialog.Result);
    }

    private Task<bool> HandleRecoveryDecisionAsync(
        DatabaseStartupDecision decision,
        DatabaseRecoveryDialogResult dialogResult,
        string productionDatabasePath,
        SqliteRecoveryService recoveryService)
    {
        if (dialogResult.Action == DatabaseRecoveryDialogAction.Recover)
        {
            if (decision.Kind != DatabaseStartupDecisionKind.RecoverCorrupt ||
                dialogResult.Candidate is null)
            {
                Logger.Warning("启动恢复决策缺少有效的已验证备份候选。");
                return Task.FromResult(false);
            }

            if (!EnsureRecoveryAdmin())
            {
                return Task.FromResult(false);
            }

            var recoveryResult = recoveryService.Recover(productionDatabasePath, dialogResult.Candidate);
            if (recoveryResult.Succeeded)
            {
                return Task.FromResult(true);
            }

            if (!recoveryResult.Succeeded)
            {
                Logger.Error($"SQLite 数据库恢复失败：{recoveryResult.ErrorMessage ?? "未知原因"}");
            }

            return Task.FromResult(false);
        }

        if (dialogResult.Action == DatabaseRecoveryDialogAction.ResumeRecovery)
        {
            if (decision.Kind != DatabaseStartupDecisionKind.InterruptedRecovery ||
                decision.RecoveryMarker is null)
            {
                Logger.Warning("启动恢复决策缺少有效的恢复 marker。");
                return Task.FromResult(false);
            }

            if (!EnsureRecoveryAdmin())
            {
                return Task.FromResult(false);
            }

            var recoveryResult = recoveryService.ResumeInterruptedRecovery(
                productionDatabasePath,
                decision.RecoveryMarker);
            if (recoveryResult.Succeeded)
            {
                return Task.FromResult(true);
            }

            if (!recoveryResult.Succeeded)
            {
                Logger.Error($"SQLite 数据库恢复续作失败：{recoveryResult.ErrorMessage ?? "未知原因"}");
            }

            return Task.FromResult(false);
        }

        return Task.FromResult(false);
    }

    private bool EnsureRecoveryAdmin()
    {
        if (AdminModeService.IsAdmin)
        {
            return true;
        }

        var dialog = new AdminPasswordDialog(AdminModeService)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true
        };
        return dialog.ShowDialog() == true && AdminModeService.IsAdmin;
    }

    private void OpenDataDirectory(string dataDirectory)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{dataDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Logger.Error("打开 SQLite 数据目录失败。", exception);
            ShowStartupError("无法打开数据目录", exception.Message);
        }
    }

    private void ShowStartupError(string title, string message)
    {
        try
        {
            var dialog = new StyledMessageDialog(title, message, MessageDialogKind.Error)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = true
            };
            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            Logger.Error("显示启动错误提示失败。", exception);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("UI 未处理异常。", e.Exception);
        var dialog = new StyledMessageDialog(
            "程序错误",
            $"程序遇到未处理错误：{e.Exception.Message}",
            MessageDialogKind.Error);
        if (MainWindow is not null)
        {
            dialog.Owner = MainWindow;
        }
        dialog.ShowDialog();
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Logger.Error("程序未处理异常。", exception);
        }
    }
}
