namespace MineRailMonitor.Core.Models;

public sealed class VehicleConfig
{
    public string VehicleNo { get; set; } = string.Empty;

    public ushort HeadRfid { get; set; }

    public string VehicleName { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string Name
    {
        get => VehicleName;
        set => VehicleName = value ?? string.Empty;
    }

    public bool Enabled { get; set; } = true;

    public List<VehicleTagConfig> Tags { get; set; } = new();

    public int TotalSectionCount => Tags?.Count ?? 0;
}
