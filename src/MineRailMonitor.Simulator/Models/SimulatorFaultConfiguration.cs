namespace MineRailMonitor.Simulator.Models;

public sealed class SimulatorFaultConfiguration
{
    public SimulatorFaultConfiguration(SimulatorFaultMode mode, int delayMilliseconds = 0)
    {
        if (!Enum.IsDefined(typeof(SimulatorFaultMode), mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (delayMilliseconds is < 0 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(delayMilliseconds), "故障延迟必须在 0-10000 毫秒之间。");
        }

        Mode = mode;
        DelayMilliseconds = delayMilliseconds;
    }

    public SimulatorFaultMode Mode { get; }

    public int DelayMilliseconds { get; }
}
