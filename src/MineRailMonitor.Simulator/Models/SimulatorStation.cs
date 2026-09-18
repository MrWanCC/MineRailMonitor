namespace MineRailMonitor.Simulator.Models;

public sealed class SimulatorStation
{
    public byte Address { get; set; }

    /// <summary>Fixed 14-slot cache used by the simulated station.</summary>
    public ushort[] Slots { get; set; } = new ushort[14];

    public byte[] CommandBytes { get; set; } = new byte[4];

    public byte CrcHigh { get; set; }

    public byte CrcLow { get; set; }

    /// <summary>Runtime-only communication fault policy for this simulated station.</summary>
    public SimulatorFaultConfiguration FaultConfiguration { get; internal set; } = new(SimulatorFaultMode.Normal);
}
