using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Simulator.Models;

public sealed class SimulatorProjectVehicleConfiguration
{
    public SimulatorProjectVehicleConfiguration(string projectName, int? expectedVehicleCount, IReadOnlyList<VehicleConfig> vehicles)
    {
        ProjectName = projectName;
        ExpectedVehicleCount = expectedVehicleCount;
        Vehicles = vehicles;
    }

    public string ProjectName { get; }

    public int? ExpectedVehicleCount { get; }

    public IReadOnlyList<VehicleConfig> Vehicles { get; }
}

public static class SimulatorProjectVehicleLoader
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static SimulatorProjectVehicleConfiguration Load(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ArgumentException("项目配置路径不能为空。", nameof(manifestPath));
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ProjectManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("项目配置内容为空。");
        var vehicles = (manifest.Vehicles ?? new List<VehicleManifest>())
            .Where(vehicle => vehicle.Enabled)
            .Select(vehicle => vehicle.ToConfig())
            .ToArray();
        var errors = VehicleConfigurationRules.Validate(vehicles);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        var expectedCount = manifest.RfidSettings?.ExpectedVehicleCount;
        if (expectedCount is < 1 or > 14)
        {
            expectedCount = null;
        }

        return new SimulatorProjectVehicleConfiguration(
            string.IsNullOrWhiteSpace(manifest.Name) ? "未命名项目" : manifest.Name ?? "未命名项目",
            expectedCount,
            vehicles);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class ProjectManifest
    {
        public string? Name { get; set; }

        public RfidSettings? RfidSettings { get; set; }

        public List<VehicleManifest>? Vehicles { get; set; }
    }

    private sealed class VehicleManifest
    {
        public ushort HeadRfid { get; set; }

        public string? VehicleNo { get; set; }

        public string? VehicleName { get; set; }

        public string? Name { get; set; }

        public bool Enabled { get; set; } = true;

        public List<VehicleTagConfig>? Tags { get; set; }

        public VehicleConfig ToConfig()
        {
            var vehicleNo = VehicleNo?.Trim();
            return new VehicleConfig
            {
                HeadRfid = HeadRfid,
                VehicleNo = string.IsNullOrWhiteSpace(vehicleNo) ? $"RFID-{HeadRfid:X4}" : vehicleNo ?? string.Empty,
                VehicleName = string.IsNullOrWhiteSpace(VehicleName) ? Name ?? string.Empty : VehicleName ?? string.Empty,
                Enabled = Enabled,
                Tags = Tags ?? new List<VehicleTagConfig>()
            };
        }
    }
}
