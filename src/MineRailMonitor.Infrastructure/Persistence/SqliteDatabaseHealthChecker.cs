using System.Data.SQLite;
using System.Runtime.CompilerServices;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Infrastructure.Logging;

[assembly: InternalsVisibleTo("MineRailMonitor.Infrastructure.Tests")]

namespace MineRailMonitor.Infrastructure.Persistence;

public sealed class SqliteDatabaseHealthChecker : ISqliteDatabaseHealthChecker
{
    private readonly IRfidTimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Func<string, SQLiteConnection> _inspectionConnectionFactory;

    public SqliteDatabaseHealthChecker(
        IRfidTimeProvider timeProvider,
        ILogger logger,
        Func<string, SQLiteConnection>? inspectionConnectionFactory = null)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inspectionConnectionFactory = inspectionConnectionFactory ?? OpenInspectionConnection;
    }

    public SqliteDatabaseHealthResult Inspect(string databasePath, SqliteInspectionMode mode)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("SQLite数据库路径不能为空。", nameof(databasePath));
        }

        var fullPath = Path.GetFullPath(databasePath);
        var checkedAt = _timeProvider.UtcNow;
        var existence = CheckFileAvailability(fullPath, checkedAt);
        if (existence is not null)
        {
            return existence;
        }

        var inspection = new InspectionState(mode);
        try
        {
            using var connection = _inspectionConnectionFactory(fullPath);
            connection.Open();

            if (mode == SqliteInspectionMode.FullValidation)
            {
                RunIntegrityCheck(connection, inspection);
                RunForeignKeyCheck(connection, inspection);
            }
            else
            {
                RunQuickCheck(connection, inspection);
                RunForeignKeyCheck(connection, inspection);

                if (!inspection.QuickCheckPassed || !inspection.ForeignKeyCheckPassed)
                {
                    RunIntegrityCheck(connection, inspection);
                }
            }
        }
        catch (Exception exception)
        {
            if (IsDeterministicCorruption(exception))
            {
                inspection.RecordCorruption(exception);
                if (!inspection.IntegrityCheckExecuted)
                {
                    inspection.IntegrityCheckExecuted = true;
                    inspection.IntegrityCheckPassed = false;
                    inspection.IntegrityCheckSummary = exception.Message;
                }
            }
            else
            {
                inspection.RecordException(exception);
            }
        }

        var state = ResolveState(inspection.IsCorrupt, inspection.HasUnavailableError);
        if (state == SqliteDatabaseHealthState.Unavailable)
        {
            return CreateUnavailable(fullPath, checkedAt, inspection.UnavailableError!);
        }

        return CreateResult(fullPath, checkedAt, inspection, state);
    }

    private SqliteDatabaseHealthResult? CheckFileAvailability(
        string databasePath,
        DateTimeOffset checkedAt)
    {
        try
        {
            File.GetAttributes(databasePath);
            return null;
        }
        catch (FileNotFoundException)
        {
            return CreateMissing(databasePath, checkedAt);
        }
        catch (DirectoryNotFoundException)
        {
            return CreateMissing(databasePath, checkedAt);
        }
        catch (UnauthorizedAccessException exception)
        {
            return CreateUnavailable(databasePath, checkedAt, exception);
        }
        catch (IOException exception)
        {
            return CreateUnavailable(databasePath, checkedAt, exception);
        }
    }

    private void RunQuickCheck(SQLiteConnection connection, InspectionState inspection)
    {
        var result = ExecuteScalarCheck(connection, "PRAGMA quick_check;");
        if (result.Exception is not null)
        {
            inspection.RecordException(result.Exception);
            inspection.QuickCheckPassed = false;
            inspection.QuickCheckSummary = result.Exception.Message;
            return;
        }

        inspection.QuickCheckPassed = result.Passed;
        inspection.QuickCheckSummary = result.Summary;
        if (!result.Passed)
        {
            inspection.IsCorrupt = true;
        }
    }

    private void RunIntegrityCheck(SQLiteConnection connection, InspectionState inspection)
    {
        inspection.IntegrityCheckExecuted = true;
        var result = ExecuteScalarCheck(connection, "PRAGMA integrity_check;");
        if (result.Exception is not null)
        {
            inspection.RecordException(result.Exception);
            inspection.IntegrityCheckPassed = false;
            inspection.IntegrityCheckSummary = result.Exception.Message;
            return;
        }

        inspection.IntegrityCheckPassed = result.Passed;
        inspection.IntegrityCheckSummary = result.Summary;
        if (!result.Passed)
        {
            inspection.IsCorrupt = true;
        }
    }

    private void RunForeignKeyCheck(SQLiteConnection connection, InspectionState inspection)
    {
        var result = ExecuteForeignKeyCheck(connection);
        if (result.Exception is not null)
        {
            inspection.RecordException(result.Exception);
            inspection.ForeignKeyCheckPassed = false;
            inspection.ForeignKeyCheckSummary = result.Exception.Message;
            return;
        }

        inspection.ForeignKeyCheckPassed = result.Passed;
        inspection.ForeignKeyCheckSummary = result.Summary;
        if (!result.Passed)
        {
            inspection.IsCorrupt = true;
        }
    }

    private static CheckResult ExecuteScalarCheck(SQLiteConnection connection, string sql)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = Convert.ToString(command.ExecuteScalar())?.Trim() ?? string.Empty;
            return new CheckResult(
                string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase),
                value,
                null);
        }
        catch (Exception exception)
        {
            return new CheckResult(false, exception.Message, exception);
        }
    }

    private static CheckResult ExecuteForeignKeyCheck(SQLiteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_key_check;";
            using var reader = command.ExecuteReader();
            var violations = new List<string>();
            while (reader.Read())
            {
                var values = new string[reader.FieldCount];
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    values[index] = Convert.ToString(reader.GetValue(index)) ?? string.Empty;
                }

                violations.Add(string.Join(",", values));
            }

            return new CheckResult(
                violations.Count == 0,
                violations.Count == 0 ? "ok" : string.Join("; ", violations),
                null);
        }
        catch (Exception exception)
        {
            return new CheckResult(false, exception.Message, exception);
        }
    }

    private SqliteDatabaseHealthResult CreateMissing(string databasePath, DateTimeOffset checkedAt) =>
        new(
            databasePath,
            SqliteDatabaseHealthState.Missing,
            checkedAt,
            false,
            "database file is missing",
            false,
            false,
            "not executed",
            false,
            "not executed");

    private SqliteDatabaseHealthResult CreateUnavailable(
        string databasePath,
        DateTimeOffset checkedAt,
        Exception exception)
    {
        _logger.Error($"SQLite health inspection unavailable: {databasePath}", exception);
        var diagnostic = GetErrorDiagnostic(exception);
        return new SqliteDatabaseHealthResult(
            databasePath,
            SqliteDatabaseHealthState.Unavailable,
            checkedAt,
            false,
            "unavailable",
            false,
            false,
            "not executed",
            false,
            "not executed",
            exception.GetType().Name,
            exception.Message,
            diagnostic.ErrorCode,
            diagnostic.ErrorCodeName);
    }

    private static SqliteDatabaseHealthResult CreateResult(
        string databasePath,
        DateTimeOffset checkedAt,
        InspectionState inspection,
        SqliteDatabaseHealthState state) =>
        new(
            databasePath,
            state,
            checkedAt,
            inspection.QuickCheckPassed,
            inspection.QuickCheckSummary,
            inspection.IntegrityCheckExecuted,
            inspection.IntegrityCheckPassed,
            inspection.IntegrityCheckSummary,
            inspection.ForeignKeyCheckPassed,
            inspection.ForeignKeyCheckSummary,
            inspection.CorruptionError?.GetType().Name,
            inspection.CorruptionError?.Message,
            inspection.CorruptionError is null
                ? null
                : GetErrorDiagnostic(inspection.CorruptionError).ErrorCode,
            inspection.CorruptionError is null
                ? null
                : GetErrorDiagnostic(inspection.CorruptionError).ErrorCodeName);

    internal static SqliteDatabaseHealthState ResolveState(bool hasCorruption, bool hasUnavailableError) =>
        hasCorruption
            ? SqliteDatabaseHealthState.Corrupt
            : hasUnavailableError
                ? SqliteDatabaseHealthState.Unavailable
                : SqliteDatabaseHealthState.Healthy;

    private static SQLiteConnection OpenInspectionConnection(string databasePath) =>
        new($"Data Source={databasePath};Version=3;Read Only=True;Default Timeout=0;");

    private static bool IsDeterministicCorruption(Exception exception)
    {
        if (exception is not SQLiteException)
        {
            return false;
        }

        var sqliteException = (SQLiteException)exception;
        var baseCode = (int)sqliteException.ResultCode & 0xFF;
        if (baseCode == (int)SQLiteErrorCode.Corrupt ||
            baseCode == (int)SQLiteErrorCode.NotADb)
        {
            return true;
        }

        var message = exception.Message.ToLowerInvariant();
        return message.IndexOf("database disk image is malformed", StringComparison.Ordinal) >= 0 ||
               message.IndexOf("malformed database schema", StringComparison.Ordinal) >= 0 ||
               message.IndexOf("file is encrypted or is not a database", StringComparison.Ordinal) >= 0 ||
               message.IndexOf("unsupported file format", StringComparison.Ordinal) >= 0;
    }

    private static ErrorDiagnostic GetErrorDiagnostic(Exception exception)
    {
        if (exception is SQLiteException sqliteException)
        {
            return new ErrorDiagnostic(
                sqliteException.ErrorCode,
                sqliteException.ResultCode.ToString());
        }

        return new ErrorDiagnostic(exception.HResult, exception.GetType().Name);
    }

    private sealed class InspectionState
    {
        public InspectionState(SqliteInspectionMode mode)
        {
            QuickCheckSummary = mode == SqliteInspectionMode.FullValidation
                ? "not executed in FullValidation"
                : "not executed";
            IntegrityCheckSummary = "not executed";
            ForeignKeyCheckSummary = "not executed";
        }

        public bool QuickCheckPassed { get; set; }

        public string QuickCheckSummary { get; set; }

        public bool IntegrityCheckExecuted { get; set; }

        public bool IntegrityCheckPassed { get; set; }

        public string IntegrityCheckSummary { get; set; }

        public bool ForeignKeyCheckPassed { get; set; }

        public string ForeignKeyCheckSummary { get; set; }

        public bool IsCorrupt { get; set; }

        public Exception? CorruptionError { get; private set; }

        public Exception? UnavailableError { get; private set; }

        public bool HasUnavailableError => UnavailableError is not null;

        public void RecordException(Exception exception)
        {
            if (IsDeterministicCorruption(exception))
            {
                RecordCorruption(exception);
                return;
            }

            UnavailableError ??= exception;
        }

        public void RecordCorruption(Exception exception)
        {
            IsCorrupt = true;
            CorruptionError ??= exception;
        }
    }

    private sealed class ErrorDiagnostic
    {
        public ErrorDiagnostic(int errorCode, string errorCodeName)
        {
            ErrorCode = errorCode;
            ErrorCodeName = errorCodeName;
        }

        public int ErrorCode { get; }

        public string ErrorCodeName { get; }
    }

    private sealed class CheckResult
    {
        public CheckResult(bool passed, string summary, Exception? exception)
        {
            Passed = passed;
            Summary = summary;
            Exception = exception;
        }

        public bool Passed { get; }

        public string Summary { get; }

        public Exception? Exception { get; }
    }
}
