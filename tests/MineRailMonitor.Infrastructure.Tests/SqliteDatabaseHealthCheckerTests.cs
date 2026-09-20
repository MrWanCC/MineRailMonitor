using System.Data.SQLite;
using System.Security.Cryptography;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Infrastructure.Logging;
using MineRailMonitor.Infrastructure.Persistence;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class SqliteDatabaseHealthCheckerTests
{
    [Fact]
    public void Missing_database_is_reported_as_missing()
    {
        using var directory = new TemporaryDirectory();

        var result = CreateChecker().Inspect(
            Path.Combine(directory.Path, "MineRailMonitor.db"),
            SqliteInspectionMode.StartupFast);

        Assert.Equal(SqliteDatabaseHealthState.Missing, result.State);
    }

    [Fact]
    public void Healthy_database_reports_quick_and_foreign_key_results()
    {
        using var database = new TemporaryDatabase();

        var result = CreateChecker().Inspect(database.Path, SqliteInspectionMode.StartupFast);

        Assert.Equal(SqliteDatabaseHealthState.Healthy, result.State);
        Assert.True(result.QuickCheckPassed);
        Assert.True(result.ForeignKeyCheckPassed);
        Assert.False(result.IntegrityCheckExecuted);
    }

    [Fact]
    public void Startup_fast_does_not_require_integrity_check_on_healthy_database()
    {
        using var database = new TemporaryDatabase();

        var result = CreateChecker().Inspect(database.Path, SqliteInspectionMode.StartupFast);

        Assert.Equal(SqliteDatabaseHealthState.Healthy, result.State);
        Assert.False(result.IntegrityCheckExecuted);
    }

    [Fact]
    public void Full_validation_always_runs_integrity_check()
    {
        using var database = new TemporaryDatabase();

        var result = CreateChecker().Inspect(database.Path, SqliteInspectionMode.FullValidation);

        Assert.Equal(SqliteDatabaseHealthState.Healthy, result.State);
        Assert.True(result.IntegrityCheckExecuted);
        Assert.True(result.IntegrityCheckPassed);
        Assert.True(result.ForeignKeyCheckPassed);
    }

    [Fact]
    public void Locked_database_is_unavailable_not_corrupt()
    {
        using var database = StandaloneDatabase.Create();
        using var exclusiveLock = database.OpenExclusiveTransaction();

        var result = CreateChecker().Inspect(database.Path, SqliteInspectionMode.StartupFast);

        Assert.Equal(SqliteDatabaseHealthState.Unavailable, result.State);
        Assert.NotEqual(SqliteDatabaseHealthState.Corrupt, result.State);
    }

    [Fact]
    public void Access_denied_is_unavailable()
    {
        using var database = new TemporaryDatabase();
        var checker = new SqliteDatabaseHealthChecker(
            new FixedRfidTimeProvider(),
            new TestLogger(),
            _ => throw new UnauthorizedAccessException("inspection denied"));

        var result = checker.Inspect(database.Path, SqliteInspectionMode.StartupFast);

        Assert.Equal(SqliteDatabaseHealthState.Unavailable, result.State);
        Assert.Equal(nameof(UnauthorizedAccessException), result.ErrorType);
    }

    [Fact]
    public void Corrupt_database_is_corrupt_after_integrity_confirmation()
    {
        using var database = StandaloneDatabase.CreateCorruptPageFile();

        var result = CreateChecker().Inspect(database.Path, SqliteInspectionMode.StartupFast);

        Assert.Equal(SqliteDatabaseHealthState.Corrupt, result.State);
        Assert.True(result.IntegrityCheckExecuted);
        Assert.False(result.IntegrityCheckPassed);
    }

    [Fact]
    public void Foreign_key_violation_is_corrupt()
    {
        using var database = StandaloneDatabase.CreateForeignKeyViolation();

        var result = CreateChecker().Inspect(database.Path, SqliteInspectionMode.StartupFast);

        Assert.Equal(SqliteDatabaseHealthState.Corrupt, result.State);
        Assert.False(result.ForeignKeyCheckPassed);
        Assert.True(result.IntegrityCheckExecuted);
    }

    [Fact]
    public void Inspection_reads_committed_wal_data()
    {
        using var database = new TemporaryDatabase();
        database.EnableWalAndCommitKnownRow();
        var before = database.SnapshotFilesWithoutSharedMemory();

        var result = CreateChecker().Inspect(database.Path, SqliteInspectionMode.StartupFast);

        Assert.Equal(SqliteDatabaseHealthState.Healthy, result.State);
        Assert.True(database.KnownRowIsReadable());
        Assert.Equal(before, database.SnapshotFilesWithoutSharedMemory());
    }

    [Fact]
    public void Inspection_does_not_create_sidecars_for_standalone_database()
    {
        using var database = StandaloneDatabase.Create();
        var before = database.SnapshotFiles();
        Assert.DoesNotContain(before.Keys, path => path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(before.Keys, path => path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase));

        var result = CreateChecker().Inspect(database.Path, SqliteInspectionMode.FullValidation);

        Assert.Equal(SqliteDatabaseHealthState.Healthy, result.State);
        var after = database.SnapshotFiles();
        Assert.Equal(before, after);
        Assert.DoesNotContain(after.Keys, path => path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(after.Keys, path => path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Inspection_does_not_mutate_inspected_file()
    {
        using var database = StandaloneDatabase.Create();
        var before = database.SnapshotFiles();

        var result = CreateChecker().Inspect(database.Path, SqliteInspectionMode.FullValidation);

        Assert.Equal(SqliteDatabaseHealthState.Healthy, result.State);
        Assert.Equal(before, database.SnapshotFiles());
    }

    private static SqliteDatabaseHealthChecker CreateChecker() =>
        new(new FixedRfidTimeProvider(), new TestLogger());

    private sealed class FixedRfidTimeProvider : IRfidTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 9, 18, 2, 0, 0, TimeSpan.Zero);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TestLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public void Information(string message) => Messages.Add(message);

        public void Warning(string message) => Messages.Add(message);

        public void Error(string message, Exception? exception = null) => Messages.Add(message);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MineRailMonitor",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => DeleteDirectory(Path);
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "MineRailMonitor",
            Guid.NewGuid().ToString("N"));
        private SQLiteConnection? _walConnection;

        public TemporaryDatabase()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "passages.db");
            Store = new SqlitePassageRecordStore(Path);
        }

        public string Path { get; }

        public SqlitePassageRecordStore Store { get; }

        public void EnableWalAndCommitKnownRow()
        {
            _walConnection = new SQLiteConnection($"Data Source={Path};Version=3;");
            _walConnection.Open();
            using var command = _walConnection.CreateCommand();
            command.CommandText = @"
PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS health_known_row (id INTEGER PRIMARY KEY, value TEXT NOT NULL);
BEGIN;
INSERT INTO health_known_row (value) VALUES ('committed-in-wal');
COMMIT;";
            command.ExecuteNonQuery();
        }

        public bool KnownRowIsReadable()
        {
            using var connection = new SQLiteConnection($"Data Source={Path};Version=3;Read Only=True;");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM health_known_row WHERE value = 'committed-in-wal';";
            return Convert.ToInt32(command.ExecuteScalar()) == 1;
        }

        public Dictionary<string, string> SnapshotFiles() => SnapshotDirectory(_directory);

        public Dictionary<string, string> SnapshotFilesWithoutSharedMemory() =>
            SnapshotFiles()
                .Where(pair => !pair.Key.EndsWith("-shm", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        public void Dispose()
        {
            _walConnection?.Dispose();
            Store.Dispose();
            DeleteDirectory(_directory);
        }
    }

    private sealed class StandaloneDatabase : IDisposable
    {
        private StandaloneDatabase(string directory, string path)
        {
            _directory = directory;
            Path = path;
        }

        private readonly string _directory;

        public string Path { get; }

        public static StandaloneDatabase Create()
        {
            var directory = CreateDirectory();
            var path = System.IO.Path.Combine(directory, "standalone.db");
            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE sample (id INTEGER PRIMARY KEY, value TEXT NOT NULL); INSERT INTO sample (value) VALUES ('healthy');";
            command.ExecuteNonQuery();
            return new StandaloneDatabase(directory, path);
        }

        public static StandaloneDatabase CreateForeignKeyViolation()
        {
            var directory = CreateDirectory();
            var path = System.IO.Path.Combine(directory, "foreign-key.db");
            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = @"
PRAGMA foreign_keys=OFF;
CREATE TABLE parent (id INTEGER PRIMARY KEY);
CREATE TABLE child (parent_id INTEGER NOT NULL REFERENCES parent(id));
INSERT INTO child (parent_id) VALUES (99);";
            command.ExecuteNonQuery();
            return new StandaloneDatabase(directory, path);
        }

        public static StandaloneDatabase CreateCorruptPageFile()
        {
            var directory = CreateDirectory();
            var path = System.IO.Path.Combine(directory, "corrupt.db");
            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE sample (id INTEGER PRIMARY KEY, value TEXT NOT NULL);
INSERT INTO sample (value) VALUES ('corruptible');
PRAGMA writable_schema=ON;
UPDATE sqlite_master SET rootpage=999999 WHERE name='sample';
PRAGMA writable_schema=OFF;";
            command.ExecuteNonQuery();
            return new StandaloneDatabase(directory, path);
        }

        public IDisposable OpenExclusiveTransaction()
        {
            var connection = Open(Path);
            using var command = connection.CreateCommand();
            command.CommandText = "BEGIN EXCLUSIVE;";
            command.ExecuteNonQuery();
            return new HeldConnection(connection);
        }

        public Dictionary<string, string> SnapshotFiles() => SnapshotDirectory(_directory);

        public void Dispose() => DeleteDirectory(_directory);

        private static string CreateDirectory()
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MineRailMonitor",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static SQLiteConnection Open(string path)
        {
            var connection = new SQLiteConnection($"Data Source={path};Version=3;");
            connection.Open();
            return connection;
        }

        private sealed class HeldConnection : IDisposable
        {
            private readonly SQLiteConnection _connection;

            public HeldConnection(SQLiteConnection connection) => _connection = connection;

            public void Dispose() => _connection.Dispose();
        }
    }

    private static Dictionary<string, string> SnapshotDirectory(string directory) =>
        Directory.EnumerateFiles(directory)
            .ToDictionary(
                System.IO.Path.GetFileName,
                file => $"{File.GetLastWriteTimeUtc(file).Ticks}:{ComputeHash(file)}",
                StringComparer.OrdinalIgnoreCase);

    private static string ComputeHash(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToBase64String(sha256.ComputeHash(stream));
    }

    private static void DeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            File.Delete(file);
        }

        Directory.Delete(directory);
    }
}
