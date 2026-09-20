namespace MineRailMonitor.Infrastructure.Persistence;

public enum SqliteDatabaseHealthState
{
    Missing,
    Healthy,
    Corrupt,
    Unavailable
}

public enum SqliteInspectionMode
{
    StartupFast,
    FullValidation
}
