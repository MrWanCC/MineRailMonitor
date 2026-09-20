namespace MineRailMonitor.Core.Tests;

public sealed class DatabaseStartupOwnershipMarkupTests
{
    [Fact]
    public void App_runs_startup_gate_before_new_main_window()
    {
        var app = ReadApp();

        var gateIndex = app.IndexOf("new DatabaseStartupGate", StringComparison.Ordinal);
        var inspectIndex = app.IndexOf("startupGate.Inspect", StringComparison.Ordinal);
        var windowIndex = app.LastIndexOf("new MainWindow(_passageRecordStore!", StringComparison.Ordinal);

        Assert.True(gateIndex >= 0);
        Assert.True(inspectIndex > gateIndex);
        Assert.True(windowIndex > inspectIndex);
    }

    [Fact]
    public void MainWindow_receives_sqlite_store()
    {
        var mainWindow = ReadMainWindow();

        Assert.Contains(
            "public MainWindow(SqlitePassageRecordStore passageRecordStore)",
            mainWindow);
        Assert.Contains(
            "_passageRecordStore = passageRecordStore ?? throw new ArgumentNullException(nameof(passageRecordStore));",
            mainWindow);
    }

    [Fact]
    public void MainWindow_does_not_construct_sqlite_store()
    {
        Assert.DoesNotContain("new SqlitePassageRecordStore", ReadMainWindow());
    }

    [Fact]
    public void MainWindow_does_not_dispose_app_owned_store()
    {
        Assert.DoesNotContain("_passageRecordStore.Dispose()", ReadMainWindow());
    }

    [Fact]
    public void App_owns_store_and_maintenance_coordinator()
    {
        var app = ReadApp();

        Assert.Contains("private SqlitePassageRecordStore? _passageRecordStore;", app);
        Assert.Contains("private DatabaseMaintenanceCoordinator? _databaseMaintenanceCoordinator;", app);
        Assert.Contains("_passageRecordStore =", app);
        Assert.Contains("_databaseMaintenanceCoordinator =", app);
    }

    [Fact]
    public void Acceptance_startup_skips_production_backup_and_recovery()
    {
        var acceptance = ExtractMethod(ReadApp(), "private void StartAcceptance");

        Assert.Contains("new SqlitePassageRecordStore(AcceptanceOptions.DatabasePath!)", acceptance);
        Assert.Contains("new MainWindow(_passageRecordStore", acceptance);
        Assert.DoesNotContain("DatabaseStartupGate", acceptance);
        Assert.DoesNotContain("DatabaseRecoveryDialog", acceptance);
        Assert.DoesNotContain("DatabaseMaintenanceCoordinator", acceptance);
        Assert.DoesNotContain("Backups", acceptance);
        Assert.DoesNotContain("Corrupt", acceptance);
    }

    [Fact]
    public void Existing_healthy_backup_attempt_precedes_store_construction()
    {
        var startHealthy = ExtractMethod(ReadApp(), "private async Task<bool> StartHealthyAsync");

        Assert.True(
            startHealthy.IndexOf("RunStartupCatchUpAsync", StringComparison.Ordinal) <
            startHealthy.IndexOf("new SqlitePassageRecordStore(productionDatabasePath)", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_store_initialization_precedes_first_backup()
    {
        var createNew = ExtractMethod(ReadApp(), "private async Task<bool> CreateNewAsync");

        Assert.True(
            createNew.IndexOf("new SqlitePassageRecordStore(productionDatabasePath)", StringComparison.Ordinal) <
            createNew.IndexOf("RunStartupCatchUpAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void App_routes_recover_before_store_creation()
    {
        var production = ReadApp();

        Assert.Contains("DatabaseStartupDecisionKind.RecoverCorrupt", production);
        Assert.Contains("await HandleRecoveryDecisionAsync", ExtractMethod(production, "private async Task StartProductionAsync"));
        Assert.Contains("recoveryService.Recover", ExtractMethod(production, "private Task<bool> HandleRecoveryDecisionAsync"));
    }

    [Fact]
    public void App_routes_resume_recovery_before_store_creation()
    {
        var production = ReadApp();

        Assert.Contains("DatabaseStartupDecisionKind.InterruptedRecovery", production);
        Assert.Contains("await HandleRecoveryDecisionAsync", ExtractMethod(production, "private async Task StartProductionAsync"));
        Assert.Contains("ResumeInterruptedRecovery", ExtractMethod(production, "private Task<bool> HandleRecoveryDecisionAsync"));
    }

    [Fact]
    public void Recovery_success_reenters_startup_gate()
    {
        var production = ReadApp();

        Assert.Contains("if (recoveryResult.Succeeded)", ExtractMethod(production, "private Task<bool> HandleRecoveryDecisionAsync"));
        Assert.Contains("continue;", production);
    }

    [Fact]
    public void Unsupported_schema_never_constructs_store()
    {
        var production = ReadApp();

        Assert.Contains("DatabaseStartupDecisionKind.UnsupportedSchema", production);
        Assert.Contains("ShowRecoveryDecisionAsync", production);
    }

    [Fact]
    public void Unavailable_never_constructs_store()
    {
        var production = ReadApp();

        Assert.Contains("DatabaseStartupDecisionKind.Unavailable", production);
        Assert.Contains("ShowRecoveryDecisionAsync", production);
    }

    [Fact]
    public void Recovery_state_error_never_constructs_store()
    {
        var production = ReadApp();

        Assert.Contains("DatabaseStartupDecisionKind.RecoveryStateError", production);
        Assert.Contains("ShowRecoveryDecisionAsync", production);
    }

    [Fact]
    public void Startup_dialog_uses_explicit_shutdown_until_main_window_is_created()
    {
        var app = ReadApp();

        var explicitIndex = app.IndexOf("ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown", StringComparison.Ordinal);
        var dialogIndex = app.IndexOf("dialog.ShowDialog()", StringComparison.Ordinal);
        var normalIndex = app.LastIndexOf("ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose", StringComparison.Ordinal);

        Assert.True(explicitIndex >= 0);
        Assert.True(dialogIndex > explicitIndex);
        Assert.True(normalIndex > explicitIndex);
    }

    [Fact]
    public void Main_window_assignment_precedes_on_main_window_close_mode()
    {
        var app = ReadApp();

        var assignmentIndex = app.LastIndexOf("MainWindow = mainWindow", StringComparison.Ordinal);
        var modeIndex = app.LastIndexOf("ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose", StringComparison.Ordinal);
        var showIndex = app.LastIndexOf("mainWindow.Show()", StringComparison.Ordinal);

        Assert.True(assignmentIndex >= 0);
        Assert.True(modeIndex > assignmentIndex);
        Assert.True(showIndex > modeIndex);
    }

    [Fact]
    public void Recover_requires_admin_verification_before_recovery_service()
    {
        var method = ExtractMethod(ReadApp(), "private Task<bool> HandleRecoveryDecisionAsync");

        Assert.True(
            method.IndexOf("EnsureRecoveryAdmin", StringComparison.Ordinal) <
            method.IndexOf("recoveryService.Recover", StringComparison.Ordinal));
    }

    [Fact]
    public void Resume_recovery_requires_admin_verification_before_recovery_service()
    {
        var method = ExtractMethod(ReadApp(), "private Task<bool> HandleRecoveryDecisionAsync");

        Assert.True(
            method.IndexOf("EnsureRecoveryAdmin", StringComparison.Ordinal) <
            method.IndexOf("ResumeInterruptedRecovery", StringComparison.Ordinal));
    }

    [Fact]
    public void Admin_cancel_never_invokes_recovery_service()
    {
        var method = ExtractMethod(ReadApp(), "private Task<bool> HandleRecoveryDecisionAsync");

        Assert.Contains("if (!EnsureRecoveryAdmin())", method);
        Assert.Contains("return Task.FromResult(false)", method);
    }

    [Fact]
    public void Already_admin_does_not_require_second_password_dialog()
    {
        var app = ReadApp();
        var method = ExtractMethod(app, "private bool EnsureRecoveryAdmin");

        Assert.Contains("if (AdminModeService.IsAdmin)", method);
        Assert.Contains("return true", method);
        Assert.Contains("new AdminPasswordDialog(AdminModeService)", method);
    }

    [Fact]
    public void App_creates_production_services_from_fixed_paths()
    {
        var production = ExtractMethod(ReadApp(), "private async Task StartProductionAsync");

        Assert.Contains("Path.Combine(AppContext.BaseDirectory, \"Data\")", production);
        Assert.Contains("Path.Combine(dataDirectory, \"MineRailMonitor.db\")", production);
        Assert.Contains("Path.Combine(AppContext.BaseDirectory, \"Backups\", \"SQLite\")", production);
        Assert.Contains("new SqliteDatabaseHealthChecker", production);
        Assert.Contains("new SqliteBackupService", production);
        Assert.Contains("new SqliteRecoveryService", production);
        Assert.Contains("new DatabaseStartupGate", production);
        Assert.Contains("new SqliteRetentionService(14", production);
        Assert.Contains("new TaskAsyncDelay()", production);
    }

    [Fact]
    public void Startup_catch_up_failure_is_best_effort_but_store_failure_blocks_startup()
    {
        var app = ReadApp();

        Assert.Contains("RunStartupCatchUpAsync", app);
        Assert.Contains("Logger.Warning", app);
        Assert.Contains("new SqlitePassageRecordStore", app);
        Assert.Contains("ShowStartupError", app);
        Assert.Contains("Shutdown()", app);
    }

    [Fact]
    public void App_starts_coordinator_only_after_store_and_main_window_exist()
    {
        var app = ReadApp();
        var startIndex = app.IndexOf("_databaseMaintenanceCoordinator!.StartAsync", StringComparison.Ordinal);
        var storeIndex = app.IndexOf("_passageRecordStore = new SqlitePassageRecordStore(productionDatabasePath)", StringComparison.Ordinal);
        var windowIndex = app.LastIndexOf("var mainWindow = new MainWindow(_passageRecordStore!", StringComparison.Ordinal);

        Assert.True(storeIndex >= 0);
        Assert.True(windowIndex > storeIndex);
        Assert.True(startIndex > windowIndex);
    }

    [Fact]
    public void App_shutdown_disposes_coordinator_before_store()
    {
        var onExit = ExtractMethod(ReadApp(), "protected override void OnExit");

        Assert.True(
            onExit.IndexOf("_databaseMaintenanceCoordinator?.Dispose()", StringComparison.Ordinal) <
            onExit.IndexOf("_passageRecordStore?.Dispose()", StringComparison.Ordinal));
    }

    private static string ReadApp() => ReadSource("src", "MineRailMonitor", "App.xaml.cs");

    private static string ReadMainWindow() => ReadSource("src", "MineRailMonitor", "MainWindow.xaml.cs");

    private static string ReadSource(params string[] segments) =>
        NormalizeNewlines(File.ReadAllText(Locate(segments)));

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method signature: {signature}");
        var nextMethod = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return nextMethod >= 0
            ? source.Substring(start, nextMethod - start)
            : source.Substring(start);
    }

    private static string Locate(params string[] segments)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException($"Could not locate source file: {string.Join("/", segments)}");
    }

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n");
}
