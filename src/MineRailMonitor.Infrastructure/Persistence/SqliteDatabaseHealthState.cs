namespace MineRailMonitor.Infrastructure.Persistence;

public enum SqliteDatabaseHealthState
{
    Missing,
    Healthy,
    Corrupt,
    Unavailable,
    UnsupportedSchema
}

public enum SqliteInspectionMode
{
    StartupFast,
    FullValidation
}
