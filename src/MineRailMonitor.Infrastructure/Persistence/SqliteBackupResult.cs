namespace MineRailMonitor.Infrastructure.Persistence;

public sealed class SqliteBackupResult
{
    public SqliteBackupResult(bool succeeded, string? finalPath, string? errorMessage)
    {
        Succeeded = succeeded;
        FinalPath = finalPath;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }

    public string? FinalPath { get; }

    public string? ErrorMessage { get; }
}
