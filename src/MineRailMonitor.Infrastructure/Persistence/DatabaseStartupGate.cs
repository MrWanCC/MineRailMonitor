using MineRailMonitor.Infrastructure.Logging;

namespace MineRailMonitor.Infrastructure.Persistence;

public sealed class DatabaseStartupGate
{
    private readonly string _productionDatabasePath;
    private readonly string _backupRootDirectory;
    private readonly ISqliteDatabaseHealthChecker _healthChecker;
    private readonly ISqliteBackupService _backupService;
    private readonly SqliteRecoveryService _recoveryService;
    private readonly bool _acceptanceMode;
    private readonly ILogger _logger;

    public DatabaseStartupGate(
        string productionDatabasePath,
        string backupRootDirectory,
        ISqliteDatabaseHealthChecker healthChecker,
        ISqliteBackupService backupService,
        SqliteRecoveryService recoveryService,
        bool acceptanceMode,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(productionDatabasePath))
        {
            throw new ArgumentException("生产数据库路径不能为空。", nameof(productionDatabasePath));
        }

        if (string.IsNullOrWhiteSpace(backupRootDirectory))
        {
            throw new ArgumentException("备份根目录不能为空。", nameof(backupRootDirectory));
        }

        _productionDatabasePath = Path.GetFullPath(productionDatabasePath);
        _backupRootDirectory = Path.GetFullPath(backupRootDirectory);
        _healthChecker = healthChecker ?? throw new ArgumentNullException(nameof(healthChecker));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _recoveryService = recoveryService ?? throw new ArgumentNullException(nameof(recoveryService));
        _acceptanceMode = acceptanceMode;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public DatabaseStartupDecision Inspect()
    {
        if (_acceptanceMode)
        {
            return InspectProductionDatabase(includeRecoveryCandidates: false);
        }

        SqliteRecoveryMarker? marker;
        try
        {
            marker = _recoveryService.ReadMarker();
        }
        catch (InvalidDataException exception)
        {
            _logger.Error("SQLite recovery marker cannot be trusted.", exception);
            return new DatabaseStartupDecision(
                DatabaseStartupDecisionKind.RecoveryStateError,
                errorMessage: exception.Message);
        }

        if (marker is not null)
        {
            return new DatabaseStartupDecision(
                DatabaseStartupDecisionKind.InterruptedRecovery,
                recoveryMarker: marker);
        }

        return InspectProductionDatabase(includeRecoveryCandidates: true);
    }

    private DatabaseStartupDecision InspectProductionDatabase(bool includeRecoveryCandidates)
    {
        var health = _healthChecker.Inspect(
            _productionDatabasePath,
            SqliteInspectionMode.StartupFast);

        return health.State switch
        {
            SqliteDatabaseHealthState.Missing => new DatabaseStartupDecision(
                DatabaseStartupDecisionKind.CreateNew,
                health),
            SqliteDatabaseHealthState.Healthy => new DatabaseStartupDecision(
                DatabaseStartupDecisionKind.StartHealthy,
                health),
            SqliteDatabaseHealthState.Unavailable => new DatabaseStartupDecision(
                DatabaseStartupDecisionKind.Unavailable,
                health),
            SqliteDatabaseHealthState.UnsupportedSchema => new DatabaseStartupDecision(
                DatabaseStartupDecisionKind.UnsupportedSchema,
                health),
            SqliteDatabaseHealthState.Corrupt => includeRecoveryCandidates
                ? InspectCorruptDatabase(health)
                : new DatabaseStartupDecision(
                    DatabaseStartupDecisionKind.RecoverCorrupt,
                    health),
            _ => throw new InvalidOperationException($"Unknown SQLite health state: {health.State}")
        };
    }

    private DatabaseStartupDecision InspectCorruptDatabase(SqliteDatabaseHealthResult health)
    {
        var candidates = _backupService
            .ScanCandidates(_backupRootDirectory)
            .OrderByDescending(candidate => candidate.LocalTimestamp)
            .ToArray();
        var healthyCandidates = new List<SqliteBackupCandidate>();
        foreach (var candidate in candidates)
        {
            var candidateHealth = _healthChecker.Inspect(
                candidate.Path,
                SqliteInspectionMode.FullValidation);
            if (candidateHealth.State == SqliteDatabaseHealthState.Healthy)
            {
                healthyCandidates.Add(candidate);
            }
            else
            {
                _logger.Warning(
                    $"SQLite recovery candidate excluded: {candidate.Path} ({candidateHealth.State}).");
            }
        }

        return new DatabaseStartupDecision(
            DatabaseStartupDecisionKind.RecoverCorrupt,
            health,
            healthyCandidates);
    }
}
