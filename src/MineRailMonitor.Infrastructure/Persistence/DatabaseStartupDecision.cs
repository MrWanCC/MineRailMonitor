namespace MineRailMonitor.Infrastructure.Persistence;

public sealed class DatabaseStartupDecision
{
    public DatabaseStartupDecision(
        DatabaseStartupDecisionKind kind,
        SqliteDatabaseHealthResult? health = null,
        IReadOnlyList<SqliteBackupCandidate>? candidates = null,
        SqliteRecoveryMarker? recoveryMarker = null,
        string? errorMessage = null)
    {
        Kind = kind;
        Health = health;
        Candidates = candidates ?? Array.Empty<SqliteBackupCandidate>();
        RecoveryMarker = recoveryMarker;
        ErrorMessage = errorMessage;
    }

    public DatabaseStartupDecisionKind Kind { get; }

    public SqliteDatabaseHealthResult? Health { get; }

    public IReadOnlyList<SqliteBackupCandidate> Candidates { get; }

    public SqliteRecoveryMarker? RecoveryMarker { get; }

    public string? ErrorMessage { get; }
}
