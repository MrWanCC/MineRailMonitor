using System.IO;
using System.Text.Json;

namespace MineRailMonitor.Simulator.Models;

public static class SimulatorStationPersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static IReadOnlyList<SimulatorStationConfig> Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Array.Empty<SimulatorStationConfig>();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<SimulatorStationConfig>>(json, JsonOptions)
                ?? new List<SimulatorStationConfig>();
        }
        catch (IOException)
        {
            return Array.Empty<SimulatorStationConfig>();
        }
        catch (JsonException)
        {
            return Array.Empty<SimulatorStationConfig>();
        }
    }

    public static void Save(string path, IEnumerable<SimulatorStationConfig> configs)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Configuration path must not be empty.", nameof(path));
        }
        if (configs is null)
        {
            throw new ArgumentNullException(nameof(configs));
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = fullPath + ".tmp";
        var snapshot = configs.Select(config => config?.Clone() ?? throw new ArgumentException("Configuration collection contains null.", nameof(configs))).ToArray();
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions));

        try
        {
            if (File.Exists(fullPath))
            {
                File.Replace(temporaryPath, fullPath, null);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }
        }
        catch (PlatformNotSupportedException)
        {
            File.Copy(temporaryPath, fullPath, overwrite: true);
            File.Delete(temporaryPath);
        }
    }
}
