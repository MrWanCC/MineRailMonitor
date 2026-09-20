using MineRailMonitor.Infrastructure.Logging;
using MineRailMonitor.Infrastructure.Persistence;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class SqliteRetentionServiceTests
{
    private static readonly DateTimeOffset LocalNow =
        new(2026, 9, 20, 2, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Retention_keeps_today_and_previous_13_local_calendar_dates()
    {
        using var directory = new TemporaryDirectory();
        var dates = new[] { LocalNow.Date, LocalNow.Date.AddDays(-1), LocalNow.Date.AddDays(-13) };
        var paths = dates.Select(date => directory.CreateFormal(date, "020000")).ToArray();

        new SqliteRetentionService(14, new TestLogger()).Apply(directory.BackupRoot, LocalNow);

        Assert.All(paths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void Retention_deletes_only_formal_backups_older_than_window()
    {
        using var directory = new TemporaryDirectory();
        var expiredFormal = directory.CreateFormal(LocalNow.Date.AddDays(-14), "020000");
        var expiredWrongDate = directory.CreateFile(
            "MineRailMonitor_20260920_020000.db",
            LocalNow.Date.AddDays(-14));
        var malformed = directory.CreateFile(
            "MineRailMonitor_20260906_020000_extra.db",
            LocalNow.Date.AddDays(-14));

        new SqliteRetentionService(14, new TestLogger()).Apply(directory.BackupRoot, LocalNow);

        Assert.False(File.Exists(expiredFormal));
        Assert.True(File.Exists(expiredWrongDate));
        Assert.True(File.Exists(malformed));
    }

    [Fact]
    public void Retention_does_not_delete_unknown_files_inside_expired_date_directory()
    {
        using var directory = new TemporaryDirectory();
        var unknown = directory.CreateFile(
            "operator-notes.txt",
            LocalNow.Date.AddDays(-14));
        directory.CreateFormal(LocalNow.Date.AddDays(-14), "020000");

        new SqliteRetentionService(14, new TestLogger()).Apply(directory.BackupRoot, LocalNow);

        Assert.True(File.Exists(unknown));
        Assert.True(Directory.Exists(Path.GetDirectoryName(unknown)));
    }

    [Fact]
    public void Retention_does_not_delete_non_date_directories()
    {
        using var directory = new TemporaryDirectory();
        var unknown = directory.CreateFile("MineRailMonitor_20260901_020000.db", "not-a-date");

        new SqliteRetentionService(14, new TestLogger()).Apply(directory.BackupRoot, LocalNow);

        Assert.True(File.Exists(unknown));
    }

    [Fact]
    public void Retention_never_touches_corrupt_evidence_directory()
    {
        using var directory = new TemporaryDirectory();
        var evidence = directory.CreateFile("corrupt.db", "Corrupt");

        new SqliteRetentionService(14, new TestLogger()).Apply(directory.BackupRoot, LocalNow);

        Assert.True(File.Exists(evidence));
    }

    [Fact]
    public void Startup_cleanup_removes_only_application_tmp_older_than_24_hours()
    {
        using var directory = new TemporaryDirectory();
        var stale = directory.CreateTmp(LocalNow.Date, "020000");
        File.SetLastWriteTimeUtc(stale, LocalNow.UtcDateTime.AddHours(-25));
        var malformed = directory.CreateFile(
            "MineRailMonitor_20260919_020000.tmp.db.bak",
            LocalNow.Date);
        var unknown = directory.CreateFile("unknown.tmp.db", LocalNow.Date);

        new SqliteRetentionService(14, new TestLogger()).Apply(directory.BackupRoot, LocalNow);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(malformed));
        Assert.True(File.Exists(unknown));
    }

    [Fact]
    public void Tmp_exactly_24_hours_old_is_not_deleted()
    {
        using var directory = new TemporaryDirectory();
        var exact = directory.CreateTmp(LocalNow.Date, "020000");
        File.SetLastWriteTimeUtc(exact, LocalNow.UtcDateTime.AddHours(-24));

        new SqliteRetentionService(14, new TestLogger()).Apply(directory.BackupRoot, LocalNow);

        Assert.True(File.Exists(exact));
    }

    [Fact]
    public void Malformed_tmp_is_not_deleted()
    {
        using var directory = new TemporaryDirectory();
        var malformed = directory.CreateFile("MineRailMonitor_bad.tmp.db", LocalNow.Date);
        File.SetLastWriteTimeUtc(malformed, LocalNow.UtcDateTime.AddHours(-48));

        new SqliteRetentionService(14, new TestLogger()).Apply(directory.BackupRoot, LocalNow);

        Assert.True(File.Exists(malformed));
    }

    private sealed class TestLogger : ILogger
    {
        public void Information(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
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
            Directory.CreateDirectory(BackupRoot);
        }

        public string Path { get; }
        public string BackupRoot { get; }

        public string CreateFormal(DateTime date, string time) =>
            CreateFile(
                $"MineRailMonitor_{date:yyyyMMdd}_{time}.db",
                date);

        public string CreateTmp(DateTime date, string time) =>
            CreateFile(
                $"MineRailMonitor_{date:yyyyMMdd}_{time}.tmp.db",
                date);

        public string CreateFile(string fileName, DateTime date) =>
            CreateFile(fileName, date.ToString("yyyy-MM-dd"));

        public string CreateFile(string fileName, string directoryName)
        {
            var directory = System.IO.Path.Combine(BackupRoot, directoryName);
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, fileName);
            File.WriteAllText(path, "test");
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
