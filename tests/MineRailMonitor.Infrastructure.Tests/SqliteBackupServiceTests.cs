using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Infrastructure.Logging;
using MineRailMonitor.Infrastructure.Persistence;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class SqliteBackupServiceTests
{
    [Fact]
    public async Task CreateValidatedBackup_preserves_committed_wal_rows()
    {
        using var database = WalDatabase.Create();
        using var directories = new TemporaryDirectory();
        var before = database.SnapshotProductionFiles();
        var service = CreateService(new SqliteDatabaseHealthChecker(new FixedRfidTimeProvider(), new TestLogger()));

        var result = await service.CreateValidatedBackupAsync(
            database.Path,
            directories.BackupRoot,
            LocalTime,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(before, database.SnapshotProductionFiles());
        Assert.False(File.Exists(result.FinalPath! + "-wal"));
        Assert.False(File.Exists(result.FinalPath! + "-shm"));
        Assert.True(ContainsKnownRow(result.FinalPath!));
    }

    [Fact]
    public async Task Formal_backup_promotion_requires_full_validation()
    {
        using var database = StandaloneDatabase.Create();
        using var directories = new TemporaryDirectory();
        var healthChecker = new RecordingHealthChecker(SqliteDatabaseHealthState.Healthy);
        var service = CreateService(healthChecker);

        var result = await service.CreateValidatedBackupAsync(
            database.Path,
            directories.BackupRoot,
            LocalTime,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Single(healthChecker.Calls);
        Assert.Equal(SqliteInspectionMode.FullValidation, healthChecker.Calls[0].Mode);
        Assert.True(File.Exists(result.FinalPath));
    }

    [Fact]
    public async Task CreateValidatedBackup_validation_failure_does_not_promote_tmp()
    {
        using var database = StandaloneDatabase.Create();
        using var directories = new TemporaryDirectory();
        var oldFormal = directories.CreateFormalBackup("20260917_020000");
        var service = CreateService(new RecordingHealthChecker(SqliteDatabaseHealthState.Corrupt));

        var result = await service.CreateValidatedBackupAsync(
            database.Path,
            directories.BackupRoot,
            LocalTime,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.FinalPath);
        Assert.True(File.Exists(oldFormal));
        Assert.False(File.Exists(Path.Combine(
            directories.BackupDirectory,
            "MineRailMonitor_20260918_020000.db")));
        Assert.Empty(Directory.GetFiles(directories.BackupDirectory, "*.tmp.db"));
    }

    [Fact]
    public async Task Online_backup_failure_does_not_delete_existing_formal_backup()
    {
        using var directories = new TemporaryDirectory();
        var oldFormal = directories.CreateFormalBackup("20260917_020000");
        var service = CreateService(new RecordingHealthChecker(SqliteDatabaseHealthState.Healthy));

        var result = await service.CreateValidatedBackupAsync(
            Path.Combine(directories.Path, "missing.db"),
            directories.BackupRoot,
            LocalTime,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(oldFormal));
    }

    [Fact]
    public async Task Cancellation_before_backup_does_not_publish_backup()
    {
        using var database = StandaloneDatabase.Create();
        using var directories = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = CreateService(new RecordingHealthChecker(SqliteDatabaseHealthState.Healthy));

        var result = await service.CreateValidatedBackupAsync(
            database.Path,
            directories.BackupRoot,
            LocalTime,
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Null(result.FinalPath);
        Assert.False(Directory.Exists(directories.BackupDirectory));
    }

    [Fact]
    public void ScanCandidates_orders_by_filename_timestamp_and_ignores_malformed_names()
    {
        using var directories = new TemporaryDirectory();
        directories.CreateFormalBackup("20260917_230000", "2026-09-17");
        var newest = directories.CreateFormalBackup("20260918_020000");
        directories.CreateFile("20260918_030000.tmp.db");
        directories.CreateFile("copy-of-MineRailMonitor.db");
        directories.CreateFile("MineRailMonitor.db");
        directories.CreateFile("MineRailMonitor_20260918_020000.txt");
        directories.CreateFile("MineRailMonitor_20260918_020000.db.bak");
        directories.CreateFile("MineRailMonitor_20260918_030000_extra.db");
        File.SetLastWriteTimeUtc(newest, DateTime.UtcNow.AddDays(-2));

        var service = CreateService(new RecordingHealthChecker(SqliteDatabaseHealthState.Healthy));
        var candidates = service.ScanCandidates(directories.BackupRoot);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("MineRailMonitor_20260918_020000.db", Path.GetFileName(candidates[0].Path));
        Assert.Equal("MineRailMonitor_20260917_230000.db", Path.GetFileName(candidates[1].Path));
    }

    [Fact]
    public void ScanCandidates_rejects_filename_date_that_does_not_match_parent_directory()
    {
        using var directories = new TemporaryDirectory();
        directories.CreateFile("MineRailMonitor_20260917_020000.db", "2026-09-18");

        var candidates = CreateService(new RecordingHealthChecker(SqliteDatabaseHealthState.Healthy))
            .ScanCandidates(directories.BackupRoot);

        Assert.Empty(candidates);
    }

    [Fact]
    public void ScanCandidates_ignores_tmp_database()
    {
        using var directories = new TemporaryDirectory();
        directories.CreateFile("MineRailMonitor_20260918_020000.tmp.db");

        var candidates = CreateService(new RecordingHealthChecker(SqliteDatabaseHealthState.Healthy))
            .ScanCandidates(directories.BackupRoot);

        Assert.Empty(candidates);
    }

    [Fact]
    public void ScanCandidates_does_not_use_last_write_time_for_ordering()
    {
        using var directories = new TemporaryDirectory();
        var older = directories.CreateFormalBackup("20260918_010000");
        var newer = directories.CreateFormalBackup("20260918_020000");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(3));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddDays(-3));

        var candidates = CreateService(new RecordingHealthChecker(SqliteDatabaseHealthState.Healthy))
            .ScanCandidates(directories.BackupRoot);

        Assert.Equal("MineRailMonitor_20260918_020000.db", Path.GetFileName(candidates[0].Path));
        Assert.Equal("MineRailMonitor_20260918_010000.db", Path.GetFileName(candidates[1].Path));
    }

    private static readonly DateTimeOffset LocalTime =
        new(2026, 9, 18, 2, 0, 0, TimeSpan.FromHours(8));

    private static SqliteBackupService CreateService(ISqliteDatabaseHealthChecker healthChecker) =>
        new(healthChecker, new TestLogger());

    private static bool ContainsKnownRow(string databasePath)
    {
        using var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;Read Only=True;");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM known_row WHERE value = 'committed-in-wal';";
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private sealed class FixedRfidTimeProvider : IRfidTimeProvider
    {
        public DateTimeOffset UtcNow => LocalTime.ToUniversalTime();

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TestLogger : ILogger
    {
        public void Information(string message) { }

        public void Warning(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class RecordingHealthChecker : ISqliteDatabaseHealthChecker
    {
        private readonly SqliteDatabaseHealthState _state;

        public RecordingHealthChecker(SqliteDatabaseHealthState state) => _state = state;

        public List<(string Path, SqliteInspectionMode Mode)> Calls { get; } = new();

        public SqliteDatabaseHealthResult Inspect(string databasePath, SqliteInspectionMode mode)
        {
            Calls.Add((databasePath, mode));
            return new SqliteDatabaseHealthResult(
                databasePath,
                _state,
                LocalTime,
                _state == SqliteDatabaseHealthState.Healthy,
                _state == SqliteDatabaseHealthState.Healthy ? "ok" : "failed",
                true,
                _state == SqliteDatabaseHealthState.Healthy,
                _state == SqliteDatabaseHealthState.Healthy ? "ok" : "failed",
                _state == SqliteDatabaseHealthState.Healthy,
                _state == SqliteDatabaseHealthState.Healthy ? "ok" : "failed");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MineRailMonitor",
                Guid.NewGuid().ToString("N"));
            BackupRoot = System.IO.Path.Combine(Path, "Backups", "SQLite");
            BackupDirectory = System.IO.Path.Combine(BackupRoot, "2026-09-18");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string BackupRoot { get; }

        public string BackupDirectory { get; }

        public string CreateFormalBackup(string timestamp, string directoryName = "2026-09-18") =>
            CreateFile($"MineRailMonitor_{timestamp}.db", directoryName);

        public string CreateFile(string fileName, string directoryName = "2026-09-18")
        {
            var directory = System.IO.Path.Combine(BackupRoot, directoryName);
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, fileName);
            File.WriteAllText(path, "backup");
            return path;
        }

        public void Dispose() => DeleteDirectory(Path);
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
    }

    private sealed class WalDatabase : IDisposable
    {
        private WalDatabase(string directory, string path, SQLiteConnection writer)
        {
            _directory = directory;
            Path = path;
            _writer = writer;
        }

        private readonly string _directory;
        private readonly SQLiteConnection _writer;

        public string Path { get; }

        public static WalDatabase Create()
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MineRailMonitor",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "production.db");
            var writer = new SQLiteConnection($"Data Source={path};Version=3;");
            writer.Open();
            using var command = writer.CreateCommand();
            command.CommandText = @"
PRAGMA journal_mode=WAL;
CREATE TABLE known_row (id INTEGER PRIMARY KEY, value TEXT NOT NULL);
PRAGMA wal_checkpoint(TRUNCATE);
BEGIN;
INSERT INTO known_row (value) VALUES ('committed-in-wal');
COMMIT;";
            command.ExecuteNonQuery();
            return new WalDatabase(directory, path, writer);
        }

        public Dictionary<string, string> SnapshotProductionFiles() =>
            new[] { Path, Path + "-wal" }
                .Where(File.Exists)
                .ToDictionary(
                    System.IO.Path.GetFileName,
                    file => $"{File.GetLastWriteTimeUtc(file).Ticks}:{ComputeHash(file)}",
                    StringComparer.OrdinalIgnoreCase);

        public void Dispose()
        {
            _writer.Dispose();
            DeleteDirectory(_directory);
        }
    }

    private static string ComputeHash(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            DeleteDirectory(child);
        }

        Directory.Delete(directory);
    }
}
