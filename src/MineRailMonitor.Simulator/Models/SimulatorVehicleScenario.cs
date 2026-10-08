namespace MineRailMonitor.Simulator.Models;

public sealed class SimulatorVehicleScenario
{
    public SimulatorVehicleScenario(
        string name,
        IReadOnlyList<ushort> rfids,
        int? delayBeforeRfidIndex = null,
        TimeSpan? delayBeforeRfid = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Rfids = rfids?.ToArray() ?? throw new ArgumentNullException(nameof(rfids));
        DelayBeforeRfidIndex = delayBeforeRfidIndex;
        DelayBeforeRfid = delayBeforeRfid;
    }

    public string Name { get; }

    public IReadOnlyList<ushort> Rfids { get; }

    public int? DelayBeforeRfidIndex { get; }

    public TimeSpan? DelayBeforeRfid { get; }
}
