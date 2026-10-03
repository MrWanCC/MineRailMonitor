using System.Data.SQLite;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Infrastructure.Persistence;

public sealed class SqlitePassageRecordStore : IPassageRecordStore, IStatisticsService, IDisposable
{
    public const int CurrentSchemaVersion = 6;
    private const string ConnectionPragmas =
        "PRAGMA journal_mode=WAL;" +
        "PRAGMA synchronous=FULL;" +
        "PRAGMA foreign_keys=ON;" +
        "PRAGMA busy_timeout=5000;";

    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqlitePassageRecordStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("SQLite数据库路径不能为空。", nameof(databasePath));
        }

        _databasePath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = $"Data Source={_databasePath};Version=3;";
        InitializeSchema();
    }

    public IReadOnlyList<PassageRecord> Records => Query(new PassageQuery { PageSize = int.MaxValue }).Items;

    public void Save(PassageRecord record)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT OR IGNORE INTO passage_record
(
    passage_id, station_id, station_address, head_rfid, vehicle_name, vehicle_head_rfid, vehicle_no,
    expected_vehicle_count, detected_vehicle_count, result,
    started_at, completed_at, warning_message, alarm_message,
    clear_state, created_at, cleared_at, alarm_acknowledged_at, alarm_recovered_at
)
VALUES
(
    $passage_id, $station_id, $station_address, $head_rfid, $vehicle_name, $vehicle_head_rfid, $vehicle_no,
    $expected_vehicle_count, $detected_vehicle_count, $result,
    $started_at, $completed_at, $warning_message, $alarm_message,
    $clear_state, $created_at, $cleared_at, $alarm_acknowledged_at, $alarm_recovered_at
);";
        AddRecordParameters(command, record);
        var inserted = command.ExecuteNonQuery();
        if (inserted > 0)
        {
            foreach (var observation in record.RfidObservations)
            {
                using var detailCommand = connection.CreateCommand();
                detailCommand.Transaction = transaction;
                detailCommand.CommandText = @"
INSERT INTO passage_rfid
(passage_id, sequence_no, rfid_value, first_seen_at, batch_no)
VALUES ($passage_id, $sequence_no, $rfid_value, $first_seen_at, $batch_no);";
                detailCommand.Parameters.AddWithValue("$passage_id", record.PassageId.ToString("D"));
                detailCommand.Parameters.AddWithValue("$sequence_no", observation.SequenceNo);
                detailCommand.Parameters.AddWithValue("$rfid_value", observation.RfidValue);
                detailCommand.Parameters.AddWithValue("$first_seen_at", ToUnixMilliseconds(observation.FirstSeenAt));
                detailCommand.Parameters.AddWithValue("$batch_no", observation.BatchNo);
                detailCommand.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    public void Add(PassageRecord record) => Save(record);

    public int MigrateStationIds(IReadOnlyDictionary<string, string> migration)
    {
        if (migration is null) throw new ArgumentNullException(nameof(migration));

        var changedRows = 0;
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var pair in migration)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value) ||
                string.Equals(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
UPDATE passage_record
SET station_id = $new_station_id
WHERE station_id = $old_station_id;";
            command.Parameters.AddWithValue("$new_station_id", pair.Value.Trim());
            command.Parameters.AddWithValue("$old_station_id", pair.Key.Trim());
            changedRows += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return changedRows;
    }

    public void MarkCleared(Guid passageId, DateTimeOffset clearedAt)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE passage_record
SET clear_state = $clear_state,
    cleared_at = $cleared_at,
    alarm_recovered_at = CASE
        WHEN result = $alarm_result AND alarm_acknowledged_at IS NOT NULL THEN $cleared_at
        ELSE alarm_recovered_at
    END
WHERE passage_id = $passage_id AND clear_state <> $clear_state;";
        command.Parameters.AddWithValue("$clear_state", (int)PassageClearState.Cleared);
        command.Parameters.AddWithValue("$cleared_at", ToUnixMilliseconds(clearedAt));
        command.Parameters.AddWithValue("$alarm_result", (int)PassageOutcome.UncouplingAlarm);
        command.Parameters.AddWithValue("$passage_id", passageId.ToString("D"));
        if (command.ExecuteNonQuery() > 0)
        {
            return;
        }

        using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = "SELECT COUNT(*) FROM passage_record WHERE passage_id = $passage_id;";
        existsCommand.Parameters.AddWithValue("$passage_id", passageId.ToString("D"));
        if (Convert.ToInt64(existsCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 0)
        {
            throw new InvalidOperationException($"PassageRecord not found: {passageId}");
        }
    }

    public void MarkAlarmAcknowledged(Guid passageId, DateTimeOffset acknowledgedAt)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE passage_record
SET alarm_acknowledged_at = $acknowledged_at,
    alarm_recovered_at = CASE
        WHEN clear_state = $cleared_state THEN COALESCE(alarm_recovered_at, cleared_at)
        ELSE alarm_recovered_at
    END
WHERE passage_id = $passage_id
  AND result = $alarm_result
  AND alarm_acknowledged_at IS NULL;";
        command.Parameters.AddWithValue("$acknowledged_at", ToUnixMilliseconds(acknowledgedAt));
        command.Parameters.AddWithValue("$cleared_state", (int)PassageClearState.Cleared);
        command.Parameters.AddWithValue("$passage_id", passageId.ToString("D"));
        command.Parameters.AddWithValue("$alarm_result", (int)PassageOutcome.UncouplingAlarm);
        if (command.ExecuteNonQuery() > 0)
        {
            return;
        }

        using var recordCommand = connection.CreateCommand();
        recordCommand.CommandText = @"
SELECT result, alarm_acknowledged_at
FROM passage_record
WHERE passage_id = $passage_id;";
        recordCommand.Parameters.AddWithValue("$passage_id", passageId.ToString("D"));
        using var reader = recordCommand.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"PassageRecord not found: {passageId}");
        }

        if ((PassageOutcome)reader.GetInt32(0) != PassageOutcome.UncouplingAlarm)
        {
            throw new InvalidOperationException("只有脱节报警记录需要人工确认。");
        }

        // Acknowledgement is idempotent and the first timestamp wins.
    }

    public IReadOnlyList<PassageRecord> GetUnacknowledgedAlarms()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT passage_id
FROM passage_record
WHERE result = $alarm_result AND alarm_acknowledged_at IS NULL
ORDER BY completed_at;";
        command.Parameters.AddWithValue("$alarm_result", (int)PassageOutcome.UncouplingAlarm);
        var ids = new List<Guid>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        return ids.Select(id => LoadRecord(connection, id)!).ToArray();
    }

    public IReadOnlyList<PassageRecord> GetPendingClear()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT passage_id FROM passage_record WHERE clear_state = $clear_state ORDER BY completed_at;";
        command.Parameters.AddWithValue("$clear_state", (int)PassageClearState.PendingClear);
        var ids = new List<Guid>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        return ids.Select(id => LoadRecord(connection, id)!).ToArray();
    }

    public PassageQueryResult Query(PassageQuery query)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        query.Validate();

        using var connection = OpenConnection();
        var filter = BuildFilter(query, out var parameters);
        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM passage_record WHERE {filter};";
        AddParameters(countCommand, parameters);
        var totalCount = Convert.ToInt32(countCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);

        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT passage_id
FROM passage_record
WHERE {filter}
ORDER BY completed_at DESC, created_at DESC
LIMIT $limit OFFSET $offset;";
        AddParameters(command, parameters);
        command.Parameters.AddWithValue("$limit", query.PageSize);
        command.Parameters.AddWithValue("$offset", checked(query.PageIndex * query.PageSize));
        var ids = new List<Guid>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        return new PassageQueryResult(ids.Select(id => LoadRecord(connection, id)!).ToArray(), totalCount, query.PageIndex, query.PageSize);
    }

    public PassageRecord? GetDetails(Guid passageId)
    {
        using var connection = OpenConnection();
        return LoadRecord(connection, passageId);
    }

    public PassageStatistics GetStatistics(DateTimeOffset localNow)
    {
        var dayStart = new DateTimeOffset(localNow.Date, localNow.Offset).ToUniversalTime();
        var dayEnd = dayStart.AddDays(1);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT station_id, result, COUNT(*)
FROM passage_record
WHERE completed_at >= $day_start AND completed_at < $day_end
GROUP BY station_id, result;";
        command.Parameters.AddWithValue("$day_start", ToUnixMilliseconds(dayStart));
        command.Parameters.AddWithValue("$day_end", ToUnixMilliseconds(dayEnd));

        var byStation = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var normal = 0;
        var alarm = 0;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var station = reader.IsDBNull(0) ? PassageRecord.LegacyStationId : NormalizeStationId(reader.GetString(0));
                var result = (PassageOutcome)reader.GetInt32(1);
                var count = reader.GetInt32(2);
                byStation[station] = byStation.TryGetValue(station, out var existing) ? existing + count : count;
                if (result == PassageOutcome.Completed) normal += count;
                if (result == PassageOutcome.UncouplingAlarm) alarm += count;
            }
        }

        return new PassageStatistics(normal + alarm, normal, alarm, byStation);
    }

    public OverviewStatistics GetOverview(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<string>? stationIds = null)
    {
        ValidateRange(start, end);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var stationFilter = AddStatisticsStationFilter(command, stationIds);
        command.CommandText = $@"
SELECT COUNT(*),
       COALESCE(SUM(CASE WHEN result = $normal_result THEN 1 ELSE 0 END), 0),
       COALESCE(SUM(CASE WHEN result = $alarm_result THEN 1 ELSE 0 END), 0),
       COALESCE(SUM(CASE WHEN result = $unverified_result THEN 1 ELSE 0 END), 0),
       COALESCE(AVG(CASE WHEN completed_at >= started_at THEN completed_at - started_at END), 0)
FROM passage_record
WHERE completed_at >= $start AND completed_at < $end
  {stationFilter};";
        AddTimeRangeParameters(command, start, end);
        command.Parameters.AddWithValue("$normal_result", (int)PassageOutcome.Completed);
        command.Parameters.AddWithValue("$alarm_result", (int)PassageOutcome.UncouplingAlarm);
        command.Parameters.AddWithValue("$unverified_result", (int)PassageOutcome.Unverified);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new OverviewStatistics(0, 0, 0);
        }

        return new OverviewStatistics(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetDouble(4) / 1000d,
            reader.GetInt32(3));
    }

    public IReadOnlyList<VehicleStatisticsItem> GetVehicleStatistics(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<string>? stationIds = null)
    {
        ValidateRange(start, end);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var stationFilter = AddStatisticsStationFilter(command, stationIds);
        command.CommandText = $@"
SELECT vehicle_no, MAX(vehicle_name), COUNT(*),
       SUM(CASE WHEN result = $normal_result THEN 1 ELSE 0 END),
       SUM(CASE WHEN result = $alarm_result THEN 1 ELSE 0 END)
FROM passage_record
WHERE completed_at >= $start AND completed_at < $end
  {stationFilter}
  AND result IN ($normal_result, $alarm_result)
GROUP BY vehicle_no
ORDER BY COUNT(*) DESC, vehicle_no COLLATE NOCASE;";
        AddTimeRangeParameters(command, start, end);
        command.Parameters.AddWithValue("$normal_result", (int)PassageOutcome.Completed);
        command.Parameters.AddWithValue("$alarm_result", (int)PassageOutcome.UncouplingAlarm);

        var result = new List<VehicleStatisticsItem>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                result.Add(new VehicleStatisticsItem(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4)));
            }
        }

        return result;
    }

    public IReadOnlyList<AlarmStatisticsItem> GetAlarmStatistics(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<string>? stationIds = null)
    {
        ValidateRange(start, end);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var stationFilter = AddStatisticsStationFilter(command, stationIds);
        const string alarmStatusExpression = @"CASE
            WHEN alarm_acknowledged_at IS NULL THEN $unconfirmed_status
            WHEN alarm_recovered_at IS NOT NULL THEN $recovered_status
            ELSE $acknowledged_status END";
        command.CommandText = $@"
SELECT {alarmStatusExpression}, COUNT(*)
FROM passage_record
WHERE completed_at >= $start AND completed_at < $end
  {stationFilter}
  AND result = $alarm_result
GROUP BY {alarmStatusExpression}
ORDER BY {alarmStatusExpression};";
        AddTimeRangeParameters(command, start, end);
        command.Parameters.AddWithValue("$alarm_result", (int)PassageOutcome.UncouplingAlarm);
        command.Parameters.AddWithValue("$unconfirmed_status", (int)AlarmStatus.Unconfirmed);
        command.Parameters.AddWithValue("$acknowledged_status", (int)AlarmStatus.Acknowledged);
        command.Parameters.AddWithValue("$recovered_status", (int)AlarmStatus.Recovered);

        var result = new List<AlarmStatisticsItem>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                result.Add(new AlarmStatisticsItem((AlarmStatus)reader.GetInt32(0), reader.GetInt32(1)));
            }
        }

        return result;
    }

    public void Dispose()
    {
    }

    private SQLiteConnection OpenConnection()
    {
        var connection = new SQLiteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = ConnectionPragmas;
        command.ExecuteNonQuery();
        return connection;
    }

    private void InitializeSchema()
    {
        using var connection = OpenConnection();
        var version = ReadSchemaVersion(connection);
        if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"SQLite数据库版本 {version} 高于当前支持版本 {CurrentSchemaVersion}。 ");
        }

        if (version == 0)
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS passage_record
(
    passage_id TEXT NOT NULL PRIMARY KEY,
    station_id TEXT NULL,
    station_address INTEGER NOT NULL,
    head_rfid INTEGER NULL,
    vehicle_name TEXT NULL,
    vehicle_head_rfid INTEGER NULL,
    vehicle_no TEXT NULL,
    expected_vehicle_count INTEGER NULL,
    detected_vehicle_count INTEGER NOT NULL,
    result INTEGER NOT NULL,
    started_at INTEGER NOT NULL,
    completed_at INTEGER NOT NULL,
    warning_message TEXT NULL,
    alarm_message TEXT NULL,
    clear_state INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    cleared_at INTEGER NULL,
    alarm_acknowledged_at INTEGER NULL,
    alarm_recovered_at INTEGER NULL
);

CREATE TABLE IF NOT EXISTS passage_rfid
(
    passage_id TEXT NOT NULL,
    sequence_no INTEGER NOT NULL,
    rfid_value INTEGER NOT NULL,
    first_seen_at INTEGER NOT NULL,
    batch_no INTEGER NOT NULL,
    PRIMARY KEY (passage_id, sequence_no),
    FOREIGN KEY (passage_id) REFERENCES passage_record(passage_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_passage_record_completed_at ON passage_record(completed_at);
CREATE INDEX IF NOT EXISTS ix_passage_record_station_completed ON passage_record(station_address, completed_at);
CREATE INDEX IF NOT EXISTS ix_passage_record_station_id_completed ON passage_record(station_id, completed_at);
CREATE INDEX IF NOT EXISTS ix_passage_record_head_completed ON passage_record(head_rfid, completed_at);
CREATE INDEX IF NOT EXISTS ix_passage_record_result_completed ON passage_record(result, completed_at);
CREATE INDEX IF NOT EXISTS ix_passage_rfid_passage_id ON passage_rfid(passage_id);
CREATE INDEX IF NOT EXISTS ix_passage_rfid_value ON passage_rfid(rfid_value);
PRAGMA user_version=6;";
            command.ExecuteNonQuery();
            transaction.Commit();
        }

        if (version == 1)
        {
            var hasStationIdColumn = HasColumn(connection, "passage_record", "station_id");
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = (hasStationIdColumn ? string.Empty : "ALTER TABLE passage_record ADD COLUMN station_id TEXT NULL;") + @"
CREATE INDEX IF NOT EXISTS ix_passage_record_station_id_completed ON passage_record(station_id, completed_at);
PRAGMA user_version=2;";
            command.ExecuteNonQuery();
            transaction.Commit();
            version = 2;
        }

        if (version == 2)
        {
            using var transaction = connection.BeginTransaction();
            var statements = new List<string>();
            if (!HasColumn(connection, "passage_record", "alarm_acknowledged_at"))
            {
                statements.Add("ALTER TABLE passage_record ADD COLUMN alarm_acknowledged_at INTEGER NULL;");
            }

            if (!HasColumn(connection, "passage_record", "alarm_recovered_at"))
            {
                statements.Add("ALTER TABLE passage_record ADD COLUMN alarm_recovered_at INTEGER NULL;");
            }

            statements.Add(@"
UPDATE passage_record
SET alarm_acknowledged_at = COALESCE(cleared_at, completed_at),
    alarm_recovered_at = COALESCE(cleared_at, completed_at)
WHERE result = $alarm_result AND clear_state = $cleared_state;");
            statements.Add("PRAGMA user_version=3;");

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = string.Join(Environment.NewLine, statements);
            command.Parameters.AddWithValue("$alarm_result", (int)PassageOutcome.UncouplingAlarm);
            command.Parameters.AddWithValue("$cleared_state", (int)PassageClearState.Cleared);
            command.ExecuteNonQuery();
            transaction.Commit();
            version = 3;
        }

        if (version == 3)
        {
            using var transaction = connection.BeginTransaction();
            var statements = new List<string>();
            if (!HasColumn(connection, "passage_record", "vehicle_name"))
            {
                statements.Add("ALTER TABLE passage_record ADD COLUMN vehicle_name TEXT NULL;");
            }

            if (!HasColumn(connection, "passage_record", "vehicle_head_rfid"))
            {
                statements.Add("ALTER TABLE passage_record ADD COLUMN vehicle_head_rfid INTEGER NULL;");
            }

            statements.Add("PRAGMA user_version=4;");
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = string.Join(Environment.NewLine, statements);
            command.ExecuteNonQuery();
            transaction.Commit();
            version = 4;
        }

        if (version == 4)
        {
            using var transaction = connection.BeginTransaction();
            var statements = new List<string>();
            if (!HasColumn(connection, "passage_record", "vehicle_no"))
            {
                statements.Add("ALTER TABLE passage_record ADD COLUMN vehicle_no TEXT NULL;");
            }

            statements.Add("PRAGMA user_version=5;");
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = string.Join(Environment.NewLine, statements);
            command.ExecuteNonQuery();
            transaction.Commit();
            version = 5;
        }

        if (version == 5)
        {
            using (var foreignKeysCommand = connection.CreateCommand())
            {
                foreignKeysCommand.CommandText = "PRAGMA foreign_keys=OFF;";
                foreignKeysCommand.ExecuteNonQuery();
            }

            using (var transaction = connection.BeginTransaction())
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
CREATE TABLE passage_record_v6
(
    passage_id TEXT NOT NULL PRIMARY KEY,
    station_id TEXT NULL,
    station_address INTEGER NOT NULL,
    head_rfid INTEGER NULL,
    vehicle_name TEXT NULL,
    vehicle_head_rfid INTEGER NULL,
    vehicle_no TEXT NULL,
    expected_vehicle_count INTEGER NULL,
    detected_vehicle_count INTEGER NOT NULL,
    result INTEGER NOT NULL,
    started_at INTEGER NOT NULL,
    completed_at INTEGER NOT NULL,
    warning_message TEXT NULL,
    alarm_message TEXT NULL,
    clear_state INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    cleared_at INTEGER NULL,
    alarm_acknowledged_at INTEGER NULL,
    alarm_recovered_at INTEGER NULL
);

INSERT INTO passage_record_v6
(
    passage_id, station_id, station_address, head_rfid, vehicle_name, vehicle_head_rfid, vehicle_no,
    expected_vehicle_count, detected_vehicle_count, result, started_at, completed_at,
    warning_message, alarm_message, clear_state, created_at, cleared_at,
    alarm_acknowledged_at, alarm_recovered_at
)
SELECT
    passage_id, station_id, station_address, head_rfid, vehicle_name, vehicle_head_rfid, vehicle_no,
    expected_vehicle_count, detected_vehicle_count, result, started_at, completed_at,
    warning_message, alarm_message, clear_state, created_at, cleared_at,
    alarm_acknowledged_at, alarm_recovered_at
FROM passage_record;

DROP TABLE passage_record;
ALTER TABLE passage_record_v6 RENAME TO passage_record;

CREATE INDEX IF NOT EXISTS ix_passage_record_completed_at ON passage_record(completed_at);
CREATE INDEX IF NOT EXISTS ix_passage_record_station_completed ON passage_record(station_address, completed_at);
CREATE INDEX IF NOT EXISTS ix_passage_record_station_id_completed ON passage_record(station_id, completed_at);
CREATE INDEX IF NOT EXISTS ix_passage_record_head_completed ON passage_record(head_rfid, completed_at);
CREATE INDEX IF NOT EXISTS ix_passage_record_result_completed ON passage_record(result, completed_at);
PRAGMA user_version=6;";
                command.ExecuteNonQuery();
                transaction.Commit();
            }

            using var enableForeignKeysCommand = connection.CreateCommand();
            enableForeignKeysCommand.CommandText = "PRAGMA foreign_keys=ON;";
            enableForeignKeysCommand.ExecuteNonQuery();
        }
    }

    private static int ReadSchemaVersion(SQLiteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ValidateRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "结束时间必须晚于开始时间。");
        }
    }

    private static void AddTimeRangeParameters(SQLiteCommand command, DateTimeOffset start, DateTimeOffset end)
    {
        command.Parameters.AddWithValue("$start", ToUnixMilliseconds(start));
        command.Parameters.AddWithValue("$end", ToUnixMilliseconds(end));
    }

    private static string AddStatisticsStationFilter(SQLiteCommand command, IEnumerable<string>? stationIds)
    {
        if (stationIds is null)
        {
            return string.Empty;
        }

        var normalizedStationIds = stationIds
            .Where(stationId => !string.IsNullOrWhiteSpace(stationId))
            .Select(stationId => NormalizeStationId(stationId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedStationIds.Length == 0)
        {
            return "AND 1 = 0";
        }

        var stationClauses = new List<string>();
        if (normalizedStationIds.Any(stationId => string.Equals(
                stationId,
                PassageRecord.LegacyStationId,
                StringComparison.OrdinalIgnoreCase)))
        {
            stationClauses.Add("(station_id IS NULL OR TRIM(station_id) = '')");
        }

        var configuredStationIds = normalizedStationIds
            .Where(stationId => !string.Equals(
                stationId,
                PassageRecord.LegacyStationId,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (configuredStationIds.Length > 0)
        {
            var parameterNames = new List<string>(configuredStationIds.Length);
            for (var index = 0; index < configuredStationIds.Length; index++)
            {
                var parameterName = $"$statistics_station_id_{index}";
                parameterNames.Add(parameterName);
                command.Parameters.AddWithValue(parameterName, configuredStationIds[index]);
            }

            stationClauses.Add($"station_id IN ({string.Join(", ", parameterNames)})");
        }

        return stationClauses.Count == 0
            ? string.Empty
            : $"AND ({string.Join(" OR ", stationClauses)})";
    }

    private static void AddRecordParameters(SQLiteCommand command, PassageRecord record)
    {
        command.Parameters.AddWithValue("$passage_id", record.PassageId.ToString("D"));
        command.Parameters.AddWithValue("$station_id", NormalizeStationId(record.StationId));
        command.Parameters.AddWithValue("$station_address", record.StationAddress);
        command.Parameters.AddWithValue("$head_rfid", record.HeadRfid.HasValue ? record.HeadRfid.Value : DBNull.Value);
        command.Parameters.AddWithValue("$vehicle_name", (object?)record.VehicleName ?? DBNull.Value);
        command.Parameters.AddWithValue("$vehicle_head_rfid", record.VehicleHeadRfid.HasValue ? record.VehicleHeadRfid.Value : DBNull.Value);
        command.Parameters.AddWithValue("$vehicle_no", (object?)record.VehicleNo ?? DBNull.Value);
        command.Parameters.AddWithValue("$expected_vehicle_count", (object?)record.ExpectedVehicleCount ?? DBNull.Value);
        command.Parameters.AddWithValue("$detected_vehicle_count", record.DetectedVehicleCount);
        command.Parameters.AddWithValue("$result", (int)record.Outcome);
        command.Parameters.AddWithValue("$started_at", ToUnixMilliseconds(record.StartedAt));
        command.Parameters.AddWithValue("$completed_at", ToUnixMilliseconds(record.CompletedAt));
        command.Parameters.AddWithValue("$warning_message", record.WarningMessages.Count == 0 ? DBNull.Value : string.Join("\n", record.WarningMessages));
        command.Parameters.AddWithValue("$alarm_message", (object?)record.AlarmMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$clear_state", (int)record.ClearState);
        command.Parameters.AddWithValue("$created_at", ToUnixMilliseconds(record.CreatedAt));
        command.Parameters.AddWithValue("$cleared_at", record.ClearedAt.HasValue ? ToUnixMilliseconds(record.ClearedAt.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$alarm_acknowledged_at", record.AlarmAcknowledgedAt.HasValue ? ToUnixMilliseconds(record.AlarmAcknowledgedAt.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$alarm_recovered_at", record.AlarmRecoveredAt.HasValue ? ToUnixMilliseconds(record.AlarmRecoveredAt.Value) : DBNull.Value);
    }

    private static string BuildFilter(PassageQuery query, out Dictionary<string, object?> parameters)
    {
        parameters = new Dictionary<string, object?>();
        var clauses = new List<string>();
        if (query.From.HasValue)
        {
            clauses.Add("completed_at >= $from");
            parameters["$from"] = ToUnixMilliseconds(query.From.Value);
        }
        if (query.To.HasValue)
        {
            clauses.Add("completed_at < $to");
            parameters["$to"] = ToUnixMilliseconds(query.To.Value);
        }
        if (query.StationIds is not null)
        {
            var stationIds = query.StationIds
                .Where(stationId => !string.IsNullOrWhiteSpace(stationId))
                .Select(NormalizeStationId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (stationIds.Length == 0)
            {
                clauses.Add("1 = 0");
            }
            else
            {
                var stationClauses = new List<string>();
                for (var index = 0; index < stationIds.Length; index++)
                {
                    var stationId = stationIds[index];
                    if (string.Equals(stationId, PassageRecord.LegacyStationId, StringComparison.OrdinalIgnoreCase))
                    {
                        stationClauses.Add("(station_id IS NULL OR TRIM(station_id) = '')");
                    }
                    else
                    {
                        var parameterName = $"$station_id_scope_{index}";
                        stationClauses.Add($"station_id = {parameterName}");
                        parameters[parameterName] = stationId;
                    }
                }

                clauses.Add($"({string.Join(" OR ", stationClauses)})");
            }
        }
        if (!string.IsNullOrWhiteSpace(query.StationId))
        {
            if (string.Equals(NormalizeStationId(query.StationId), PassageRecord.LegacyStationId, StringComparison.OrdinalIgnoreCase))
            {
                clauses.Add("(station_id IS NULL OR TRIM(station_id) = '')");
            }
            else
            {
                clauses.Add("station_id = $station_id_filter");
                parameters["$station_id_filter"] = query.StationId!.Trim();
            }
        }
        else if (query.StationAddress.HasValue)
        {
            clauses.Add("station_address = $station_address_filter");
            parameters["$station_address_filter"] = query.StationAddress.Value;
        }
        if (query.HeadRfid.HasValue)
        {
            clauses.Add("head_rfid = $head_rfid_filter");
            parameters["$head_rfid_filter"] = query.HeadRfid.Value;
        }
        if (query.Outcome.HasValue)
        {
            clauses.Add("result = $result_filter");
            parameters["$result_filter"] = (int)query.Outcome.Value;
        }
        if (query.IncludeWarnings)
        {
            clauses.Add("(result = $alert_result OR (result <> $unverified_result AND warning_message IS NOT NULL AND TRIM(warning_message) <> ''))");
            parameters["$alert_result"] = (int)PassageOutcome.UncouplingAlarm;
            parameters["$unverified_result"] = (int)PassageOutcome.Unverified;
        }
        return clauses.Count == 0 ? "1=1" : string.Join(" AND ", clauses);
    }

    private static void AddParameters(SQLiteCommand command, IReadOnlyDictionary<string, object?> parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
        }
    }

    private static PassageRecord? LoadRecord(SQLiteConnection connection, Guid passageId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT passage_id, station_id, station_address, head_rfid,
       expected_vehicle_count, result, started_at, completed_at,
       warning_message, alarm_message, clear_state, created_at, cleared_at,
       alarm_acknowledged_at, alarm_recovered_at, vehicle_name, vehicle_head_rfid, vehicle_no
FROM passage_record
WHERE passage_id = $passage_id;";
        command.Parameters.AddWithValue("$passage_id", passageId.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var recordId = Guid.Parse(reader.GetString(0));
        var details = LoadDetails(connection, recordId);
        return new PassageRecord(
            recordId,
            reader.IsDBNull(1) ? PassageRecord.LegacyStationId : NormalizeStationId(reader.GetString(1)),
            checked((byte)reader.GetInt32(2)),
            reader.IsDBNull(3) ? null : checked((ushort)reader.GetInt32(3)),
            details.Select(item => item.RfidValue),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            (PassageOutcome)reader.GetInt32(5),
            FromUnixMilliseconds(reader.GetInt64(6)),
            FromUnixMilliseconds(reader.GetInt64(7)),
            ReadMessages(reader.IsDBNull(8) ? null : reader.GetString(8)),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            details,
            (PassageClearState)reader.GetInt32(10),
            reader.IsDBNull(12) ? null : FromUnixMilliseconds(reader.GetInt64(12)),
            FromUnixMilliseconds(reader.GetInt64(11)),
            reader.IsDBNull(13) ? null : FromUnixMilliseconds(reader.GetInt64(13)),
            reader.IsDBNull(14) ? null : FromUnixMilliseconds(reader.GetInt64(14)),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.IsDBNull(16) ? null : checked((ushort)reader.GetInt32(16)),
            reader.IsDBNull(17) ? null : reader.GetString(17));
    }

    private static IReadOnlyList<PassageRfidObservation> LoadDetails(SQLiteConnection connection, Guid passageId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT sequence_no, rfid_value, first_seen_at, batch_no
FROM passage_rfid
WHERE passage_id = $passage_id
ORDER BY sequence_no;";
        command.Parameters.AddWithValue("$passage_id", passageId.ToString("D"));
        var details = new List<PassageRfidObservation>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            details.Add(new PassageRfidObservation(
                reader.GetInt32(0),
                checked((ushort)reader.GetInt32(1)),
                FromUnixMilliseconds(reader.GetInt64(2)),
                reader.GetInt32(3)));
        }

        return details;
    }

    private static IReadOnlyList<string> ReadMessages(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value!.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static long ToUnixMilliseconds(DateTimeOffset value) => value.ToUniversalTime().ToUnixTimeMilliseconds();

    private static DateTimeOffset FromUnixMilliseconds(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);

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

    private static string NormalizeStationId(string? stationId) =>
        string.IsNullOrWhiteSpace(stationId) ? PassageRecord.LegacyStationId : stationId!.Trim();
}
