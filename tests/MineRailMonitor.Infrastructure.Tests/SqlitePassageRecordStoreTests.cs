using System.Data.SQLite;
using System.Globalization;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Infrastructure.Persistence;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class SqlitePassageRecordStoreTests
{
    private static readonly DateTimeOffset Today = new(2026, 9, 9, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Constructor_creates_schema_v3_and_applies_connection_pragmas()
    {
        using var database = new TemporaryDatabase();

        using var connection = database.OpenConnection();
        Assert.Equal(3L, ExecuteScalar<long>(connection, "PRAGMA user_version;"));
        Assert.Equal(2L, ExecuteScalar<long>(connection, "PRAGMA synchronous;"));
        Assert.Equal(1L, ExecuteScalar<long>(connection, "PRAGMA foreign_keys;"));
        Assert.Equal(5000L, ExecuteScalar<long>(connection, "PRAGMA busy_timeout;"));
        Assert.Equal("wal", ExecuteScalar<string>(connection, "PRAGMA journal_mode;").ToLowerInvariant());
        Assert.Equal(1L, ExecuteScalar<long>(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='passage_record';"));
        Assert.Equal(1L, ExecuteScalar<long>(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='passage_rfid';"));
        Assert.True(HasColumn(connection, "passage_record", "alarm_acknowledged_at"));
        Assert.True(HasColumn(connection, "passage_record", "alarm_recovered_at"));
    }

    [Fact]
    public void Save_commits_main_record_and_all_rfid_details_as_one_transaction()
    {
        using var database = new TemporaryDatabase();
        var record = CreateRecord(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 0x01, Today);

        database.Store.Save(record);

        Assert.Single(database.Store.Records);
        var saved = database.Store.GetDetails(record.PassageId);
        Assert.NotNull(saved);
        Assert.Equal(PassageClearState.PendingClear, saved!.ClearState);
        Assert.Equal(new ushort[] { 0x0003, 0x001D }, saved.RfidObservations.Select(item => item.RfidValue));
        Assert.Equal(1, saved.RfidObservations[0].BatchNo);
        Assert.Equal(2, saved.RfidObservations[1].BatchNo);
    }

    [Fact]
    public void Save_rolls_back_main_row_when_a_rfid_detail_fails()
    {
        using var database = new TemporaryDatabase();
        var invalidDetails = new[]
        {
            new PassageRfidObservation(1, 0x0003, Today, 1),
            new PassageRfidObservation(1, 0x001D, Today, 1)
        };
        var record = new PassageRecord(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "RFID-01",
            0x01,
            0x0003,
            invalidDetails.Select(item => item.RfidValue),
            11,
            PassageOutcome.Completed,
            Today,
            Today,
            rfidObservations: invalidDetails);

        Assert.Throws<SQLiteException>(() => database.Store.Save(record));
        Assert.Empty(database.Store.Records);
        Assert.Null(database.Store.GetDetails(record.PassageId));
    }

    [Fact]
    public void Duplicate_passage_id_is_idempotent_without_duplicate_details()
    {
        using var database = new TemporaryDatabase();
        var record = CreateRecord(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 0x01, Today);

        database.Store.Save(record);
        database.Store.Save(record);

        Assert.Single(database.Store.Records);
        Assert.Equal(2, database.Store.GetDetails(record.PassageId)!.RfidObservations.Count);
    }

    [Fact]
    public void Mark_cleared_and_pending_query_are_persisted()
    {
        using var database = new TemporaryDatabase();
        var pending = CreateRecord(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), 0x01, Today);
        var cleared = CreateRecord(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), 0x02, Today);
        var alarm = CreateRecord(Guid.Parse("f0eeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), 0x03, Today, PassageOutcome.UncouplingAlarm);
        database.Store.Save(pending);
        database.Store.Save(cleared);
        database.Store.Save(alarm);
        var clearedAt = Today.AddMinutes(1);

        database.Store.MarkCleared(cleared.PassageId, clearedAt);
        database.Store.MarkCleared(alarm.PassageId, clearedAt);

        var pendingRows = database.Store.GetPendingClear();
        Assert.Equal(pending.PassageId, Assert.Single(pendingRows).PassageId);
        Assert.Equal(PassageClearState.Cleared, database.Store.GetDetails(cleared.PassageId)!.ClearState);
        Assert.Equal(clearedAt, database.Store.GetDetails(cleared.PassageId)!.ClearedAt);
        Assert.Equal(clearedAt, database.Store.GetDetails(alarm.PassageId)!.AlarmRecoveredAt);
    }

    [Fact]
    public void Save_round_trips_alarm_acknowledgement_and_recovery_timestamps()
    {
        using var database = new TemporaryDatabase();
        var acknowledgedAt = Today.AddMinutes(1);
        var recoveredAt = Today.AddMinutes(2);
        var alarm = CreateRecord(
            Guid.Parse("d1111111-1111-1111-1111-111111111111"),
            0x01,
            Today,
            PassageOutcome.UncouplingAlarm,
            acknowledgedAt,
            recoveredAt);

        database.Store.Save(alarm);

        var saved = database.Store.GetDetails(alarm.PassageId)!;
        Assert.Equal(acknowledgedAt, saved.AlarmAcknowledgedAt);
        Assert.Equal(recoveredAt, saved.AlarmRecoveredAt);
    }

    [Fact]
    public void Mark_alarm_acknowledged_is_first_write_wins_and_rejects_warnings()
    {
        using var database = new TemporaryDatabase();
        var alarm = CreateRecord(
            Guid.Parse("d2222222-2222-2222-2222-222222222222"),
            0x01,
            Today,
            PassageOutcome.UncouplingAlarm);
        var warning = CreateRecordWithStation(
            Guid.Parse("d3333333-3333-3333-3333-333333333333"),
            "RFID-02",
            0x02,
            Today,
            PassageOutcome.Completed,
            new[] { "识别不完整" });
        database.Store.Save(alarm);
        database.Store.Save(warning);

        var firstAcknowledgedAt = Today.AddMinutes(1);
        database.Store.MarkAlarmAcknowledged(alarm.PassageId, firstAcknowledgedAt);
        database.Store.MarkAlarmAcknowledged(alarm.PassageId, Today.AddMinutes(2));

        Assert.Equal(firstAcknowledgedAt, database.Store.GetDetails(alarm.PassageId)!.AlarmAcknowledgedAt);
        Assert.Throws<InvalidOperationException>(() =>
            database.Store.MarkAlarmAcknowledged(warning.PassageId, firstAcknowledgedAt));
    }

    [Fact]
    public void Get_unacknowledged_alarms_excludes_acknowledged_alarms_and_warnings()
    {
        using var database = new TemporaryDatabase();
        var pending = CreateRecord(
            Guid.Parse("d4444444-4444-4444-4444-444444444444"),
            0x01,
            Today,
            PassageOutcome.UncouplingAlarm);
        var acknowledged = CreateRecord(
            Guid.Parse("d5555555-5555-5555-5555-555555555555"),
            0x02,
            Today.AddMinutes(1),
            PassageOutcome.UncouplingAlarm);
        var warning = CreateRecordWithStation(
            Guid.Parse("d6666666-6666-6666-6666-666666666666"),
            "RFID-03",
            0x03,
            Today.AddMinutes(2),
            PassageOutcome.Completed,
            new[] { "识别不完整" });
        database.Store.Save(pending);
        database.Store.Save(acknowledged);
        database.Store.Save(warning);
        database.Store.MarkAlarmAcknowledged(acknowledged.PassageId, Today.AddMinutes(3));

        var result = database.Store.GetUnacknowledgedAlarms();

        Assert.Equal(pending.PassageId, Assert.Single(result).PassageId);
    }

    [Fact]
    public void V2_cleared_uncoupling_alarm_migrates_as_acknowledged_and_recovered()
    {
        using var legacy = new LegacyV2Database(
            PassageOutcome.UncouplingAlarm,
            PassageClearState.Cleared,
            Today);
        using var store = new SqlitePassageRecordStore(legacy.Path);

        var record = store.GetDetails(legacy.PassageId)!;

        using var migratedConnection = legacy.OpenConnection();
        Assert.Equal(3L, ExecuteScalar<long>(migratedConnection, "PRAGMA user_version;"));
        Assert.Equal(Today, record.AlarmAcknowledgedAt);
        Assert.Equal(Today, record.AlarmRecoveredAt);
        Assert.Empty(store.GetUnacknowledgedAlarms());
    }

    [Fact]
    public void V2_pending_clear_uncoupling_alarm_remains_unacknowledged_and_unrecovered()
    {
        using var legacy = new LegacyV2Database(
            PassageOutcome.UncouplingAlarm,
            PassageClearState.PendingClear,
            Today);
        using var store = new SqlitePassageRecordStore(legacy.Path);

        var record = Assert.Single(store.GetUnacknowledgedAlarms());

        Assert.Null(record.AlarmAcknowledgedAt);
        Assert.Null(record.AlarmRecoveredAt);
    }

    [Fact]
    public void Query_supports_filters_pagination_and_statistics()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Guid.Parse("f1111111-1111-1111-1111-111111111111"), 0x01, Today.AddMinutes(-3)));
        database.Store.Save(CreateRecord(Guid.Parse("f2222222-2222-2222-2222-222222222222"), 0x01, Today.AddMinutes(-2)));
        database.Store.Save(CreateRecord(Guid.Parse("f3333333-3333-3333-3333-333333333333"), 0x02, Today.AddMinutes(-1), PassageOutcome.UncouplingAlarm));

        var query = database.Store.Query(new PassageQuery
        {
            From = Today.AddHours(-1),
            To = Today.AddHours(1),
            StationAddress = 0x01,
            Outcome = PassageOutcome.Completed,
            PageIndex = 1,
            PageSize = 1
        });
        var statistics = database.Store.GetStatistics(Today);

        Assert.Equal(2, query.TotalCount);
        Assert.Equal(Guid.Parse("f1111111-1111-1111-1111-111111111111"), Assert.Single(query.Items).PassageId);
        Assert.Equal(3, statistics.TodayPassageCount);
        Assert.Equal(2, statistics.TodayNormalCount);
        Assert.Equal(1, statistics.TodayAlarmCount);
        Assert.Equal(2, statistics.ByStation["RFID-01"]);
    }

    [Fact]
    public void Query_filters_multiple_station_ids_without_mixing_yards()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecordWithStation(
            Guid.Parse("f4444444-4444-4444-4444-444444444444"),
            "RFID-01",
            0x01,
            Today.AddMinutes(-3)));
        database.Store.Save(CreateRecordWithStation(
            Guid.Parse("f5555555-5555-5555-5555-555555555555"),
            "RFID-02",
            0x02,
            Today.AddMinutes(-2)));
        database.Store.Save(CreateRecordWithStation(
            Guid.Parse("f6666666-6666-6666-6666-666666666666"),
            "RFID-03",
            0x03,
            Today.AddMinutes(-1)));

        var result = database.Store.Query(new PassageQuery
        {
            StationIds = new[] { "RFID-01", "RFID-03" },
            PageIndex = 0,
            PageSize = 20
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new[] { "RFID-03", "RFID-01" }, result.Items.Select(record => record.StationId));
    }

    [Fact]
    public void Save_persists_distinct_station_ids_when_protocol_addresses_repeat()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecordWithStation(
            Guid.Parse("a1111111-1111-1111-1111-111111111111"),
            "RFID-01",
            0x01,
            Today.AddMinutes(-2)));
        database.Store.Save(CreateRecordWithStation(
            Guid.Parse("a2222222-2222-2222-2222-222222222222"),
            "RFID-02",
            0x01,
            Today.AddMinutes(-1)));

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT station_id FROM passage_record ORDER BY completed_at;";
        using var reader = command.ExecuteReader();
        var stationIds = new List<string>();
        while (reader.Read())
        {
            stationIds.Add(reader.GetString(0));
        }

        var statistics = database.Store.GetStatistics(Today);

        Assert.Equal(new[] { "RFID-01", "RFID-02" }, stationIds);
        Assert.Equal(1, statistics.ByStation["RFID-01"]);
        Assert.Equal(1, statistics.ByStation["RFID-02"]);
    }

    [Fact]
    public void MigrateStationIds_preserves_historical_records_under_new_scoped_ids()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecordWithStation(
            Guid.Parse("a4444444-4444-4444-4444-444444444444"),
            "RFID-01",
            0x01,
            Today));

        var migrated = database.Store.MigrateStationIds(new Dictionary<string, string>
        {
            ["RFID-01"] = "RFID-560-01"
        });

        Assert.Equal(1, migrated);
        Assert.Empty(database.Store.Query(new PassageQuery
        {
            StationIds = new[] { "RFID-01" },
            PageSize = 20
        }).Items);
        Assert.Equal("RFID-560-01", Assert.Single(database.Store.Query(new PassageQuery
        {
            StationIds = new[] { "RFID-560-01" },
            PageSize = 20
        }).Items).StationId);
    }

    [Fact]
    public void Version_one_database_migrates_without_guessing_legacy_station_identity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "passages.db");
        var passageId = Guid.Parse("a3333333-3333-3333-3333-333333333333");
        Directory.CreateDirectory(directory);

        try
        {
            using (var connection = new SQLiteConnection($"Data Source={path};Version=3;"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"
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
INSERT INTO passage_record
(passage_id, station_address, head_rfid, expected_vehicle_count, detected_vehicle_count, result,
 started_at, completed_at, warning_message, alarm_message, clear_state, created_at, cleared_at)
VALUES
('a3333333-3333-3333-3333-333333333333', 1, 3, 11, 1, 0, 0, 1000, NULL, NULL, 1, 1000, NULL);
PRAGMA user_version=1;";
                command.ExecuteNonQuery();
            }

            using var store = new SqlitePassageRecordStore(path);
            var record = Assert.Single(store.Query(new PassageQuery { PageSize = 20 }).Items);
            using var migratedConnection = new SQLiteConnection($"Data Source={path};Version=3;");
            migratedConnection.Open();
            using var versionCommand = migratedConnection.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version;";

            Assert.Equal(3L, Convert.ToInt64(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture));
            Assert.Equal(PassageRecord.LegacyStationId, record.StationId);
            var unixEpoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
            Assert.Equal(1, store.GetStatistics(unixEpoch.AddMilliseconds(1000)).ByStation[PassageRecord.LegacyStationId]);
        }
        finally
        {
            foreach (var file in Directory.Exists(directory) ? Directory.EnumerateFiles(directory) : Array.Empty<string>())
            {
                File.Delete(file);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    [Fact]
    public void Alarm_query_returns_only_uncoupling_alarm_records_and_keeps_station_identity()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecordWithStation(
            Guid.Parse("a4444444-4444-4444-4444-444444444444"),
            "RFID-01",
            0x01,
            Today.AddMinutes(-3),
            PassageOutcome.Completed));
        database.Store.Save(CreateRecordWithStation(
            Guid.Parse("a5555555-5555-5555-5555-555555555555"),
            "RFID-02",
            0x01,
            Today.AddMinutes(-2),
            PassageOutcome.UncouplingAlarm));
        database.Store.Save(CreateRecordWithStation(
            Guid.Parse("a6666666-6666-6666-6666-666666666666"),
            "RFID-01",
            0x01,
            Today.AddMinutes(-1),
            PassageOutcome.UncouplingAlarm));

        var result = database.Store.Query(new PassageQuery
        {
            StationId = "RFID-02",
            Outcome = PassageOutcome.UncouplingAlarm,
            PageIndex = 0,
            PageSize = 20
        });

        var alarm = Assert.Single(result.Items);
        Assert.Equal("RFID-02", alarm.StationId);
        Assert.Equal(PassageOutcome.UncouplingAlarm, alarm.Outcome);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public void Alert_query_includes_persisted_warning_records_and_uncoupling_alarms()
    {
        using var database = new TemporaryDatabase();
        var normal = CreateRecordWithStation(
            Guid.Parse("a7777777-7777-7777-7777-777777777777"),
            "RFID-01",
            0x01,
            Today.AddMinutes(-3));
        var warning = CreateRecordWithStation(
            Guid.Parse("a8888888-8888-8888-8888-888888888888"),
            "RFID-01",
            0x01,
            Today.AddMinutes(-2),
            warningMessages: new[] { "识别不完整：已识别10/11，缺少1个RFID" });
        var alarm = CreateRecordWithStation(
            Guid.Parse("a9999999-9999-9999-9999-999999999999"),
            "RFID-01",
            0x01,
            Today.AddMinutes(-1),
            PassageOutcome.UncouplingAlarm);

        database.Store.Save(normal);
        database.Store.Save(warning);
        database.Store.Save(alarm);

        var result = database.Store.Query(new PassageQuery { IncludeWarnings = true, PageSize = 20 });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new[] { alarm.PassageId, warning.PassageId }, result.Items.Select(record => record.PassageId));
    }

    private static PassageRecord CreateRecord(
        Guid passageId,
        byte stationAddress,
        DateTimeOffset completedAt,
        PassageOutcome outcome = PassageOutcome.Completed,
        DateTimeOffset? alarmAcknowledgedAt = null,
        DateTimeOffset? alarmRecoveredAt = null)
    {
        var startedAt = completedAt.AddSeconds(-10);
        var details = new[]
        {
            new PassageRfidObservation(1, 0x0003, startedAt, 1),
            new PassageRfidObservation(2, 0x001D, completedAt, 2)
        };
        return new PassageRecord(
            passageId,
            $"RFID-{stationAddress:X2}",
            stationAddress,
            0x0003,
            details.Select(item => item.RfidValue),
            11,
            outcome,
            startedAt,
            completedAt,
            outcome == PassageOutcome.Completed ? Array.Empty<string>() : new[] { "脱节报警" },
            outcome == PassageOutcome.Completed ? null : "脱节报警",
            details,
            alarmAcknowledgedAt: alarmAcknowledgedAt,
            alarmRecoveredAt: alarmRecoveredAt);
    }

    private static PassageRecord CreateRecordWithStation(
        Guid passageId,
        string stationId,
        byte stationAddress,
        DateTimeOffset completedAt,
        PassageOutcome outcome = PassageOutcome.Completed,
        IEnumerable<string>? warningMessages = null)
    {
        var startedAt = completedAt.AddSeconds(-10);
        var details = new[]
        {
            new PassageRfidObservation(1, 0x0003, startedAt, 1)
        };
        return new PassageRecord(
            passageId,
            stationId,
            stationAddress,
            0x0003,
            details.Select(item => item.RfidValue),
            11,
            outcome,
            startedAt,
            completedAt,
            warningMessages: warningMessages ?? (outcome == PassageOutcome.Completed ? Array.Empty<string>() : new[] { "脱节报警" }),
            alarmMessage: outcome == PassageOutcome.Completed ? null : "脱节报警",
            rfidObservations: details);
    }

    private static T ExecuteScalar<T>(SQLiteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T));
    }

    private static bool HasColumn(SQLiteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));

        public TemporaryDatabase()
        {
            Directory.CreateDirectory(_directory);
            Store = new SqlitePassageRecordStore(Path.Combine(_directory, "passages.db"));
        }

        public SqlitePassageRecordStore Store { get; }

        public SQLiteConnection OpenConnection()
        {
            var connection = new SQLiteConnection($"Data Source={Path.Combine(_directory, "passages.db")};Version=3;");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            command.ExecuteNonQuery();
            return connection;
        }

        public void Dispose()
        {
            Store.Dispose();
            foreach (var file in Directory.EnumerateFiles(_directory))
            {
                File.Delete(file);
            }
            Directory.Delete(_directory);
        }
    }

    private sealed class LegacyV2Database : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));

        public LegacyV2Database(PassageOutcome outcome, PassageClearState clearState, DateTimeOffset completedAt)
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "passages.db");
            PassageId = Guid.NewGuid();

            using var connection = new SQLiteConnection($"Data Source={Path};Version=3;");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
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
            insert.CommandText = @"
INSERT INTO passage_record
(passage_id, station_id, station_address, head_rfid, expected_vehicle_count, detected_vehicle_count,
 result, started_at, completed_at, warning_message, alarm_message, clear_state, created_at, cleared_at)
VALUES ($id, $station_id, $address, $head_rfid, 11, 1, $result, $started, $completed,
        $warning, $alarm, $clear_state, $created, $cleared);";
            insert.Parameters.AddWithValue("$id", PassageId.ToString("D"));
            insert.Parameters.AddWithValue("$station_id", "RFID-560-01");
            insert.Parameters.AddWithValue("$address", 0x01);
            insert.Parameters.AddWithValue("$head_rfid", 0x0003);
            insert.Parameters.AddWithValue("$result", (int)outcome);
            insert.Parameters.AddWithValue("$started", ToUnixMilliseconds(completedAt.AddSeconds(-10)));
            insert.Parameters.AddWithValue("$completed", ToUnixMilliseconds(completedAt));
            insert.Parameters.AddWithValue("$warning", outcome == PassageOutcome.Completed ? DBNull.Value : "脱节报警");
            insert.Parameters.AddWithValue("$alarm", outcome == PassageOutcome.Completed ? DBNull.Value : "脱节报警");
            insert.Parameters.AddWithValue("$clear_state", (int)clearState);
            insert.Parameters.AddWithValue("$created", ToUnixMilliseconds(completedAt));
            insert.Parameters.AddWithValue("$cleared", clearState == PassageClearState.Cleared ? ToUnixMilliseconds(completedAt) : DBNull.Value);
            insert.ExecuteNonQuery();
        }

        public string Path { get; }

        public Guid PassageId { get; }

        public SQLiteConnection OpenConnection()
        {
            var connection = new SQLiteConnection($"Data Source={Path};Version=3;");
            connection.Open();
            return connection;
        }

        public void Dispose()
        {
            foreach (var file in Directory.Exists(_directory) ? Directory.EnumerateFiles(_directory) : Array.Empty<string>())
            {
                File.Delete(file);
            }

            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory);
            }
        }
    }

    private static long ToUnixMilliseconds(DateTimeOffset value) => value.ToUniversalTime().ToUnixTimeMilliseconds();
}
