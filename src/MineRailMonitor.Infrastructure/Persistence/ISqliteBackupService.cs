namespace MineRailMonitor.Infrastructure.Persistence;

public interface ISqliteBackupService
{
    Task<SqliteBackupResult> CreateValidatedBackupAsync(
        string productionDatabasePath,
        string backupRootDirectory,
        DateTimeOffset localNow,
        CancellationToken cancellationToken);

    IReadOnlyList<SqliteBackupCandidate> ScanCandidates(
        string backupRootDirectory);
}
