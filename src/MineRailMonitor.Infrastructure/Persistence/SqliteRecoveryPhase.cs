using Newtonsoft.Json;

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
        DateTimeOffset startedAt,
        string? bundleManifestSha256 = null)
    {
        SourceBackupPath = sourceBackupPath;
        CorruptBundlePath = corruptBundlePath;
        StagingPath = stagingPath;
        StartedAt = startedAt;
        BundleManifestSha256 = bundleManifestSha256 ?? string.Empty;
        RecoveryStarted = true;
    }

    [JsonProperty("recoveryStarted")]
    public bool RecoveryStarted { get; set; }

    [JsonProperty("sourceBackupPath")]
    public string SourceBackupPath { get; set; }

    [JsonProperty("corruptBundlePath")]
    public string CorruptBundlePath { get; set; }

    [JsonProperty("stagingPath")]
    public string StagingPath { get; set; }

    [JsonProperty("startedAt")]
    public DateTimeOffset StartedAt { get; set; }

    [JsonProperty("bundleManifestSha256")]
    public string BundleManifestSha256 { get; set; }
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
