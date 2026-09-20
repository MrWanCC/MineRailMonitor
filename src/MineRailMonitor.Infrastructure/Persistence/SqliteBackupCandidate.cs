namespace MineRailMonitor.Infrastructure.Persistence;

public sealed class SqliteBackupCandidate
{
    public SqliteBackupCandidate(string path, DateTimeOffset localTimestamp)
    {
        Path = System.IO.Path.GetFullPath(path);
        LocalTimestamp = localTimestamp;
    }

    public string Path { get; }

    public DateTimeOffset LocalTimestamp { get; }
}
