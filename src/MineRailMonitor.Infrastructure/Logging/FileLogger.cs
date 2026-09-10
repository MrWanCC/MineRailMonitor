using System.Globalization;

namespace MineRailMonitor.Infrastructure.Logging;

public sealed class FileLogger : ILogger
{
    private readonly object _syncRoot = new();
    private readonly string _logDirectory;
    private readonly int _retentionDays;

    public FileLogger(string logDirectory, int retentionDays = 30)
    {
        _logDirectory = logDirectory;
        _retentionDays = Math.Max(1, retentionDays);
    }

    public void Information(string message) => Write("INFO", message, null);

    public void Warning(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (_syncRoot)
            {
                Directory.CreateDirectory(_logDirectory);
                var logFile = Path.Combine(
                    _logDirectory,
                    $"{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");
                var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
                if (exception is not null)
                {
                    line += $"{Environment.NewLine}{exception}";
                }

                File.AppendAllText(logFile, line + Environment.NewLine);
                PurgeOldLogs();
            }
        }
        catch
        {
            // Logging must never bring down the field application.
        }
    }

    private void PurgeOldLogs()
    {
        var cutoff = DateTime.Now.Date.AddDays(-_retentionDays);
        foreach (var file in Directory.EnumerateFiles(_logDirectory, "*.log"))
        {
            if (File.GetLastWriteTime(file) < cutoff)
            {
                File.Delete(file);
            }
        }
    }
}
