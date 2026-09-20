namespace MineRailMonitor.Infrastructure.Persistence;

public interface ISqliteDatabaseHealthChecker
{
    SqliteDatabaseHealthResult Inspect(
        string databasePath,
        SqliteInspectionMode mode);
}

public sealed class SqliteDatabaseHealthResult
{
    public SqliteDatabaseHealthResult(
        string databasePath,
        SqliteDatabaseHealthState state,
        DateTimeOffset checkedAt,
        bool quickCheckPassed,
        string quickCheckSummary,
        bool integrityCheckExecuted,
        bool integrityCheckPassed,
        string integrityCheckSummary,
        bool foreignKeyCheckPassed,
        string foreignKeyCheckSummary,
        string? errorType = null,
        string? errorMessage = null,
        int? errorCode = null,
        string? errorCodeName = null)
    {
        DatabasePath = databasePath;
        State = state;
        CheckedAt = checkedAt;
        QuickCheckPassed = quickCheckPassed;
        QuickCheckSummary = quickCheckSummary;
        IntegrityCheckExecuted = integrityCheckExecuted;
        IntegrityCheckPassed = integrityCheckPassed;
        IntegrityCheckSummary = integrityCheckSummary;
        ForeignKeyCheckPassed = foreignKeyCheckPassed;
        ForeignKeyCheckSummary = foreignKeyCheckSummary;
        ErrorType = errorType;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        ErrorCodeName = errorCodeName;
    }

    public string DatabasePath { get; }

    public SqliteDatabaseHealthState State { get; }

    public DateTimeOffset CheckedAt { get; }

    public bool QuickCheckPassed { get; }

    public string QuickCheckSummary { get; }

    public bool IntegrityCheckExecuted { get; }

    public bool IntegrityCheckPassed { get; }

    public string IntegrityCheckSummary { get; }

    public bool ForeignKeyCheckPassed { get; }

    public string ForeignKeyCheckSummary { get; }

    public string? ErrorType { get; }

    public string? ErrorMessage { get; }

    public int? ErrorCode { get; }

    public string? ErrorCodeName { get; }
}
