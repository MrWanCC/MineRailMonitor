using System.Text;
using Newtonsoft.Json;

namespace MineRailMonitor.Infrastructure.Persistence;

public sealed class SqliteRecoveryMarkerStore
{
    public SqliteRecoveryMarkerStore(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("SQLite recovery data directory must not be empty.", nameof(dataDirectory));
        }

        DataDirectory = Path.GetFullPath(dataDirectory);
        MarkerPath = Path.Combine(DataDirectory, ".sqlite-recovery-in-progress");
        TemporaryMarkerPath = MarkerPath + ".tmp";
    }

    public string DataDirectory { get; }

    public string MarkerPath { get; }

    public string TemporaryMarkerPath { get; }

    public SqliteRecoveryMarker? ReadMarker()
    {
        string json;
        try
        {
            using var stream = new FileStream(
                MarkerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            json = reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("SQLite recovery marker cannot be read.", exception);
        }

        try
        {
            var marker = JsonConvert.DeserializeObject<SqliteRecoveryMarker>(json);
            ValidateMarker(marker);
            return marker!;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException)
        {
            throw new InvalidDataException("SQLite recovery marker is malformed.", exception);
        }
    }

    public void WriteMarker(SqliteRecoveryMarker marker)
    {
        ValidateMarker(marker);
        Directory.CreateDirectory(DataDirectory);

        if (File.Exists(MarkerPath))
        {
            throw new IOException("SQLite recovery marker already exists.");
        }

        if (File.Exists(TemporaryMarkerPath))
        {
            throw new IOException("SQLite recovery marker temporary file already exists.");
        }

        var json = JsonConvert.SerializeObject(
            marker,
            Formatting.Indented,
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

        using (var stream = new FileStream(
                   TemporaryMarkerPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   4096,
                   FileOptions.WriteThrough))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(true);
        }

        if (File.Exists(MarkerPath))
        {
            throw new IOException("SQLite recovery marker appeared during persistence.");
        }

        File.Move(TemporaryMarkerPath, MarkerPath);
    }

    public void DeleteMarker()
    {
        File.Delete(MarkerPath);
    }

    private void ValidateMarker(SqliteRecoveryMarker? marker)
    {
        if (marker is null ||
            !marker.RecoveryStarted ||
            marker.StartedAt == default ||
            !IsAbsolutePath(marker.SourceBackupPath) ||
            !IsStagingPathInScope(marker.StagingPath) ||
            !IsBundlePathInScope(marker.CorruptBundlePath) ||
            !IsSha256(marker.BundleManifestSha256))
        {
            throw new InvalidDataException("SQLite recovery marker is missing required fields.");
        }
    }

    private static bool IsAbsolutePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path);

    private bool IsStagingPathInScope(string? path) =>
        IsAbsolutePath(path) &&
        PathEquals(
            Path.GetFullPath(path!),
            Path.Combine(DataDirectory, "MineRailMonitor.restore.tmp.db"));

    private bool IsBundlePathInScope(string? path)
    {
        if (!IsAbsolutePath(path))
        {
            return false;
        }

        var bundlePath = Path.GetFullPath(path!);
        var corruptRoot = Path.GetFullPath(Path.Combine(DataDirectory, "Corrupt"));
        var parent = Path.GetDirectoryName(bundlePath);
        return parent is not null &&
               PathEquals(parent, corruptRoot) &&
               !PathEquals(bundlePath, corruptRoot);
    }

    private static bool IsSha256(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        return value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
}
