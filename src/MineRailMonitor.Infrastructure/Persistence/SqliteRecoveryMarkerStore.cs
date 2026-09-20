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
        if (!File.Exists(MarkerPath))
        {
            return null;
        }

        string json;
        try
        {
            json = File.ReadAllText(MarkerPath, Encoding.UTF8);
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
        if (File.Exists(MarkerPath))
        {
            File.Delete(MarkerPath);
        }
    }

    private static void ValidateMarker(SqliteRecoveryMarker? marker)
    {
        if (marker is null ||
            !marker.RecoveryStarted ||
            marker.StartedAt == default ||
            !IsAbsolutePath(marker.SourceBackupPath) ||
            !IsAbsolutePath(marker.CorruptBundlePath) ||
            !IsAbsolutePath(marker.StagingPath))
        {
            throw new InvalidDataException("SQLite recovery marker is missing required fields.");
        }
    }

    private static bool IsAbsolutePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path);
}
