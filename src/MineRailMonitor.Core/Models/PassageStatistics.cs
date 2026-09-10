namespace MineRailMonitor.Core.Models;

public sealed class PassageStatistics
{
    public PassageStatistics(
        int todayPassageCount,
        int todayNormalCount,
        int todayAlarmCount,
        IReadOnlyDictionary<string, int> byStation)
    {
        TodayPassageCount = todayPassageCount;
        TodayNormalCount = todayNormalCount;
        TodayAlarmCount = todayAlarmCount;
        ByStation = byStation ?? throw new ArgumentNullException(nameof(byStation));
    }

    public int TodayPassageCount { get; }

    public int TodayNormalCount { get; }

    public int TodayAlarmCount { get; }

    public IReadOnlyDictionary<string, int> ByStation { get; }
}
