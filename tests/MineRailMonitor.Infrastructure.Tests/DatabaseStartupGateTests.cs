using System.Text;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Infrastructure.Logging;
using MineRailMonitor.Infrastructure.Persistence;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class DatabaseStartupGateTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 9, 20, 10, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Missing_database_without_marker_returns_create_new()
    {
        using var workspace = StartupWorkspace.Create();
        var health = new RecordingHealthChecker((_, _) => SqliteDatabaseHealthState.Missing);
        var backup = new RecordingBackupService(Array.Empty<SqliteBackupCandidate>());
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.CreateNew, decision.Kind);
        Assert.Equal(SqliteDatabaseHealthState.Missing, decision.Health!.State);
        Assert.Empty(decision.Candidates);
        Assert.Null(decision.RecoveryMarker);
        Assert.Equal(0, backup.ScanCalls);
    }

    [Fact]
    public void Healthy_database_returns_start_healthy()
    {
        using var workspace = StartupWorkspace.Create();
        var health = new RecordingHealthChecker((_, _) => SqliteDatabaseHealthState.Healthy);
        var backup = new RecordingBackupService(Array.Empty<SqliteBackupCandidate>());
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.StartHealthy, decision.Kind);
        Assert.Equal(SqliteDatabaseHealthState.Healthy, decision.Health!.State);
        Assert.Equal(0, backup.ScanCalls);
    }

    [Fact]
    public void Healthy_database_does_not_scan_recovery_candidates()
    {
        using var workspace = StartupWorkspace.Create();
        var health = new RecordingHealthChecker((_, _) => SqliteDatabaseHealthState.Healthy);
        var backup = new RecordingBackupService(new[] { workspace.CreateCandidate("healthy") });
        var gate = CreateGate(workspace, health, backup);

        gate.Inspect();

        Assert.Equal(0, backup.ScanCalls);
        Assert.Single(health.Calls);
        Assert.Equal(SqliteInspectionMode.StartupFast, health.Calls[0].Mode);
    }

    [Fact]
    public void Unavailable_database_returns_unavailable_without_recovery_candidates()
    {
        using var workspace = StartupWorkspace.Create();
        var health = new RecordingHealthChecker((_, _) => SqliteDatabaseHealthState.Unavailable);
        var backup = new RecordingBackupService(new[] { workspace.CreateCandidate("backup") });
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.Unavailable, decision.Kind);
        Assert.Equal(SqliteDatabaseHealthState.Unavailable, decision.Health!.State);
        Assert.Empty(decision.Candidates);
        Assert.Equal(0, backup.ScanCalls);
    }

    [Fact]
    public void Newer_schema_returns_unsupported_schema_without_recovery_candidates()
    {
        using var workspace = StartupWorkspace.Create();
        var health = new RecordingHealthChecker((_, _) => SqliteDatabaseHealthState.UnsupportedSchema);
        var backup = new RecordingBackupService(new[] { workspace.CreateCandidate("backup") });
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.UnsupportedSchema, decision.Kind);
        Assert.Equal(SqliteDatabaseHealthState.UnsupportedSchema, decision.Health!.State);
        Assert.Empty(decision.Candidates);
        Assert.Equal(0, backup.ScanCalls);
    }

    [Fact]
    public void Corrupt_database_returns_recover_corrupt_with_individually_scanned_candidates()
    {
        using var workspace = StartupWorkspace.Create();
        var newest = workspace.CreateCandidate("newest");
        var older = workspace.CreateCandidate("older");
        var unavailable = workspace.CreateCandidate("unavailable");
        var health = new RecordingHealthChecker((path, mode) =>
            mode == SqliteInspectionMode.StartupFast
                ? SqliteDatabaseHealthState.Corrupt
                : path == newest.Path || path == older.Path
                    ? SqliteDatabaseHealthState.Healthy
                    : SqliteDatabaseHealthState.Unavailable);
        var backup = new RecordingBackupService(new[] { newest, older, unavailable });
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.RecoverCorrupt, decision.Kind);
        Assert.Equal(SqliteDatabaseHealthState.Corrupt, decision.Health!.State);
        Assert.Equal(new[] { newest.Path, older.Path }, decision.Candidates.Select(candidate => candidate.Path));
        Assert.Equal(1, backup.ScanCalls);
        Assert.Equal(4, health.Calls.Count);
        Assert.All(health.Calls.Skip(1), call => Assert.Equal(SqliteInspectionMode.FullValidation, call.Mode));
    }

    [Fact]
    public void Newest_corrupt_backup_falls_back_to_next_healthy_candidate()
    {
        using var workspace = StartupWorkspace.Create();
        var newest = workspace.CreateCandidate("newest");
        var older = workspace.CreateCandidate("older");
        var health = new RecordingHealthChecker((path, mode) =>
            mode == SqliteInspectionMode.StartupFast
                ? SqliteDatabaseHealthState.Corrupt
                : path == newest.Path
                    ? SqliteDatabaseHealthState.Corrupt
                    : SqliteDatabaseHealthState.Healthy);
        var backup = new RecordingBackupService(new[] { newest, older });
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.RecoverCorrupt, decision.Kind);
        var candidate = Assert.Single(decision.Candidates);
        Assert.Equal(older.Path, candidate.Path);
        Assert.Equal(3, health.Calls.Count);
    }

    [Fact]
    public void Unavailable_candidate_is_not_presented_for_restore()
    {
        using var workspace = StartupWorkspace.Create();
        var candidate = workspace.CreateCandidate("unavailable");
        var health = CreateCorruptStartupHealth(candidate.Path, SqliteDatabaseHealthState.Unavailable);
        var backup = new RecordingBackupService(new[] { candidate });
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.RecoverCorrupt, decision.Kind);
        Assert.Empty(decision.Candidates);
    }

    [Fact]
    public void Unsupported_schema_candidate_is_not_presented_for_restore()
    {
        using var workspace = StartupWorkspace.Create();
        var candidate = workspace.CreateCandidate("unsupported");
        var health = CreateCorruptStartupHealth(candidate.Path, SqliteDatabaseHealthState.UnsupportedSchema);
        var backup = new RecordingBackupService(new[] { candidate });
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.RecoverCorrupt, decision.Kind);
        Assert.Empty(decision.Candidates);
    }

    [Fact]
    public void Missing_candidate_is_not_presented_for_restore()
    {
        using var workspace = StartupWorkspace.Create();
        var candidate = workspace.CreateCandidate("missing");
        var health = CreateCorruptStartupHealth(candidate.Path, SqliteDatabaseHealthState.Missing);
        var backup = new RecordingBackupService(new[] { candidate });
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.RecoverCorrupt, decision.Kind);
        Assert.Empty(decision.Candidates);
    }

    [Fact]
    public void Candidate_validation_uses_full_validation()
    {
        using var workspace = StartupWorkspace.Create();
        var candidate = workspace.CreateCandidate("candidate");
        var health = CreateCorruptStartupHealth(candidate.Path, SqliteDatabaseHealthState.Healthy);
        var backup = new RecordingBackupService(new[] { candidate });
        var gate = CreateGate(workspace, health, backup);

        gate.Inspect();

        Assert.Equal(
            new[] { SqliteInspectionMode.StartupFast, SqliteInspectionMode.FullValidation },
            health.Calls.Select(call => call.Mode));
    }

    [Fact]
    public void Healthy_candidates_preserve_newest_to_oldest_order()
    {
        using var workspace = StartupWorkspace.Create();
        var newest = workspace.CreateCandidate("newest");
        var middle = workspace.CreateCandidate("middle");
        var oldest = workspace.CreateCandidate("oldest");
        var health = CreateCorruptStartupHealth(null, SqliteDatabaseHealthState.Healthy);
        var backup = new RecordingBackupService(new[] { newest, middle, oldest });
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(
            new[] { newest.Path, middle.Path, oldest.Path },
            decision.Candidates.Select(candidate => candidate.Path));
    }

    [Fact]
    public void Marker_and_missing_database_returns_interrupted_recovery_not_create_new()
    {
        using var workspace = StartupWorkspace.Create();
        var marker = workspace.WriteMarker();
        File.Delete(workspace.ProductionPath);
        var health = new RecordingHealthChecker((_, _) => SqliteDatabaseHealthState.Missing);
        var backup = new RecordingBackupService(Array.Empty<SqliteBackupCandidate>());
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.InterruptedRecovery, decision.Kind);
        AssertMarkerEqual(marker, decision.RecoveryMarker);
        Assert.Null(decision.Health);
        Assert.Empty(decision.Candidates);
        Assert.Empty(health.Calls);
        Assert.Equal(0, backup.ScanCalls);
    }

    [Fact]
    public void Marker_and_existing_database_still_blocks_normal_start()
    {
        using var workspace = StartupWorkspace.Create();
        var marker = workspace.WriteMarker();
        var health = new RecordingHealthChecker((_, _) => SqliteDatabaseHealthState.Healthy);
        var backup = new RecordingBackupService(Array.Empty<SqliteBackupCandidate>());
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.InterruptedRecovery, decision.Kind);
        AssertMarkerEqual(marker, decision.RecoveryMarker);
        Assert.Empty(health.Calls);
        Assert.Equal(0, backup.ScanCalls);
    }

    [Fact]
    public void Interrupted_recovery_decision_exposes_marker_for_resume()
    {
        using var workspace = StartupWorkspace.Create();
        var marker = workspace.WriteMarker();
        var gate = CreateGate(
            workspace,
            new RecordingHealthChecker((_, _) => SqliteDatabaseHealthState.Corrupt),
            new RecordingBackupService(Array.Empty<SqliteBackupCandidate>()));

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.InterruptedRecovery, decision.Kind);
        Assert.Equal(marker.SourceBackupPath, decision.RecoveryMarker!.SourceBackupPath);
    }

    [Fact]
    public void Malformed_recovery_marker_blocks_normal_start()
    {
        using var workspace = StartupWorkspace.Create();
        File.WriteAllText(workspace.MarkerPath, "{not-json");
        var health = new RecordingHealthChecker((_, _) => SqliteDatabaseHealthState.Missing);
        var backup = new RecordingBackupService(Array.Empty<SqliteBackupCandidate>());
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.RecoveryStateError, decision.Kind);
        Assert.Null(decision.Health);
        Assert.Null(decision.RecoveryMarker);
        Assert.Empty(decision.Candidates);
        Assert.False(string.IsNullOrWhiteSpace(decision.ErrorMessage));
        Assert.Empty(health.Calls);
        Assert.Equal(0, backup.ScanCalls);
    }

    [Fact]
    public void Malformed_marker_and_missing_database_never_returns_create_new()
    {
        using var workspace = StartupWorkspace.Create();
        File.Delete(workspace.ProductionPath);
        File.WriteAllText(workspace.MarkerPath, "{not-json");
        var gate = CreateGate(
            workspace,
            new RecordingHealthChecker((_, _) => SqliteDatabaseHealthState.Missing),
            new RecordingBackupService(Array.Empty<SqliteBackupCandidate>()));

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.RecoveryStateError, decision.Kind);
        Assert.NotEqual(DatabaseStartupDecisionKind.CreateNew, decision.Kind);
    }

    [Fact]
    public void Unreadable_recovery_marker_never_returns_create_new()
    {
        using var workspace = StartupWorkspace.Create();
        Directory.CreateDirectory(workspace.MarkerPath);
        var health = new RecordingHealthChecker((_, _) => SqliteDatabaseHealthState.Missing);
        var backup = new RecordingBackupService(Array.Empty<SqliteBackupCandidate>());
        var gate = CreateGate(workspace, health, backup);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.RecoveryStateError, decision.Kind);
        Assert.Null(decision.RecoveryMarker);
        Assert.False(string.IsNullOrWhiteSpace(decision.ErrorMessage));
        Assert.Empty(health.Calls);
        Assert.Equal(0, backup.ScanCalls);
    }

    [Fact]
    public void Acceptance_mode_bypasses_production_backup_recovery_and_maintenance_paths()
    {
        using var workspace = StartupWorkspace.Create();
        Directory.CreateDirectory(workspace.MarkerPath);
        var health = new RecordingHealthChecker((_, mode) =>
            mode == SqliteInspectionMode.StartupFast
                ? SqliteDatabaseHealthState.Corrupt
                : SqliteDatabaseHealthState.Healthy);
        var backup = new RecordingBackupService(new[] { workspace.CreateCandidate("backup") });
        var gate = CreateGate(workspace, health, backup, acceptanceMode: true);

        var decision = gate.Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.RecoverCorrupt, decision.Kind);
        Assert.Equal(SqliteDatabaseHealthState.Corrupt, decision.Health!.State);
        Assert.Empty(decision.Candidates);
        Assert.Empty(health.Calls.Skip(1));
        Assert.Single(health.Calls);
        Assert.Equal(SqliteInspectionMode.StartupFast, health.Calls[0].Mode);
        Assert.Equal(0, backup.ScanCalls);
    }

    [Fact]
    public void Inspect_does_not_modify_database_or_candidate_files()
    {
        using var workspace = StartupWorkspace.Create();
        var candidate = workspace.CreateCandidate("candidate");
        var beforeProduction = File.ReadAllBytes(workspace.ProductionPath);
        var beforeCandidate = File.ReadAllBytes(candidate.Path);
        var health = new RecordingHealthChecker((_, mode) =>
            mode == SqliteInspectionMode.StartupFast
                ? SqliteDatabaseHealthState.Corrupt
                : SqliteDatabaseHealthState.Healthy);
        var backup = new RecordingBackupService(new[] { candidate });
        var gate = CreateGate(workspace, health, backup);

        gate.Inspect();

        Assert.Equal(beforeProduction, File.ReadAllBytes(workspace.ProductionPath));
        Assert.Equal(beforeCandidate, File.ReadAllBytes(candidate.Path));
        Assert.Empty(Directory.GetDirectories(workspace.CorruptRoot));
        Assert.False(File.Exists(workspace.StagingPath));
    }

    private static DatabaseStartupGate CreateGate(
        StartupWorkspace workspace,
        RecordingHealthChecker health,
        RecordingBackupService backup,
        bool acceptanceMode = false)
    {
        var recovery = new SqliteRecoveryService(
            health,
            workspace.DataDirectory,
            new TestLogger(),
            new FixedTimeProvider());
        return new DatabaseStartupGate(
            workspace.ProductionPath,
            workspace.BackupRoot,
            health,
            backup,
            recovery,
            acceptanceMode,
            new TestLogger());
    }

    private static RecordingHealthChecker CreateCorruptStartupHealth(
        string? candidatePath,
        SqliteDatabaseHealthState candidateState)
    {
        return new RecordingHealthChecker((path, mode) =>
            mode == SqliteInspectionMode.StartupFast
                ? SqliteDatabaseHealthState.Corrupt
                : path == candidatePath
                    ? candidateState
                    : SqliteDatabaseHealthState.Healthy);
    }

    private static void AssertMarkerEqual(SqliteRecoveryMarker expected, SqliteRecoveryMarker? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.RecoveryStarted, actual!.RecoveryStarted);
        Assert.Equal(expected.SourceBackupPath, actual.SourceBackupPath);
        Assert.Equal(expected.CorruptBundlePath, actual.CorruptBundlePath);
        Assert.Equal(expected.StagingPath, actual.StagingPath);
        Assert.Equal(expected.StartedAt, actual.StartedAt);
        Assert.Equal(expected.BundleManifestSha256, actual.BundleManifestSha256);
    }

    private sealed class RecordingHealthChecker : ISqliteDatabaseHealthChecker
    {
        private readonly Func<string, SqliteInspectionMode, SqliteDatabaseHealthState> _stateFactory;

        public RecordingHealthChecker(
            Func<string, SqliteInspectionMode, SqliteDatabaseHealthState> stateFactory)
        {
            _stateFactory = stateFactory;
        }

        public List<(string Path, SqliteInspectionMode Mode, SqliteDatabaseHealthState State)> Calls { get; } = new();

        public SqliteDatabaseHealthResult Inspect(string databasePath, SqliteInspectionMode mode)
        {
            var state = _stateFactory(databasePath, mode);
            Calls.Add((databasePath, mode, state));
            var healthy = state == SqliteDatabaseHealthState.Healthy;
            return new SqliteDatabaseHealthResult(
                databasePath,
                state,
                StartedAt,
                healthy,
                healthy ? "ok" : state.ToString(),
                true,
                healthy,
                healthy ? "ok" : state.ToString(),
                healthy,
                healthy ? "ok" : state.ToString(),
                schemaVersion: state == SqliteDatabaseHealthState.UnsupportedSchema ? 4 : 3);
        }
    }

    private sealed class RecordingBackupService : ISqliteBackupService
    {
        private readonly IReadOnlyList<SqliteBackupCandidate> _candidates;

        public RecordingBackupService(IReadOnlyList<SqliteBackupCandidate> candidates)
        {
            _candidates = candidates;
        }

        public int ScanCalls { get; private set; }

        public Task<SqliteBackupResult> CreateValidatedBackupAsync(
            string productionDatabasePath,
            string backupRootDirectory,
            DateTimeOffset localNow,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SqliteBackupResult(false, null, "should not be called"));

        public IReadOnlyList<SqliteBackupCandidate> ScanCandidates(string backupRootDirectory)
        {
            ScanCalls++;
            return _candidates;
        }
    }

    private sealed class FixedTimeProvider : IRfidTimeProvider
    {
        public DateTimeOffset UtcNow => StartedAt.ToUniversalTime();

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TestLogger : ILogger
    {
        public void Information(string message) { }

        public void Warning(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class StartupWorkspace : IDisposable
    {
        private int _candidateIndex;

        private StartupWorkspace(string root)
        {
            Root = root;
            DataDirectory = Path.Combine(root, "Data");
            BackupRoot = Path.Combine(root, "Backups", "SQLite");
            CorruptRoot = Path.Combine(DataDirectory, "Corrupt");
            ProductionPath = Path.Combine(DataDirectory, "MineRailMonitor.db");
            MarkerPath = Path.Combine(DataDirectory, ".sqlite-recovery-in-progress");
            StagingPath = Path.Combine(DataDirectory, "MineRailMonitor.restore.tmp.db");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(BackupRoot);
            Directory.CreateDirectory(CorruptRoot);
            File.WriteAllText(ProductionPath, "production", Encoding.UTF8);
        }

        public string Root { get; }

        public string DataDirectory { get; }

        public string BackupRoot { get; }

        public string CorruptRoot { get; }

        public string ProductionPath { get; }

        public string MarkerPath { get; }

        public string StagingPath { get; }

        public static StartupWorkspace Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));
            return new StartupWorkspace(root);
        }

        public SqliteBackupCandidate CreateCandidate(string name)
        {
            var timestamp = StartedAt.AddMinutes(-_candidateIndex++);
            var directory = Path.Combine(BackupRoot, timestamp.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(
                directory,
                $"MineRailMonitor_{timestamp:yyyyMMdd_HHmmss}.db");
            File.WriteAllText(path, name, Encoding.UTF8);
            return new SqliteBackupCandidate(path, timestamp);
        }

        public SqliteRecoveryMarker WriteMarker()
        {
            var marker = new SqliteRecoveryMarker(
                Path.Combine(Root, "source.db"),
                Path.Combine(CorruptRoot, "bundle"),
                StagingPath,
                StartedAt,
                new string('0', 64));
            new SqliteRecoveryMarkerStore(DataDirectory).WriteMarker(marker);
            return marker;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
