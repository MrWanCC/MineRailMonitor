using System.Data.SQLite;
using System.Globalization;
using System.Text.RegularExpressions;
using MineRailMonitor.Infrastructure.Logging;

namespace MineRailMonitor.Infrastructure.Persistence;

public sealed class SqliteBackupService : ISqliteBackupService
{
    private static readonly Regex FormalBackupFileName = new(
        "^MineRailMonitor_(?<date>[0-9]{8})_(?<time>[0-9]{6})\\.db$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ISqliteDatabaseHealthChecker _healthChecker;
    private readonly ILogger _logger;

    public SqliteBackupService(
        ISqliteDatabaseHealthChecker healthChecker,
        ILogger logger)
    {
        _healthChecker = healthChecker ?? throw new ArgumentNullException(nameof(healthChecker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SqliteBackupResult> CreateValidatedBackupAsync(
        string productionDatabasePath,
        string backupRootDirectory,
        DateTimeOffset localNow,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productionDatabasePath))
        {
            throw new ArgumentException("生产数据库路径不能为空。", nameof(productionDatabasePath));
        }

        if (string.IsNullOrWhiteSpace(backupRootDirectory))
        {
            throw new ArgumentException("备份根目录不能为空。", nameof(backupRootDirectory));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Failure("SQLite backup cancelled before it started."));
        }

        string? temporaryPath = null;
        try
        {
            var productionPath = System.IO.Path.GetFullPath(productionDatabasePath);
            var rootPath = System.IO.Path.GetFullPath(backupRootDirectory);
            var dateDirectory = localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var timestamp = localNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var directory = System.IO.Path.Combine(rootPath, dateDirectory);
            Directory.CreateDirectory(directory);

            temporaryPath = System.IO.Path.Combine(
                directory,
                $"MineRailMonitor_{timestamp}.tmp.db");
            var finalPath = System.IO.Path.Combine(
                directory,
                $"MineRailMonitor_{timestamp}.db");

            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            using (var source = OpenSourceConnection(productionPath))
            using (var destination = OpenDestinationConnection(temporaryPath))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(
                    destination,
                    "main",
                    "main",
                    -1,
                    null,
                    0);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(FailureAndCleanup(
                    temporaryPath,
                    "SQLite backup cancelled before validation."));
            }

            var health = _healthChecker.Inspect(
                temporaryPath,
                SqliteInspectionMode.FullValidation);
            if (health.State != SqliteDatabaseHealthState.Healthy)
            {
                return Task.FromResult(FailureAndCleanup(
                    temporaryPath,
                    $"SQLite backup validation returned {health.State}."));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(FailureAndCleanup(
                    temporaryPath,
                    "SQLite backup cancelled before promotion."));
            }

            File.Move(temporaryPath, finalPath);
            temporaryPath = null;
            return Task.FromResult(new SqliteBackupResult(true, finalPath, null));
        }
        catch (Exception exception)
        {
            _logger.Error("SQLite backup failed.", exception);
            return Task.FromResult(FailureAndCleanup(
                temporaryPath,
                exception.Message));
        }
    }

    public IReadOnlyList<SqliteBackupCandidate> ScanCandidates(string backupRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(backupRootDirectory))
        {
            throw new ArgumentException("备份根目录不能为空。", nameof(backupRootDirectory));
        }

        var rootPath = System.IO.Path.GetFullPath(backupRootDirectory);
        if (!Directory.Exists(rootPath))
        {
            return Array.Empty<SqliteBackupCandidate>();
        }

        var candidates = new List<SqliteBackupCandidate>();
        foreach (var directory in Directory.EnumerateDirectories(rootPath))
        {
            var directoryName = System.IO.Path.GetFileName(directory);
            if (!DateTime.TryParseExact(
                    directoryName,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var directoryDate))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.db", SearchOption.TopDirectoryOnly))
            {
                var match = FormalBackupFileName.Match(System.IO.Path.GetFileName(file));
                if (!match.Success ||
                    !DateTime.TryParseExact(
                        match.Groups["date"].Value + "_" + match.Groups["time"].Value,
                        "yyyyMMdd_HHmmss",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var fileTimestamp))
                {
                    continue;
                }

                if (directoryDate.Date != fileTimestamp.Date)
                {
                    continue;
                }

                var localOffset = TimeZoneInfo.Local.GetUtcOffset(fileTimestamp);
                candidates.Add(new SqliteBackupCandidate(
                    file,
                    new DateTimeOffset(fileTimestamp, localOffset)));
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.LocalTimestamp)
            .ToArray();
    }

    private SqliteBackupResult FailureAndCleanup(string? temporaryPath, string errorMessage)
    {
        CleanupTemporaryFile(temporaryPath);
        return Failure(errorMessage);
    }

    private SqliteBackupResult Failure(string errorMessage) =>
        new(false, null, errorMessage);

    private void CleanupTemporaryFile(string? temporaryPath)
    {
        if (string.IsNullOrWhiteSpace(temporaryPath) || !File.Exists(temporaryPath))
        {
            return;
        }

        try
        {
            File.Delete(temporaryPath);
        }
        catch (Exception exception)
        {
            _logger.Warning($"Unable to clean up SQLite temporary backup: {temporaryPath}");
            _logger.Error("SQLite temporary backup cleanup failed.", exception);
        }
    }

    private static SQLiteConnection OpenSourceConnection(string productionPath) =>
        new($"Data Source={productionPath};Version=3;Read Only=True;Default Timeout=0;");

    private static SQLiteConnection OpenDestinationConnection(string temporaryPath) =>
        new($"Data Source={temporaryPath};Version=3;");
}
