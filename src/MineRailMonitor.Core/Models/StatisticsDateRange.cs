namespace MineRailMonitor.Core.Models;

public sealed class StatisticsDateRange
{
    public StatisticsDateRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "统计结束时间必须晚于开始时间。");
        }

        Start = start;
        End = end;
    }

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }

    public TimeSpan Duration => End - Start;
}
