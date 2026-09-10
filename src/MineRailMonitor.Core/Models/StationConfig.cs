namespace MineRailMonitor.Core.Models;

public sealed class StationConfig
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string BackgroundImage { get; set; } = string.Empty;

    public double CadMinX { get; set; }

    public double CadMaxX { get; set; }

    public double CadMinY { get; set; }

    public double CadMaxY { get; set; }

    public IReadOnlyList<MapPoint> Points { get; set; } = new List<MapPoint>();

    public IReadOnlyList<MapLabel> Labels { get; set; } = new List<MapLabel>();

    public IReadOnlyList<DeviceConfig> Devices { get; set; } = new List<DeviceConfig>();
}
