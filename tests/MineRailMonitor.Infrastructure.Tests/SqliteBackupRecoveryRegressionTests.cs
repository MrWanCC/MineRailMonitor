using System.Data.SQLite;
using System.Security.Cryptography;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Infrastructure.Logging;
using MineRailMonitor.Infrastructure.Persistence;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class SqliteBackupRecoveryRegressionTests
{
    private static readonly DateTimeOffset LocalNow =
        new(2026, 9, 20, 10, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task Schema_v1_v2_v3_migrations_remain_unchanged_after_health_and_backup_inspection()
    {
        foreach (var version in new[] { 1, 2, 3 })
        {
            using var workspace = TestWorkspace.Create();
            var expectedPassageId = CreateSchemaFixture(workspace.ProductionPath, version);
            var checker = CreateHealthChecker();
            var beforeVersion = ReadUserVersion(workspace.ProductionPath);
            var beforeRecordBytes = File.ReadAllBytes(workspace.ProductionPath);

            var health = checker.Inspect(workspace.ProductionPath, SqliteInspectionMode.StartupFast);
            Assert.Equal(SqliteDatabaseHealthState.Healthy, health.State);
            Assert.Equal(beforeVersion, ReadUserVersion(workspace.ProductionPath));

            var backup = await CreateBackupAsync(workspace, checker);
            Assert.Equal(beforeVersion, ReadUserVersion(workspace.ProductionPath));
            Assert.Equal(beforeRecordBytes, File.ReadAllBytes(workspace.ProductionPath));

            using (var store = new SqlitePassageRecordStore(workspace.ProductionPath))
            {
                var record = store.GetDetails(expectedPassageId);
                Assert.NotNull(record);
                Assert.Equal(3, ReadUserVersion(workspace.ProductionPath));

                if (version == 1)
                {
                    Assert.Equal(PassageRecord.LegacyStationId, record!.StationId);
                }
                else
                {
                    Assert.Equal("RFID-560-01", record!.StationId);
                }

                if (version == 2)
                {
                    Assert.Equal(PassageOutcome.UncouplingAlarm, record.Outcome);
                    Assert.Equal(PassageClearState.Cleared, record.ClearState);
                    Assert.Equal(record.CompletedAt, record.AlarmAcknowledgedAt);
                    Assert.Equal(record.CompletedAt, record.AlarmRecoveredAt);
                }

                if (version == 3)
                {
                    Assert.Equal(new ushort[] { 0x1001, 0x1002 }, record.ObservedRfids);
                }
            }

            Assert.True(File.Exists(backup.FinalPath));
            Assert.Equal(3, ReadUserVersion(workspace.ProductionPath));
        }
    }

    [Fact]
    public async Task Alarm_ack_recovery_and_pending_clear_survive_backup_and_restore()
    {
        using var workspace = TestWorkspace.Create();
        var acknowledgedAt = LocalNow.AddMinutes(1);
        var recoveredAt = LocalNow.AddMinutes(2);
        var alarmPending = CreateRecord("RFID-560-01", 0x01, PassageOutcome.UncouplingAlarm);
        var alarmRecovered = CreateRecord(
            "RFID-560-02",
            0x02,
            PassageOutcome.UncouplingAlarm,
            PassageClearState.Cleared,
            recoveredAt,
            acknowledgedAt,
            recoveredAt);
        var ordinaryPending = CreateRecord("RFID-560-03", 0x03, PassageOutcome.Completed);

        using (var store = new SqlitePassageRecordStore(workspace.ProductionPath))
        {
            store.Save(alarmPending);
            store.MarkAlarmAcknowledged(alarmPending.PassageId, acknowledgedAt);
            store.Save(alarmRecovered);
            store.Save(ordinaryPending);
        }

        var checker = CreateHealthChecker();
        var backup = await CreateBackupAsync(workspace, checker);
        File.WriteAllText(workspace.ProductionPath, "corrupt production");

        var recovery = CreateRecoveryService(workspace, checker);
        var result = recovery.Recover(
            workspace.ProductionPath,
            new SqliteBackupCandidate(backup.FinalPath!, LocalNow));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(SqliteDatabaseHealthState.Healthy,
            checker.Inspect(workspace.ProductionPath, SqliteInspectionMode.FullValidation).State);

        using var restored = new SqlitePassageRecordStore(workspace.ProductionPath);
        var restoredPending = restored.GetDetails(alarmPending.PassageId)!;
        AssertRecord(restoredPending, alarmPending, acknowledgedAt);
        AssertRecord(restored.GetDetails(alarmRecovered.PassageId)!, alarmRecovered);
        AssertRecord(restored.GetDetails(ordinaryPending.PassageId)!, ordinaryPending);
        Assert.Equal(2, restored.GetDetails(alarmPending.PassageId)!.RfidObservations.Count);
    }

    [Fact]
    public async Task Backup_restore_preserves_distinct_station_identity_when_protocol_addresses_repeat()
    {
        using var workspace = TestWorkspace.Create();
        var first = CreateRecord("RFID-560-01", 0x01, PassageOutcome.Completed);
        var second = CreateRecord("RFID-620-01", 0x01, PassageOutcome.Completed);

        using (var store = new SqlitePassageRecordStore(workspace.ProductionPath))
        {
            store.Save(first);
            store.Save(second);
        }

        var checker = CreateHealthChecker();
        var backup = await CreateBackupAsync(workspace, checker);
        File.WriteAllText(workspace.ProductionPath, "corrupt production");
        var result = CreateRecoveryService(workspace, checker).Recover(
            workspace.ProductionPath,
            new SqliteBackupCandidate(backup.FinalPath!, LocalNow));
        Assert.True(result.Succeeded, result.ErrorMessage);

        using var restored = new SqlitePassageRecordStore(workspace.ProductionPath);
        Assert.Equal(
            new[] { "RFID-560-01" },
            restored.Query(new PassageQuery { StationIds = new[] { "RFID-560-01" }, PageSize = 20 })
                .Items.Select(item => item.StationId));
        Assert.Equal(
            new[] { "RFID-620-01" },
            restored.Query(new PassageQuery { StationIds = new[] { "RFID-620-01" }, PageSize = 20 })
                .Items.Select(item => item.StationId));

        var statistics = restored.GetStatistics(LocalNow);
        Assert.Equal(1, statistics.ByStation["RFID-560-01"]);
        Assert.Equal(1, statistics.ByStation["RFID-620-01"]);
    }

    [Fact]
    public async Task Raw_packet_black_box_files_are_not_touched_by_backup_or_recovery()
    {
        using var workspace = TestWorkspace.Create();
        Directory.CreateDirectory(workspace.BlackBoxRoot);
        var blackBoxFiles = new[]
        {
            CreateBlackBoxFile(workspace, "560", "560 packet"),
            CreateBlackBoxFile(workspace, "620", "620 packet")
        };
        var before = blackBoxFiles.ToDictionary(path => path, SnapshotFile);

        using (var store = new SqlitePassageRecordStore(workspace.ProductionPath))
        {
            store.Save(CreateRecord("RFID-560-01", 0x01, PassageOutcome.Completed));
        }

        var checker = CreateHealthChecker();
        var backup = await CreateBackupAsync(workspace, checker);
        File.WriteAllText(workspace.ProductionPath, "corrupt production");
        var result = CreateRecoveryService(workspace, checker).Recover(
            workspace.ProductionPath,
            new SqliteBackupCandidate(backup.FinalPath!, LocalNow));

        Assert.True(result.Succeeded, result.ErrorMessage);
        var after = blackBoxFiles.ToDictionary(path => path, SnapshotFile);
        Assert.Equal(before.Keys.OrderBy(path => path), after.Keys.OrderBy(path => path));
        foreach (var path in before.Keys)
        {
            Assert.Equal(before[path], after[path]);
        }
    }

    [Fact]
    public async Task Interrupted_recovery_crash_boundaries_never_initialize_empty_database()
    {
        foreach (var boundary in new[]
        {
            SqliteRecoveryPhase.CorruptBundleSaved,
            SqliteRecoveryPhase.SidecarsRemoved,
            SqliteRecoveryPhase.ProductionReplacementStarted
        })
        {
            using var workspace = TestWorkspace.Create();
            using (var store = new SqlitePassageRecordStore(workspace.ProductionPath))
            {
                store.Save(CreateRecord("RFID-560-01", 0x01, PassageOutcome.Completed));
            }

            var checker = CreateHealthChecker();
            var backup = await CreateBackupAsync(workspace, checker);
            File.WriteAllText(workspace.ProductionPath, "corrupt production");
            var trigger = boundary == SqliteRecoveryPhase.CorruptBundleSaved
                ? SqliteRecoveryPhase.RecoveryMarkerPersisted
                : boundary;
            var phases = new List<SqliteRecoveryPhase>();
            var recovery = CreateRecoveryService(workspace, checker, phase =>
            {
                phases.Add(phase);
                if (phase == trigger)
                {
                    throw new SimulatedCrashException(boundary.ToString());
                }
            });

            var result = recovery.Recover(
                workspace.ProductionPath,
                new SqliteBackupCandidate(backup.FinalPath!, LocalNow));

            Assert.False(result.Succeeded);
            Assert.Contains(boundary, phases);
            var marker = recovery.ReadMarker();
            Assert.NotNull(marker);
            File.Delete(workspace.ProductionPath);

            var gate = CreateGate(workspace, checker);
            var decision = gate.Inspect();
            Assert.Equal(DatabaseStartupDecisionKind.InterruptedRecovery, decision.Kind);
            Assert.False(File.Exists(workspace.ProductionPath));
        }
    }

    [Fact]
    public async Task Interrupted_recovery_resume_revalidates_and_finishes_without_empty_database()
    {
        using (var workspace = TestWorkspace.Create())
        {
            using (var store = new SqlitePassageRecordStore(workspace.ProductionPath))
            {
                store.Save(CreateRecord("RFID-560-01", 0x01, PassageOutcome.Completed));
            }

            var checker = CreateHealthChecker();
            var backup = await CreateBackupAsync(workspace, checker);
            File.WriteAllText(workspace.ProductionPath, "corrupt production");
            var recovery = CreateRecoveryService(workspace, checker, phase =>
            {
                if (phase == SqliteRecoveryPhase.SidecarsRemoved)
                {
                    throw new SimulatedCrashException("sidecars removed");
                }
            });
            var interrupted = recovery.Recover(
                workspace.ProductionPath,
                new SqliteBackupCandidate(backup.FinalPath!, LocalNow));
            Assert.False(interrupted.Succeeded);
            var marker = recovery.ReadMarker()!;
            Assert.NotNull(marker);

            var resume = CreateRecoveryService(workspace, checker).ResumeInterruptedRecovery(
                workspace.ProductionPath,
                marker);
            Assert.True(resume.Succeeded, resume.ErrorMessage);
            Assert.Null(CreateRecoveryService(workspace, checker).ReadMarker());
            using var restored = new SqlitePassageRecordStore(workspace.ProductionPath);
            Assert.NotNull(restored.GetDetails(
                Assert.Single(restored.Records).PassageId));
        }

        using (var workspace = TestWorkspace.Create())
        {
            using (var store = new SqlitePassageRecordStore(workspace.ProductionPath))
            {
                store.Save(CreateRecord("RFID-560-01", 0x01, PassageOutcome.Completed));
            }

            var checker = CreateHealthChecker();
            var backup = await CreateBackupAsync(workspace, checker);
            File.WriteAllText(workspace.ProductionPath, "corrupt production");
            var recovery = CreateRecoveryService(workspace, checker, phase =>
            {
                if (phase == SqliteRecoveryPhase.SidecarsRemoved)
                {
                    throw new SimulatedCrashException("sidecars removed");
                }
            });
            Assert.False(recovery.Recover(
                workspace.ProductionPath,
                new SqliteBackupCandidate(backup.FinalPath!, LocalNow)).Succeeded);
            var marker = recovery.ReadMarker()!;
            Assert.NotNull(marker);
            File.WriteAllText(marker.SourceBackupPath, "tampered source");

            var failedResume = CreateRecoveryService(workspace, checker)
                .ResumeInterruptedRecovery(workspace.ProductionPath, marker);
            Assert.False(failedResume.Succeeded);
            Assert.NotNull(CreateRecoveryService(workspace, checker).ReadMarker());
            Assert.Equal(
                DatabaseStartupDecisionKind.InterruptedRecovery,
                CreateGate(workspace, checker).Inspect().Kind);
        }
    }

    [Fact]
    public void Unavailable_never_reaches_recovery_candidate_selection()
    {
        using var workspace = TestWorkspace.Create();
        File.WriteAllText(workspace.ProductionPath, "production bytes");
        var before = SnapshotFile(workspace.ProductionPath);
        var checker = new RecordingHealthChecker((path, _) =>
            path == workspace.ProductionPath
                ? SqliteDatabaseHealthState.Unavailable
                : SqliteDatabaseHealthState.Healthy);
        var backup = new RecordingBackupService();

        var decision = new DatabaseStartupGate(
            workspace.ProductionPath,
            workspace.BackupRoot,
            checker,
            backup,
            CreateRecoveryService(workspace, checker),
            acceptanceMode: false,
            new TestLogger()).Inspect();

        Assert.Equal(DatabaseStartupDecisionKind.Unavailable, decision.Kind);
        Assert.Equal(0, backup.ScanCalls);
        Assert.Equal(before, SnapshotFile(workspace.ProductionPath));
        Assert.False(Directory.Exists(workspace.CorruptRoot));
        Assert.False(File.Exists(workspace.MarkerPath));
    }

    [Fact]
    public void Newer_schema_never_enters_recovery_or_store_creation()
    {
        using (var workspace = TestWorkspace.Create())
        {
            CreateFutureSchemaDatabase(workspace.ProductionPath);
            var before = SnapshotFile(workspace.ProductionPath);
            var checker = CreateHealthChecker();
            var decision = CreateGate(workspace, checker).Inspect();

            Assert.Equal(DatabaseStartupDecisionKind.UnsupportedSchema, decision.Kind);
            Assert.Equal(before, SnapshotFile(workspace.ProductionPath));
            Assert.False(Directory.Exists(workspace.CorruptRoot));
            Assert.False(File.Exists(workspace.MarkerPath));
            Assert.False(File.Exists(workspace.StagingPath));
        }

        using (var workspace = TestWorkspace.Create())
        {
            File.WriteAllText(workspace.ProductionPath, "corrupt production");
            var candidatePath = Path.Combine(
                workspace.BackupDateDirectory,
                "MineRailMonitor_20260920_103000.db");
            CreateFutureSchemaDatabase(candidatePath);
            var checker = CreateHealthChecker();
            var decision = CreateGate(workspace, checker).Inspect();

            Assert.Equal(DatabaseStartupDecisionKind.RecoverCorrupt, decision.Kind);
            Assert.Empty(decision.Candidates);
            Assert.False(Directory.Exists(workspace.CorruptRoot));
            Assert.False(File.Exists(workspace.MarkerPath));
            Assert.False(File.Exists(workspace.StagingPath));
        }
    }

    [Fact]
    public async Task Inspection_mode_routing_remains_explicit_across_components()
    {
        using var workspace = TestWorkspace.Create();
        using (var store = new SqlitePassageRecordStore(workspace.ProductionPath))
        {
            store.Save(CreateRecord("RFID-560-01", 0x01, PassageOutcome.Completed));
        }

        var checker = new RecordingHealthChecker((_, _) => SqliteDatabaseHealthState.Healthy);
        var backupService = new SqliteBackupService(checker, new TestLogger());
        var gate = new DatabaseStartupGate(
            workspace.ProductionPath,
            workspace.BackupRoot,
            checker,
            backupService,
            CreateRecoveryService(workspace, checker),
            acceptanceMode: false,
            new TestLogger());

        Assert.Equal(DatabaseStartupDecisionKind.StartHealthy, gate.Inspect().Kind);
        Assert.Equal(SqliteInspectionMode.StartupFast, checker.Calls[0].Mode);

        var backup = await backupService.CreateValidatedBackupAsync(
            workspace.ProductionPath,
            workspace.BackupRoot,
            LocalNow,
            CancellationToken.None);
        Assert.True(backup.Succeeded, backup.ErrorMessage);
        Assert.Contains(checker.Calls, call => call.Mode == SqliteInspectionMode.FullValidation);

        File.WriteAllText(workspace.ProductionPath, "corrupt production");
        var recoveryCallStart = checker.Calls.Count;
        var recovery = CreateRecoveryService(workspace, checker);
        var result = recovery.Recover(
            workspace.ProductionPath,
            new SqliteBackupCandidate(backup.FinalPath!, LocalNow));
        Assert.True(result.Succeeded, result.ErrorMessage);

        var recoveryCalls = checker.Calls
            .Where(call => call.Path == workspace.ProductionPath ||
                           call.Path == result.Marker!.SourceBackupPath ||
                           call.Path == result.Marker.StagingPath)
            .Skip(recoveryCallStart)
            .ToArray();
        Assert.All(recoveryCalls, call => Assert.Equal(SqliteInspectionMode.FullValidation, call.Mode));
    }

    private static SqliteDatabaseHealthChecker CreateHealthChecker() =>
        new(new FixedTimeProvider(), new TestLogger());

    private static SqliteRecoveryService CreateRecoveryService(
        TestWorkspace workspace,
        ISqliteDatabaseHealthChecker checker,
        Action<SqliteRecoveryPhase>? phaseObserver = null) =>
        new(
            checker,
            workspace.DataDirectory,
            new TestLogger(),
            new FixedTimeProvider(),
            phaseObserver);

    private static DatabaseStartupGate CreateGate(
        TestWorkspace workspace,
        ISqliteDatabaseHealthChecker checker) =>
        new(
            workspace.ProductionPath,
            workspace.BackupRoot,
            checker,
            new SqliteBackupService(checker, new TestLogger()),
            CreateRecoveryService(workspace, checker),
            acceptanceMode: false,
            new TestLogger());

    private static async Task<SqliteBackupResult> CreateBackupAsync(
        TestWorkspace workspace,
        ISqliteDatabaseHealthChecker checker)
    {
        var result = await new SqliteBackupService(checker, new TestLogger())
            .CreateValidatedBackupAsync(
                workspace.ProductionPath,
                workspace.BackupRoot,
                LocalNow,
                CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        return result;
    }

    private static Guid CreateSchemaFixture(string path, int version)
    {
        var passageId = Guid.NewGuid();
        if (version == 3)
        {
            using var store = new SqlitePassageRecordStore(path);
            store.Save(CreateRecord("RFID-560-01", 0x01, PassageOutcome.Completed));
            return store.Records.Single().PassageId;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SQLiteConnection($"Data Source={path};Version=3;");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = version == 1
            ? @"
CREATE TABLE passage_record
(
    passage_id TEXT NOT NULL PRIMARY KEY,
    station_address INTEGER NOT NULL,
    head_rfid INTEGER NULL,
    expected_vehicle_count INTEGER NOT NULL,
    detected_vehicle_count INTEGER NOT NULL,
    result INTEGER NOT NULL,
    started_at INTEGER NOT NULL,
    completed_at INTEGER NOT NULL,
    warning_message TEXT NULL,
    alarm_message TEXT NULL,
    clear_state INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    cleared_at INTEGER NULL
);
CREATE TABLE passage_rfid
(
    passage_id TEXT NOT NULL,
    sequence_no INTEGER NOT NULL,
    rfid_value INTEGER NOT NULL,
    first_seen_at INTEGER NOT NULL,
    batch_no INTEGER NOT NULL,
    PRIMARY KEY (passage_id, sequence_no)
);
PRAGMA user_version=1;"
            : @"
CREATE TABLE passage_record
(
    passage_id TEXT NOT NULL PRIMARY KEY,
    station_id TEXT NULL,
    station_address INTEGER NOT NULL,
    head_rfid INTEGER NULL,
    expected_vehicle_count INTEGER NOT NULL,
    detected_vehicle_count INTEGER NOT NULL,
    result INTEGER NOT NULL,
    started_at INTEGER NOT NULL,
    completed_at INTEGER NOT NULL,
    warning_message TEXT NULL,
    alarm_message TEXT NULL,
    clear_state INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    cleared_at INTEGER NULL
);
CREATE TABLE passage_rfid
(
    passage_id TEXT NOT NULL,
    sequence_no INTEGER NOT NULL,
    rfid_value INTEGER NOT NULL,
    first_seen_at INTEGER NOT NULL,
    batch_no INTEGER NOT NULL,
    PRIMARY KEY (passage_id, sequence_no)
);
PRAGMA user_version=2;";
        command.ExecuteNonQuery();

        using var insert = connection.CreateCommand();
        var completed = LocalNow.AddMinutes(-1);
        insert.CommandText = version == 1
            ? @"INSERT INTO passage_record
                (passage_id, station_address, head_rfid, expected_vehicle_count,
                 detected_vehicle_count, result, started_at, completed_at,
                 warning_message, alarm_message, clear_state, created_at, cleared_at)
                VALUES ($id, 1, 4097, 2, 2, 1, $started, $completed,
                        NULL, NULL, 0, $created, NULL);"
            : @"INSERT INTO passage_record
                (passage_id, station_id, station_address, head_rfid, expected_vehicle_count,
                 detected_vehicle_count, result, started_at, completed_at,
                 warning_message, alarm_message, clear_state, created_at, cleared_at)
                VALUES ($id, 'RFID-560-01', 1, 4097, 2, 2, 1, $started, $completed,
                        '脱节报警', '脱节报警', 1, $created, $completed);";
        insert.Parameters.AddWithValue("$id", passageId.ToString("D"));
        insert.Parameters.AddWithValue("$started", ToUnixMilliseconds(completed.AddSeconds(-10)));
        insert.Parameters.AddWithValue("$completed", ToUnixMilliseconds(completed));
        insert.Parameters.AddWithValue("$created", ToUnixMilliseconds(completed));
        insert.ExecuteNonQuery();

        using var detail = connection.CreateCommand();
        detail.CommandText = @"INSERT INTO passage_rfid
            (passage_id, sequence_no, rfid_value, first_seen_at, batch_no)
            VALUES ($id, 1, 4097, $first_seen, 1);";
        detail.Parameters.AddWithValue("$id", passageId.ToString("D"));
        detail.Parameters.AddWithValue("$first_seen", ToUnixMilliseconds(completed.AddSeconds(-10)));
        detail.ExecuteNonQuery();
        return passageId;
    }

    private static void CreateFutureSchemaDatabase(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SQLiteConnection($"Data Source={path};Version=3;");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version={SqlitePassageRecordStore.CurrentSchemaVersion + 1};";
        command.ExecuteNonQuery();
    }

    private static PassageRecord CreateRecord(
        string stationId,
        byte stationAddress,
        PassageOutcome outcome,
        PassageClearState clearState = PassageClearState.PendingClear,
        DateTimeOffset? clearedAt = null,
        DateTimeOffset? acknowledgedAt = null,
        DateTimeOffset? recoveredAt = null)
    {
        var completedAt = LocalNow.AddMinutes(-5 + stationAddress);
        var startedAt = completedAt.AddSeconds(-10);
        var observations = new[]
        {
            new PassageRfidObservation(1, 0x1001, startedAt, 1),
            new PassageRfidObservation(2, 0x1002, completedAt, 2)
        };
        return new PassageRecord(
            Guid.NewGuid(),
            stationId,
            stationAddress,
            0x1001,
            observations.Select(item => item.RfidValue),
            2,
            outcome,
            startedAt,
            completedAt,
            outcome == PassageOutcome.UncouplingAlarm ? new[] { "脱节报警" } : Array.Empty<string>(),
            outcome == PassageOutcome.UncouplingAlarm ? "脱节报警" : null,
            observations,
            clearState,
            clearedAt,
            completedAt,
            acknowledgedAt,
            recoveredAt);
    }

    private static void AssertRecord(
        PassageRecord actual,
        PassageRecord expected,
        DateTimeOffset? acknowledgedAtOverride = null)
    {
        Assert.Equal(expected.StationId, actual.StationId);
        Assert.Equal(expected.StationAddress, actual.StationAddress);
        Assert.Equal(expected.ClearState, actual.ClearState);
        Assert.Equal(expected.ClearedAt, actual.ClearedAt);
        Assert.Equal(acknowledgedAtOverride ?? expected.AlarmAcknowledgedAt, actual.AlarmAcknowledgedAt);
        Assert.Equal(expected.AlarmRecoveredAt, actual.AlarmRecoveredAt);
        Assert.Equal(expected.ObservedRfids, actual.ObservedRfids);
        Assert.Equal(expected.RfidObservations.Select(item => item.RfidValue), actual.RfidObservations.Select(item => item.RfidValue));
    }

    private static string CreateBlackBoxFile(TestWorkspace workspace, string yard, string content)
    {
        var directory = Path.Combine(workspace.BlackBoxRoot, "2026-09-20");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, yard + ".jsonl");
        File.WriteAllText(path, content);
        return path;
    }

    private static FileSnapshot SnapshotFile(string path)
    {
        using var sha256 = SHA256.Create();
        return new FileSnapshot(
            File.ReadAllBytes(path),
            new FileInfo(path).Length,
            File.GetLastWriteTimeUtc(path),
            BitConverter.ToString(sha256.ComputeHash(File.ReadAllBytes(path)))
                .Replace("-", string.Empty));
    }

    private static long ReadUserVersion(string path)
    {
        using var connection = new SQLiteConnection($"Data Source={path};Version=3;Read Only=True;");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static long ToUnixMilliseconds(DateTimeOffset value) =>
        value.ToUniversalTime().ToUnixTimeMilliseconds();

    private sealed class FileSnapshot : IEquatable<FileSnapshot>
    {
        public FileSnapshot(byte[] bytes, long length, DateTime lastWriteTimeUtc, string sha256)
        {
            Bytes = bytes;
            Length = length;
            LastWriteTimeUtc = lastWriteTimeUtc;
            Sha256 = sha256;
        }

        public byte[] Bytes { get; }

        public long Length { get; }

        public DateTime LastWriteTimeUtc { get; }

        public string Sha256 { get; }

        public bool Equals(FileSnapshot? other) =>
            other is not null &&
            Bytes.SequenceEqual(other.Bytes) &&
            Length == other.Length &&
            LastWriteTimeUtc == other.LastWriteTimeUtc &&
            string.Equals(Sha256, other.Sha256, StringComparison.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as FileSnapshot);

        public override int GetHashCode() =>
            Length.GetHashCode() ^ LastWriteTimeUtc.GetHashCode() ^ Sha256.GetHashCode();
    }

    private sealed class FixedTimeProvider : IRfidTimeProvider
    {
        public DateTimeOffset UtcNow => LocalNow.ToUniversalTime();

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class SimulatedCrashException : Exception
    {
        public SimulatedCrashException(string phase)
            : base($"simulated crash at {phase}")
        {
        }
    }

    private sealed class TestLogger : ILogger
    {
        public void Information(string message) { }

        public void Warning(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class RecordingHealthChecker : ISqliteDatabaseHealthChecker
    {
        private readonly Func<string, SqliteInspectionMode, SqliteDatabaseHealthState> _stateFactory;

        public RecordingHealthChecker(
            Func<string, SqliteInspectionMode, SqliteDatabaseHealthState> stateFactory)
        {
            _stateFactory = stateFactory;
        }

        public List<(string Path, SqliteInspectionMode Mode)> Calls { get; } = new();

        public SqliteDatabaseHealthResult Inspect(string databasePath, SqliteInspectionMode mode)
        {
            Calls.Add((databasePath, mode));
            var state = _stateFactory(databasePath, mode);
            var healthy = state == SqliteDatabaseHealthState.Healthy;
            return new SqliteDatabaseHealthResult(
                databasePath,
                state,
                LocalNow,
                healthy,
                healthy ? "ok" : state.ToString(),
                true,
                healthy,
                healthy ? "ok" : state.ToString(),
                healthy,
                healthy ? "ok" : state.ToString(),
                errorType: state == SqliteDatabaseHealthState.Unavailable ? "Unavailable" : null,
                errorMessage: state == SqliteDatabaseHealthState.Unavailable ? "unavailable" : null,
                schemaVersion: state == SqliteDatabaseHealthState.UnsupportedSchema
                    ? SqlitePassageRecordStore.CurrentSchemaVersion + 1
                    : SqlitePassageRecordStore.CurrentSchemaVersion);
        }
    }

    private sealed class RecordingBackupService : ISqliteBackupService
    {
        public int ScanCalls { get; private set; }

        public Task<SqliteBackupResult> CreateValidatedBackupAsync(
            string productionDatabasePath,
            string backupRootDirectory,
            DateTimeOffset localNow,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SqliteBackupResult(false, null, "not used"));

        public IReadOnlyList<SqliteBackupCandidate> ScanCandidates(string backupRootDirectory)
        {
            ScanCalls++;
            return Array.Empty<SqliteBackupCandidate>();
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string root)
        {
            Root = root;
            DataDirectory = Path.Combine(root, "Data");
            BackupRoot = Path.Combine(root, "Backups", "SQLite");
            BackupDateDirectory = Path.Combine(BackupRoot, "2026-09-20");
            ProductionPath = Path.Combine(DataDirectory, "MineRailMonitor.db");
            CorruptRoot = Path.Combine(DataDirectory, "Corrupt");
            StagingPath = Path.Combine(DataDirectory, "MineRailMonitor.restore.tmp.db");
            MarkerPath = Path.Combine(DataDirectory, ".sqlite-recovery-in-progress");
            BlackBoxRoot = Path.Combine(root, "Logs", "BlackBox");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(BackupDateDirectory);
        }

        public string Root { get; }

        public string DataDirectory { get; }

        public string BackupRoot { get; }

        public string BackupDateDirectory { get; }

        public string ProductionPath { get; }

        public string CorruptRoot { get; }

        public string StagingPath { get; }

        public string MarkerPath { get; }

        public string BlackBoxRoot { get; }

        public static TestWorkspace Create() => new(Path.Combine(
            Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N")));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
