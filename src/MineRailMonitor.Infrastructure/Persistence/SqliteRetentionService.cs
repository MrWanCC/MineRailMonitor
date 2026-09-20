using System.Globalization;
using System.Text.RegularExpressions;
using MineRailMonitor.Infrastructure.Logging;

namespace MineRailMonitor.Infrastructure.Persistence;

public sealed class SqliteRetentionService
{
    private static readonly Regex FormalBackupFileName = new(
        "^MineRailMonitor_(?<date>[0-9]{8})_(?<time>[0-9]{6})\\.db$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TemporaryBackupFileName = new(
        "^MineRailMonitor_(?<date>[0-9]{8})_(?<time>[0-9]{6})\\.tmp\\.db$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly int _retentionDays;
    private readonly ILogger _logger;

    public SqliteRetentionService(int retentionDays, ILogger logger)
    {
        if (retentionDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays));
        }

        _retentionDays = retentionDays;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Apply(string backupRootDirectory, DateTimeOffset localNow)
    {
        if (string.IsNullOrWhiteSpace(backupRootDirectory))
        {
            throw new ArgumentException("备份根目录不能为空。", nameof(backupRootDirectory));
        }

        var rootPath = Path.GetFullPath(backupRootDirectory);
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var directory in EnumerateDateDirectories(rootPath))
        {
            DeleteExpiredFormalBackups(directory.Path, directory.Date, localNow.Date);
            CleanupStaleFilesInDateDirectory(directory.Path, directory.Date, localNow);
        }
    }

    public void CleanupStaleTemporaryFiles(
        string backupRootDirectory,
        DateTimeOffset localNow)
    {
        if (string.IsNullOrWhiteSpace(backupRootDirectory))
        {
            throw new ArgumentException("备份根目录不能为空。", nameof(backupRootDirectory));
        }

        var rootPath = Path.GetFullPath(backupRootDirectory);
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var directory in EnumerateDateDirectories(rootPath))
        {
            CleanupStaleFilesInDateDirectory(directory.Path, directory.Date, localNow);
        }
    }

    private IEnumerable<(string Path, DateTime Date)> EnumerateDateDirectories(string rootPath)
    {
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(rootPath);
        }
        catch (Exception exception)
        {
            LogFailure("SQLite retention directory enumeration failed.", exception);
            yield break;
        }

        foreach (var directory in directories)
        {
            var name = System.IO.Path.GetFileName(directory);
            if (!DateTime.TryParseExact(
                    name,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                continue;
            }

            yield return (directory, date.Date);
        }
    }

    private void DeleteExpiredFormalBackups(
        string directory,
        DateTime directoryDate,
        DateTime localDate)
    {
        if (directoryDate > localDate.AddDays(-_retentionDays))
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.db", SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception)
        {
            LogFailure("SQLite retention file enumeration failed.", exception);
            return;
        }

        var deletedFormalBackup = false;
        foreach (var file in files)
        {
            if (!TryParseFormalFile(file, directoryDate))
            {
                continue;
            }

            try
            {
                File.Delete(file);
                deletedFormalBackup = true;
            }
            catch (Exception exception)
            {
                LogFailure($"SQLite retention could not delete formal backup: {file}", exception);
            }
        }

        if (!deletedFormalBackup)
        {
            return;
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory, recursive: false);
            }
        }
        catch (Exception exception)
        {
            LogFailure($"SQLite retention could not remove empty date directory: {directory}", exception);
        }
    }

    private void CleanupStaleFilesInDateDirectory(
        string directory,
        DateTime directoryDate,
        DateTimeOffset localNow)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.tmp.db", SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception)
        {
            LogFailure("SQLite temporary backup enumeration failed.", exception);
            return;
        }

        foreach (var file in files)
        {
            if (!TryParseTemporaryFile(file, directoryDate))
            {
                continue;
            }

            DateTime lastWriteUtc;
            try
            {
                lastWriteUtc = File.GetLastWriteTimeUtc(file);
            }
            catch (Exception exception)
            {
                LogFailure($"SQLite temporary backup timestamp read failed: {file}", exception);
                continue;
            }

            if (localNow.UtcDateTime - lastWriteUtc <= TimeSpan.FromHours(24))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception exception)
            {
                LogFailure($"SQLite stale temporary backup deletion failed: {file}", exception);
            }
        }
    }

    private static bool TryParseFormalFile(string file, DateTime directoryDate)
    {
        var match = FormalBackupFileName.Match(System.IO.Path.GetFileName(file));
        return match.Success &&
               DateTime.TryParseExact(
                   match.Groups["date"].Value,
                   "yyyyMMdd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out var fileDate) &&
               fileDate.Date == directoryDate.Date;
    }

    private static bool TryParseTemporaryFile(string file, DateTime directoryDate)
    {
        var match = TemporaryBackupFileName.Match(System.IO.Path.GetFileName(file));
        return match.Success &&
               DateTime.TryParseExact(
                   match.Groups["date"].Value,
                   "yyyyMMdd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out var fileDate) &&
               fileDate.Date == directoryDate.Date;
    }

    private void LogFailure(string message, Exception exception)
    {
        _logger.Warning(message);
        _logger.Error(message, exception);
    }
}
