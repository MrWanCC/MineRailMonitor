using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Infrastructure.Logging;
using MineRailMonitor.Infrastructure.Persistence;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class SqliteRecoveryServiceTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 9, 20, 10, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Recover_validates_staging_before_touching_production()
    {
        using var workspace = RecoveryWorkspace.Create();
        var before = workspace.SnapshotProductionFiles();
        var health = new RecordingHealthChecker((path, _, _) =>
            path == workspace.StagingPath
                ? SqliteDatabaseHealthState.Corrupt
                : SqliteDatabaseHealthState.Healthy);
        var service = CreateService(workspace, health);

        var result = service.Recover(workspace.ProductionPath, workspace.Candidate);

        Assert.False(result.Succeeded);
        Assert.Equal(before, workspace.SnapshotProductionFiles());
        Assert.False(File.Exists(workspace.MarkerPath));
        Assert.DoesNotContain(health.Calls, call => call.Path == workspace.ProductionPath);
    }

    [Fact]
    public void Recover_saves_db_wal_shm_before_destructive_replacement()
    {
        using var workspace = RecoveryWorkspace.Create(includeSidecars: true);
        var service = CreateService(workspace, new RecordingHealthChecker((_, _, _) =>
            SqliteDatabaseHealthState.Healthy));

        var result = service.Recover(workspace.ProductionPath, workspace.Candidate);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var bundle = Assert.Single(Directory.GetDirectories(workspace.CorruptRoot));
        Assert.Equal(workspace.ProductionBytes, File.ReadAllBytes(Path.Combine(bundle, "MineRailMonitor.db")));
        Assert.Equal(workspace.WalBytes, File.ReadAllBytes(Path.Combine(bundle, "MineRailMonitor.db-wal")));
        Assert.Equal(workspace.ShmBytes, File.ReadAllBytes(Path.Combine(bundle, "MineRailMonitor.db-shm")));
        Assert.True(File.Exists(Path.Combine(bundle, "bundle-manifest.json")));
    }

    [Fact]
    public void Recover_persists_marker_before_sidecar_removal()
    {
        using var workspace = RecoveryWorkspace.Create(includeSidecars: true);
        var phases = new List<SqliteRecoveryPhase>();
        var service = CreateService(
            workspace,
            new RecordingHealthChecker((_, _, _) => SqliteDatabaseHealthState.Healthy),
            phases.Add);

        var result = service.Recover(workspace.ProductionPath, workspace.Candidate);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(
            new[]
            {
                SqliteRecoveryPhase.StagingValidated,
                SqliteRecoveryPhase.CorruptBundleSaved,
                SqliteRecoveryPhase.RecoveryMarkerPersisted,
                SqliteRecoveryPhase.SidecarsRemoved,
                SqliteRecoveryPhase.ProductionReplacementStarted,
                SqliteRecoveryPhase.FinalHealthCheckCompleted
            },
            phases);
    }

    [Fact]
    public void Final_health_failure_keeps_bundle_marker_and_failure_evidence()
    {
        using var workspace = RecoveryWorkspace.Create();
        var health = new RecordingHealthChecker((path, _, callNumber) =>
            path == workspace.ProductionPath && callNumber >= 2
                ? SqliteDatabaseHealthState.Corrupt
                : SqliteDatabaseHealthState.Healthy);
        var service = CreateService(workspace, health);

        var result = service.Recover(workspace.ProductionPath, workspace.Candidate);

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(workspace.MarkerPath));
        Assert.Single(Directory.GetDirectories(workspace.CorruptRoot));
        Assert.True(File.Exists(workspace.ProductionPath));
    }

    [Fact]
    public void Recover_removes_marker_only_after_final_health_succeeds()
    {
        using var workspace = RecoveryWorkspace.Create();
        var service = CreateService(workspace, new RecordingHealthChecker((_, _, _) =>
            SqliteDatabaseHealthState.Healthy));

        var result = service.Recover(workspace.ProductionPath, workspace.Candidate);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.False(File.Exists(workspace.MarkerPath));
        Assert.False(File.Exists(workspace.ProductionPath + "-wal"));
        Assert.False(File.Exists(workspace.ProductionPath + "-shm"));
        Assert.False(File.Exists(workspace.Candidate.Path + "-wal"));
        Assert.False(File.Exists(workspace.Candidate.Path + "-shm"));
    }

    [Fact]
    public void Recover_revalidates_selected_candidate_with_full_validation()
    {
        using var workspace = RecoveryWorkspace.Create();
        var health = new RecordingHealthChecker((_, _, _) => SqliteDatabaseHealthState.Corrupt);
        var service = CreateService(workspace, health);

        var result = service.Recover(workspace.ProductionPath, workspace.Candidate);

        Assert.False(result.Succeeded);
        var call = Assert.Single(health.Calls);
        Assert.Equal(workspace.Candidate.Path, call.Path);
        Assert.Equal(SqliteInspectionMode.FullValidation, call.Mode);
        Assert.False(File.Exists(workspace.MarkerPath));
    }

    [Fact]
    public void Resume_interrupted_recovery_revalidates_source_and_new_staging()
    {
        using var workspace = RecoveryWorkspace.Create();
        var marker = workspace.CreateMarker();
        workspace.CreateBundle(marker.CorruptBundlePath);
        var markerStore = new SqliteRecoveryMarkerStore(workspace.DataDirectory);
        markerStore.WriteMarker(marker);
        var health = new RecordingHealthChecker((_, _, _) => SqliteDatabaseHealthState.Healthy);
        var service = CreateService(workspace, health);

        var result = service.ResumeInterruptedRecovery(workspace.ProductionPath, marker);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(3, health.Calls.Count);
        Assert.Equal(marker.SourceBackupPath, health.Calls[0].Path);
        Assert.Equal(SqliteInspectionMode.FullValidation, health.Calls[0].Mode);
        Assert.Equal(marker.StagingPath, health.Calls[1].Path);
        Assert.Equal(SqliteInspectionMode.FullValidation, health.Calls[1].Mode);
        Assert.False(File.Exists(workspace.MarkerPath));
    }

    [Fact]
    public void Resume_does_not_overwrite_original_corrupt_bundle()
    {
        using var workspace = RecoveryWorkspace.Create(includeSidecars: true);
        var marker = workspace.CreateMarker();
        workspace.CreateBundle(marker.CorruptBundlePath, includeSidecars: true);
        var before = SnapshotDirectory(marker.CorruptBundlePath);
        new SqliteRecoveryMarkerStore(workspace.DataDirectory).WriteMarker(marker);
        var service = CreateService(workspace, new RecordingHealthChecker((_, _, _) =>
            SqliteDatabaseHealthState.Healthy));

        var result = service.ResumeInterruptedRecovery(workspace.ProductionPath, marker);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(before, SnapshotDirectory(marker.CorruptBundlePath));
    }

    [Fact]
    public void Resume_with_missing_bundle_does_not_touch_production()
    {
        using var workspace = RecoveryWorkspace.Create();
        var marker = workspace.CreateMarker();
        new SqliteRecoveryMarkerStore(workspace.DataDirectory).WriteMarker(marker);
        var before = workspace.SnapshotProductionFiles();
        var service = CreateService(workspace, new RecordingHealthChecker((_, _, _) =>
            SqliteDatabaseHealthState.Healthy));

        var result = service.ResumeInterruptedRecovery(workspace.ProductionPath, marker);

        Assert.False(result.Succeeded);
        Assert.Equal(before, workspace.SnapshotProductionFiles());
        Assert.True(File.Exists(workspace.MarkerPath));
    }

    [Fact]
    public void Resume_with_corrupt_source_keeps_marker_and_bundle()
    {
        using var workspace = RecoveryWorkspace.Create();
        var marker = workspace.CreateMarker();
        workspace.CreateBundle(marker.CorruptBundlePath);
        new SqliteRecoveryMarkerStore(workspace.DataDirectory).WriteMarker(marker);
        var before = SnapshotDirectory(marker.CorruptBundlePath);
        var service = CreateService(workspace, new RecordingHealthChecker((path, _, _) =>
            path == marker.SourceBackupPath
                ? SqliteDatabaseHealthState.Corrupt
                : SqliteDatabaseHealthState.Healthy));

        var result = service.ResumeInterruptedRecovery(workspace.ProductionPath, marker);

        Assert.False(result.Succeeded);
        Assert.Equal(before, SnapshotDirectory(marker.CorruptBundlePath));
        Assert.True(File.Exists(workspace.MarkerPath));
        Assert.Equal(workspace.ProductionBytes, File.ReadAllBytes(workspace.ProductionPath));
    }

    [Fact]
    public void Resume_after_sidecar_removal_can_complete()
    {
        using var workspace = RecoveryWorkspace.Create();
        var marker = workspace.CreateMarker();
        workspace.CreateBundle(marker.CorruptBundlePath);
        File.Delete(workspace.ProductionPath + "-wal");
        File.Delete(workspace.ProductionPath + "-shm");
        new SqliteRecoveryMarkerStore(workspace.DataDirectory).WriteMarker(marker);
        var service = CreateService(workspace, new RecordingHealthChecker((_, _, _) =>
            SqliteDatabaseHealthState.Healthy));

        var result = service.ResumeInterruptedRecovery(workspace.ProductionPath, marker);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.False(File.Exists(workspace.MarkerPath));
    }

    [Fact]
    public void Resume_after_production_replacement_started_can_complete()
    {
        using var workspace = RecoveryWorkspace.Create();
        var marker = workspace.CreateMarker();
        workspace.CreateBundle(marker.CorruptBundlePath);
        File.Delete(workspace.ProductionPath);
        new SqliteRecoveryMarkerStore(workspace.DataDirectory).WriteMarker(marker);
        var service = CreateService(workspace, new RecordingHealthChecker((_, _, _) =>
            SqliteDatabaseHealthState.Healthy));

        var result = service.ResumeInterruptedRecovery(workspace.ProductionPath, marker);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(File.Exists(workspace.ProductionPath));
        Assert.False(File.Exists(workspace.MarkerPath));
    }

    [Fact]
    public void Resume_removes_marker_only_after_final_healthy()
    {
        using var workspace = RecoveryWorkspace.Create();
        var marker = workspace.CreateMarker();
        workspace.CreateBundle(marker.CorruptBundlePath);
        new SqliteRecoveryMarkerStore(workspace.DataDirectory).WriteMarker(marker);
        var service = CreateService(workspace, new RecordingHealthChecker((path, _, _) =>
            path == marker.StagingPath
                ? SqliteDatabaseHealthState.Healthy
                : path == workspace.ProductionPath
                    ? SqliteDatabaseHealthState.Corrupt
                    : SqliteDatabaseHealthState.Healthy));

        var result = service.ResumeInterruptedRecovery(workspace.ProductionPath, marker);

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(workspace.MarkerPath));
        Assert.True(Directory.Exists(marker.CorruptBundlePath));
    }

    [Fact]
    public void Recover_refuses_to_start_when_recovery_marker_already_exists()
    {
        using var workspace = RecoveryWorkspace.Create();
        var marker = workspace.CreateMarker();
        new SqliteRecoveryMarkerStore(workspace.DataDirectory).WriteMarker(marker);
        var health = new RecordingHealthChecker((_, _, _) => SqliteDatabaseHealthState.Healthy);
        var service = CreateService(workspace, health);
        var before = workspace.SnapshotProductionFiles();

        var result = service.Recover(workspace.ProductionPath, workspace.Candidate);

        Assert.False(result.Succeeded);
        Assert.Empty(health.Calls);
        Assert.Equal(before, workspace.SnapshotProductionFiles());
    }

    [Fact]
    public void Recover_bundle_manifest_matches_original_evidence()
    {
        using var workspace = RecoveryWorkspace.Create(includeSidecars: true);
        var service = CreateService(workspace, new RecordingHealthChecker((_, _, _) =>
            SqliteDatabaseHealthState.Healthy));

        var result = service.Recover(workspace.ProductionPath, workspace.Candidate);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var bundle = Assert.Single(Directory.GetDirectories(workspace.CorruptRoot));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "bundle-manifest.json")));
        var evidence = document.RootElement.GetProperty("files");
        Assert.Equal(3, evidence.GetArrayLength());
        foreach (var item in evidence.EnumerateArray())
        {
            var fileName = item.GetProperty("fileName").GetString()!;
            var copiedPath = Path.Combine(bundle, fileName);
            Assert.Equal(new FileInfo(copiedPath).Length, item.GetProperty("length").GetInt64());
            Assert.Equal(Sha256(copiedPath), item.GetProperty("sha256").GetString());
        }
    }

    [Fact]
    public void Resume_with_tampered_bundle_does_not_touch_production()
    {
        using var workspace = RecoveryWorkspace.Create();
        var marker = workspace.CreateMarker();
        workspace.CreateBundle(marker.CorruptBundlePath);
        new SqliteRecoveryMarkerStore(workspace.DataDirectory).WriteMarker(marker);
        File.AppendAllText(Path.Combine(marker.CorruptBundlePath, "MineRailMonitor.db"), "tampered");
        var before = workspace.SnapshotProductionFiles();
        var health = new RecordingHealthChecker((_, _, _) => SqliteDatabaseHealthState.Healthy);
        var service = CreateService(workspace, health);

        var result = service.ResumeInterruptedRecovery(workspace.ProductionPath, marker);

        Assert.False(result.Succeeded);
        Assert.Equal(before, workspace.SnapshotProductionFiles());
        Assert.Empty(health.Calls);
        Assert.True(File.Exists(workspace.MarkerPath));
    }

    [Fact]
    public void Resume_with_missing_manifest_evidence_does_not_touch_production()
    {
        using var workspace = RecoveryWorkspace.Create(includeSidecars: true);
        var marker = workspace.CreateMarker();
        workspace.CreateBundle(marker.CorruptBundlePath, includeSidecars: true);
        File.Delete(Path.Combine(marker.CorruptBundlePath, "MineRailMonitor.db-wal"));
        new SqliteRecoveryMarkerStore(workspace.DataDirectory).WriteMarker(marker);
        var before = workspace.SnapshotProductionFiles();
        var service = CreateService(workspace, new RecordingHealthChecker((_, _, _) =>
            SqliteDatabaseHealthState.Healthy));

        var result = service.ResumeInterruptedRecovery(workspace.ProductionPath, marker);

        Assert.False(result.Succeeded);
        Assert.Equal(before, workspace.SnapshotProductionFiles());
        Assert.True(File.Exists(workspace.MarkerPath));
    }

    [Fact]
    public void Malformed_marker_is_not_treated_as_marker_absent()
    {
        using var workspace = RecoveryWorkspace.Create();
        File.WriteAllText(workspace.MarkerPath, "{not-json");
        var service = CreateService(workspace, new RecordingHealthChecker((_, _, _) =>
            SqliteDatabaseHealthState.Healthy));

        Assert.Throws<InvalidDataException>(() => service.ReadMarker());
        var result = service.Recover(workspace.ProductionPath, workspace.Candidate);

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(workspace.MarkerPath));
    }

    [Fact]
    public void Recover_does_not_accept_unsupported_schema_candidate()
    {
        using var workspace = RecoveryWorkspace.Create();
        var health = new RecordingHealthChecker((_, _, _) =>
            SqliteDatabaseHealthState.UnsupportedSchema);
        var service = CreateService(workspace, health);

        var result = service.Recover(workspace.ProductionPath, workspace.Candidate);

        Assert.False(result.Succeeded);
        Assert.Single(health.Calls);
        Assert.False(File.Exists(workspace.MarkerPath));
        Assert.Empty(Directory.GetDirectories(workspace.CorruptRoot));
    }

    private static SqliteRecoveryService CreateService(
        RecoveryWorkspace workspace,
        RecordingHealthChecker health,
        Action<SqliteRecoveryPhase>? phaseObserver = null) =>
        new(
            health,
            workspace.DataDirectory,
            new TestLogger(),
            new FixedTimeProvider(),
            phaseObserver);

    private static Dictionary<string, string> SnapshotDirectory(string directory)
    {
        return Directory
            .GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllText);
    }

    private static string Sha256(string path)
    {
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(File.ReadAllBytes(path)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private sealed class RecordingHealthChecker : ISqliteDatabaseHealthChecker
    {
        private readonly Func<string, SqliteInspectionMode, int, SqliteDatabaseHealthState> _stateFactory;

        public RecordingHealthChecker(
            Func<string, SqliteInspectionMode, int, SqliteDatabaseHealthState> stateFactory)
        {
            _stateFactory = stateFactory;
        }

        public List<(string Path, SqliteInspectionMode Mode)> Calls { get; } = new();

        public SqliteDatabaseHealthResult Inspect(string databasePath, SqliteInspectionMode mode)
        {
            var callNumber = Calls.Count;
            Calls.Add((databasePath, mode));
            var state = _stateFactory(databasePath, mode, callNumber);
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

    private sealed class RecoveryWorkspace : IDisposable
    {
        private RecoveryWorkspace(string root)
        {
            Root = root;
            DataDirectory = Path.Combine(root, "Data");
            CorruptRoot = Path.Combine(DataDirectory, "Corrupt");
            ProductionPath = Path.Combine(DataDirectory, "MineRailMonitor.db");
            CandidatePath = Path.Combine(root, "MineRailMonitor_20260920_103000.db");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(CorruptRoot);
            ProductionBytes = Encoding.UTF8.GetBytes("production-original");
            CandidateBytes = Encoding.UTF8.GetBytes("candidate-healthy");
            WalBytes = Encoding.UTF8.GetBytes("production-wal");
            ShmBytes = Encoding.UTF8.GetBytes("production-shm");
            File.WriteAllBytes(ProductionPath, ProductionBytes);
            File.WriteAllBytes(CandidatePath, CandidateBytes);
        }

        public string Root { get; }

        public string DataDirectory { get; }

        public string CorruptRoot { get; }

        public string ProductionPath { get; }

        public string CandidatePath { get; }

        public string StagingPath => Path.Combine(DataDirectory, "MineRailMonitor.restore.tmp.db");

        public string MarkerPath => Path.Combine(DataDirectory, ".sqlite-recovery-in-progress");

        public byte[] ProductionBytes { get; }

        public byte[] CandidateBytes { get; }

        public byte[] WalBytes { get; }

        public byte[] ShmBytes { get; }

        public SqliteBackupCandidate Candidate => new(CandidatePath, StartedAt);

        public static RecoveryWorkspace Create(bool includeSidecars = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));
            var workspace = new RecoveryWorkspace(root);
            if (includeSidecars)
            {
                File.WriteAllBytes(workspace.ProductionPath + "-wal", workspace.WalBytes);
                File.WriteAllBytes(workspace.ProductionPath + "-shm", workspace.ShmBytes);
            }

            return workspace;
        }

        public SqliteRecoveryMarker CreateMarker() =>
            new(
                CandidatePath,
                Path.Combine(CorruptRoot, "20260920_103000_000"),
                Path.Combine(DataDirectory, "MineRailMonitor.restore.tmp.db"),
                StartedAt);

        public void CreateBundle(string bundlePath, bool includeSidecars = false)
        {
            Directory.CreateDirectory(bundlePath);
            File.WriteAllBytes(Path.Combine(bundlePath, "MineRailMonitor.db"), ProductionBytes);
            if (includeSidecars)
            {
                File.WriteAllBytes(Path.Combine(bundlePath, "MineRailMonitor.db-wal"), WalBytes);
                File.WriteAllBytes(Path.Combine(bundlePath, "MineRailMonitor.db-shm"), ShmBytes);
            }

            var files = Directory
                .GetFiles(bundlePath, "MineRailMonitor.db*", SearchOption.TopDirectoryOnly)
                .Select(path => new
                {
                    fileName = Path.GetFileName(path),
                    length = new FileInfo(path).Length,
                    sha256 = Sha256(path)
                })
                .ToArray();
            File.WriteAllText(
                Path.Combine(bundlePath, "bundle-manifest.json"),
                JsonSerializer.Serialize(new { files }));
        }

        public Dictionary<string, string> SnapshotProductionFiles() =>
            Directory
                .GetFiles(DataDirectory, "MineRailMonitor.db*", SearchOption.TopDirectoryOnly)
                .ToDictionary(path => Path.GetFileName(path), File.ReadAllText);

        public void Dispose() => DeleteDirectory(Root);
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive: true);
    }
}
