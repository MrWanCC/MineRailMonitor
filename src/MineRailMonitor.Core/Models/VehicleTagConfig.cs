namespace MineRailMonitor.Core.Models;

public sealed class VehicleTagConfig
{
    public int Position { get; set; }

    public ushort Rfid { get; set; }

    public VehicleTagType Type { get; set; }
}
