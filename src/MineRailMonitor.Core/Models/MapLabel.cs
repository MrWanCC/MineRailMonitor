namespace MineRailMonitor.Core.Models;

public sealed class MapLabel
{
    public string Id { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public double CadX { get; set; }

    public double CadY { get; set; }

    public double Rotation { get; set; }

    public double TextHeight { get; set; }

    public bool Enabled { get; set; } = true;
}
