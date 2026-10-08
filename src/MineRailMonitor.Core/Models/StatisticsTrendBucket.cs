namespace MineRailMonitor.Core.Models;

public sealed class StatisticsTrendBucket
{
    public StatisticsTrendBucket(
        DateTimeOffset start,
        int normalCount,
        int alarmCount,
        int unverifiedCount)
    {
        Start = start;
        NormalCount = normalCount;
        AlarmCount = alarmCount;
        UnverifiedCount = unverifiedCount;
        TotalCount = normalCount + alarmCount + unverifiedCount;
        var verifiableCount = normalCount + alarmCount;
        AlarmRatePercent = verifiableCount == 0 ? null : alarmCount * 100d / verifiableCount;
    }

    public DateTimeOffset Start { get; }

    public int TotalCount { get; }

    public int NormalCount { get; }

    public int AlarmCount { get; }

    public int UnverifiedCount { get; }

    public double? AlarmRatePercent { get; }
}
