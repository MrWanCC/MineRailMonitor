using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Infrastructure.Logging;
using Newtonsoft.Json;

namespace MineRailMonitor.Infrastructure.Persistence;

public sealed class SqliteRecoveryService
{
    private const string ManifestFileName = "bundle-manifest.json";

    private readonly ISqliteDatabaseHealthChecker _healthChecker;
    private readonly string _dataDirectory;
    private readonly SqliteRecoveryMarkerStore _markerStore;
    private readonly ILogger _logger;
    private readonly IRfidTimeProvider _timeProvider;
    private readonly Action<SqliteRecoveryPhase>? _phaseObserver;

    public SqliteRecoveryService(
        ISqliteDatabaseHealthChecker healthChecker,
        string dataDirectory,
        ILogger logger,
        IRfidTimeProvider timeProvider,
        Action<SqliteRecoveryPhase>? phaseObserver = null)
    {
        _healthChecker = healthChecker ?? throw new ArgumentNullException(nameof(healthChecker));
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("SQLite recovery data directory must not be empty.", nameof(dataDirectory));
        }

        _dataDirectory = Path.GetFullPath(dataDirectory);
        _markerStore = new SqliteRecoveryMarkerStore(_dataDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _phaseObserver = phaseObserver;
    }

    public SqliteRecoveryMarker? ReadMarker() => _markerStore.ReadMarker();

    public SqliteRecoveryResult Recover(
        string productionDatabasePath,
        SqliteBackupCandidate candidate)
    {
        SqliteRecoveryMarker? marker = null;
        try
        {
            marker = _markerStore.ReadMarker();
            if (marker is not null)
            {
                return Failure("An interrupted SQLite recovery already exists; resume it first.", marker);
            }

            var productionPath = GetFullPath(productionDatabasePath, nameof(productionDatabasePath));
            if (candidate is null)
            {
                throw new ArgumentNullException(nameof(candidate));
            }

            var candidatePath = Path.GetFullPath(candidate.Path);
            var candidateHealth = InspectHealthy(candidatePath);
            if (candidateHealth.State != SqliteDatabaseHealthState.Healthy)
            {
                return Failure($"Recovery candidate validation returned {candidateHealth.State}.");
            }

            var stagingPath = Path.Combine(_dataDirectory, "MineRailMonitor.restore.tmp.db");
            PrepareStaging(stagingPath);
            File.Copy(candidatePath, stagingPath, overwrite: false);
            var stagingHealth = InspectHealthy(stagingPath);
            if (stagingHealth.State != SqliteDatabaseHealthState.Healthy)
            {
                return Failure($"Recovery staging validation returned {stagingHealth.State}.");
            }

            Notify(SqliteRecoveryPhase.StagingValidated);

            var startedAt = _timeProvider.UtcNow;
            var bundlePath = SaveCorruptBundle(productionPath, startedAt);
            Notify(SqliteRecoveryPhase.CorruptBundleSaved);

            marker = new SqliteRecoveryMarker(
                candidatePath,
                bundlePath,
                stagingPath,
                startedAt);
            _markerStore.WriteMarker(marker);
            Notify(SqliteRecoveryPhase.RecoveryMarkerPersisted);

            RemoveProductionSidecars(productionPath);
            Notify(SqliteRecoveryPhase.SidecarsRemoved);

            Notify(SqliteRecoveryPhase.ProductionReplacementStarted);
            ReplaceProduction(stagingPath, productionPath);

            var finalHealth = _healthChecker.Inspect(
                productionPath,
                SqliteInspectionMode.FullValidation);
            Notify(SqliteRecoveryPhase.FinalHealthCheckCompleted);
            if (finalHealth.State != SqliteDatabaseHealthState.Healthy)
            {
                return Failure($"Final recovery validation returned {finalHealth.State}.", marker);
            }

            _markerStore.DeleteMarker();
            return new SqliteRecoveryResult(true, marker: marker);
        }
        catch (Exception exception)
        {
            _logger.Error("SQLite recovery failed.", exception);
            return Failure(exception.Message, marker);
        }
    }

    public SqliteRecoveryResult ResumeInterruptedRecovery(
        string productionDatabasePath,
        SqliteRecoveryMarker marker)
    {
        try
        {
            var diskMarker = _markerStore.ReadMarker();
            if (diskMarker is null)
            {
                return Failure("SQLite recovery marker is missing.");
            }

            if (!MarkersMatch(diskMarker, marker))
            {
                return Failure("The supplied SQLite recovery marker does not match the disk marker.", diskMarker);
            }

            var productionPath = GetFullPath(productionDatabasePath, nameof(productionDatabasePath));
            ValidateCorruptBundle(diskMarker.CorruptBundlePath, productionPath);

            var sourceHealth = InspectHealthy(diskMarker.SourceBackupPath);
            if (sourceHealth.State != SqliteDatabaseHealthState.Healthy)
            {
                return Failure($"Recovery source validation returned {sourceHealth.State}.", diskMarker);
            }

            PrepareStaging(diskMarker.StagingPath);
            File.Copy(diskMarker.SourceBackupPath, diskMarker.StagingPath, overwrite: false);
            var stagingHealth = InspectHealthy(diskMarker.StagingPath);
            if (stagingHealth.State != SqliteDatabaseHealthState.Healthy)
            {
                return Failure($"Recovery staging validation returned {stagingHealth.State}.", diskMarker);
            }

            Notify(SqliteRecoveryPhase.StagingValidated);
            RemoveProductionSidecars(productionPath);
            Notify(SqliteRecoveryPhase.SidecarsRemoved);
            Notify(SqliteRecoveryPhase.ProductionReplacementStarted);
            ReplaceProduction(diskMarker.StagingPath, productionPath);

            var finalHealth = _healthChecker.Inspect(
                productionPath,
                SqliteInspectionMode.FullValidation);
            Notify(SqliteRecoveryPhase.FinalHealthCheckCompleted);
            if (finalHealth.State != SqliteDatabaseHealthState.Healthy)
            {
                return Failure($"Final recovery validation returned {finalHealth.State}.", diskMarker);
            }

            _markerStore.DeleteMarker();
            return new SqliteRecoveryResult(true, marker: diskMarker);
        }
        catch (Exception exception)
        {
            _logger.Error("SQLite recovery resume failed.", exception);
            return Failure(exception.Message, marker);
        }
    }

    private SqliteDatabaseHealthResult InspectHealthy(string path)
    {
        var result = _healthChecker.Inspect(path, SqliteInspectionMode.FullValidation);
        return result;
    }

    private string SaveCorruptBundle(string productionPath, DateTimeOffset startedAt)
    {
        if (!File.Exists(productionPath))
        {
            throw new FileNotFoundException("The production SQLite database is required for recovery evidence.", productionPath);
        }

        var corruptRoot = Path.Combine(_dataDirectory, "Corrupt");
        Directory.CreateDirectory(corruptRoot);
        var bundlePath = CreateUniqueBundlePath(corruptRoot, startedAt);
        Directory.CreateDirectory(bundlePath);

        var evidence = new List<BundleEvidence>();
        CopyEvidence(productionPath, bundlePath, evidence, required: true);
        CopyOptionalEvidence(productionPath + "-wal", bundlePath, evidence);
        CopyOptionalEvidence(productionPath + "-shm", bundlePath, evidence);

        var manifestPath = Path.Combine(bundlePath, ManifestFileName);
        var manifest = new BundleManifest { Files = evidence };
        File.WriteAllText(
            manifestPath,
            JsonConvert.SerializeObject(manifest, Formatting.Indented),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        ValidateCorruptBundle(bundlePath, productionPath);
        return bundlePath;
    }

    private static string CreateUniqueBundlePath(string corruptRoot, DateTimeOffset startedAt)
    {
        var baseName = startedAt.ToLocalTime().ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var candidate = Path.Combine(corruptRoot, baseName);
        var suffix = 0;
        while (Directory.Exists(candidate))
        {
            suffix++;
            candidate = Path.Combine(corruptRoot, $"{baseName}_{suffix}");
        }

        return candidate;
    }

    private static void CopyEvidence(
        string sourcePath,
        string bundlePath,
        ICollection<BundleEvidence> evidence,
        bool required)
    {
        if (!File.Exists(sourcePath))
        {
            if (required)
            {
                throw new FileNotFoundException("Required SQLite recovery evidence is missing.", sourcePath);
            }

            return;
        }

        var destinationPath = Path.Combine(bundlePath, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, destinationPath, overwrite: false);
        var source = DescribeEvidence(sourcePath);
        var destination = DescribeEvidence(destinationPath);
        if (source.Length != destination.Length ||
            !string.Equals(source.Sha256, destination.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"SQLite recovery evidence verification failed: {sourcePath}");
        }

        evidence.Add(destination);
    }

    private static void CopyOptionalEvidence(
        string sourcePath,
        string bundlePath,
        ICollection<BundleEvidence> evidence) =>
        CopyEvidence(sourcePath, bundlePath, evidence, required: false);

    private static void ValidateCorruptBundle(string bundlePath, string productionPath)
    {
        if (!Directory.Exists(bundlePath))
        {
            throw new InvalidDataException("SQLite recovery corrupt bundle directory is missing.");
        }

        var manifestPath = Path.Combine(bundlePath, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException("SQLite recovery corrupt bundle manifest is missing.");
        }

        BundleManifest? manifest;
        try
        {
            manifest = JsonConvert.DeserializeObject<BundleManifest>(File.ReadAllText(manifestPath));
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            throw new InvalidDataException("SQLite recovery corrupt bundle manifest is malformed.", exception);
        }

        if (manifest?.Files is null || manifest.Files.Count == 0)
        {
            throw new InvalidDataException("SQLite recovery corrupt bundle manifest has no evidence.");
        }

        var productionFileName = Path.GetFileName(productionPath);
        var expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            productionFileName,
            productionFileName + "-wal",
            productionFileName + "-shm"
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var evidence in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(evidence.FileName) ||
                !string.Equals(Path.GetFileName(evidence.FileName), evidence.FileName, StringComparison.Ordinal) ||
                !expectedNames.Contains(evidence.FileName) ||
                !seen.Add(evidence.FileName) ||
                evidence.Length < 0 ||
                string.IsNullOrWhiteSpace(evidence.Sha256))
            {
                throw new InvalidDataException("SQLite recovery corrupt bundle manifest contains invalid evidence.");
            }

            var evidencePath = Path.Combine(bundlePath, evidence.FileName);
            if (!File.Exists(evidencePath))
            {
                throw new InvalidDataException($"SQLite recovery bundle evidence is missing: {evidence.FileName}");
            }

            var actual = DescribeEvidence(evidencePath);
            if (actual.Length != evidence.Length ||
                !string.Equals(actual.Sha256, evidence.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SQLite recovery bundle evidence hash mismatch: {evidence.FileName}");
            }
        }

        if (!seen.Contains(productionFileName))
        {
            throw new InvalidDataException("SQLite recovery bundle does not contain the production database evidence.");
        }
    }

    private static BundleEvidence DescribeEvidence(string path)
    {
        using var sha256 = SHA256.Create();
        return new BundleEvidence
        {
            FileName = Path.GetFileName(path),
            Length = new FileInfo(path).Length,
            Sha256 = BitConverter.ToString(sha256.ComputeHash(File.ReadAllBytes(path)))
                .Replace("-", string.Empty)
                .ToLowerInvariant()
        };
    }

    private static void PrepareStaging(string stagingPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
        if (File.Exists(stagingPath))
        {
            File.Delete(stagingPath);
        }

        if (File.Exists(stagingPath + "-wal"))
        {
            File.Delete(stagingPath + "-wal");
        }

        if (File.Exists(stagingPath + "-shm"))
        {
            File.Delete(stagingPath + "-shm");
        }
    }

    private static void RemoveProductionSidecars(string productionPath)
    {
        DeleteIfExists(productionPath + "-wal");
        DeleteIfExists(productionPath + "-shm");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void ReplaceProduction(string stagingPath, string productionPath)
    {
        if (File.Exists(productionPath))
        {
            File.Replace(stagingPath, productionPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(stagingPath, productionPath);
        }
    }

    private static bool MarkersMatch(SqliteRecoveryMarker left, SqliteRecoveryMarker right) =>
        right is not null &&
        string.Equals(left.SourceBackupPath, right.SourceBackupPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.CorruptBundlePath, right.CorruptBundlePath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.StagingPath, right.StagingPath, StringComparison.OrdinalIgnoreCase) &&
        left.StartedAt == right.StartedAt;

    private static string GetFullPath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("SQLite recovery path must not be empty.", parameterName);
        }

        return Path.GetFullPath(path);
    }

    private void Notify(SqliteRecoveryPhase phase) => _phaseObserver?.Invoke(phase);

    private static SqliteRecoveryResult Failure(
        string message,
        SqliteRecoveryMarker? marker = null) =>
        new(false, message, marker);

    private sealed class BundleManifest
    {
        [JsonProperty("files")]
        public List<BundleEvidence> Files { get; set; } = new();
    }

    private sealed class BundleEvidence
    {
        [JsonProperty("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonProperty("length")]
        public long Length { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }
}
