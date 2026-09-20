namespace MineRailMonitor.Infrastructure.Persistence;

public enum SqliteRecoveryPhase
{
    StagingValidated,
    CorruptBundleSaved,
    RecoveryMarkerPersisted,
    SidecarsRemoved,
    ProductionReplacementStarted,
    FinalHealthCheckCompleted
}

public sealed class SqliteRecoveryMarker
{
    public SqliteRecoveryMarker(
        string sourceBackupPath,
        string corruptBundlePath,
        string stagingPath,
        DateTimeOffset startedAt)
    {
        SourceBackupPath = sourceBackupPath;
        CorruptBundlePath = corruptBundlePath;
        StagingPath = stagingPath;
        StartedAt = startedAt;
        RecoveryStarted = true;
    }

    public bool RecoveryStarted { get; set; }

    public string SourceBackupPath { get; set; }

    public string CorruptBundlePath { get; set; }

    public string StagingPath { get; set; }

    public DateTimeOffset StartedAt { get; set; }
}

public sealed class SqliteRecoveryResult
{
    public SqliteRecoveryResult(
        bool succeeded,
        string? errorMessage = null,
        SqliteRecoveryMarker? marker = null)
    {
        Succeeded = succeeded;
        ErrorMessage = errorMessage;
        Marker = marker;
    }

    public bool Succeeded { get; }

    public string? ErrorMessage { get; }

    public SqliteRecoveryMarker? Marker { get; }
}
