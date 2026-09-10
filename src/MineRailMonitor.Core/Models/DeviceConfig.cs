namespace MineRailMonitor.Core.Models;

public sealed class DeviceConfig
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DeviceType Type { get; set; } = DeviceType.RfidReader;

    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// Stable identity of the communication station represented by this map device.
    /// This is intentionally separate from StationId, which identifies the map station.
    /// </summary>
    public string? RfidStationId { get; set; }

    public double CadX { get; set; }

    public double CadY { get; set; }

    public string? ProtocolAddress { get; set; }

    public bool Enabled { get; set; } = true;
}
