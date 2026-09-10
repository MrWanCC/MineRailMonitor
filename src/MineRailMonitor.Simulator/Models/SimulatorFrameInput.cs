namespace MineRailMonitor.Simulator.Models;

public sealed class SimulatorFrameInput
{
    public byte Address { get; set; }

    public byte[] CommandBytes { get; set; } = new byte[4];

    /// <summary>Fixed physical RFID slot snapshot. Slot position is intentionally preserved.</summary>
    public ushort[] Slots { get; set; } = new ushort[14];

    public ushort EmptySlotValue { get; set; } = 0x0000;

    public byte CrcHigh { get; set; } = 0x00;

    public byte CrcLow { get; set; } = 0x00;
}
