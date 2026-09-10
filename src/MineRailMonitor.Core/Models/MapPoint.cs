namespace MineRailMonitor.Core.Models;

public sealed class MapPoint
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public double CadX { get; set; }

    public double CadY { get; set; }

    public bool Enabled { get; set; } = true;
}
