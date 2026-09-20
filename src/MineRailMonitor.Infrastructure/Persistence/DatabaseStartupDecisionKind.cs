namespace MineRailMonitor.Infrastructure.Persistence;

public enum DatabaseStartupDecisionKind
{
    CreateNew,
    StartHealthy,
    RecoverCorrupt,
    Unavailable,
    UnsupportedSchema,
    InterruptedRecovery,
    RecoveryStateError
}
