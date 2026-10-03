namespace MineRailMonitor.Core.Models;

public sealed class AlarmStatisticsItem
{
    public AlarmStatisticsItem(AlarmStatus status, int count)
    {
        Status = status;
        Count = count;
    }

    public AlarmStatus Status { get; }

    public int Count { get; }
}
