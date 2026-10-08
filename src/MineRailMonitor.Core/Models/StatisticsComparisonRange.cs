namespace MineRailMonitor.Core.Models;

public sealed class StatisticsComparisonRange
{
    public StatisticsComparisonRange(
        StatisticsDateRange current,
        StatisticsDateRange previous,
        string label,
        string comparisonLabel = "较上一周期")
    {
        Current = current ?? throw new ArgumentNullException(nameof(current));
        Previous = previous ?? throw new ArgumentNullException(nameof(previous));
        if (Current.Duration != Previous.Duration)
        {
            throw new ArgumentException("当前周期和对比周期必须等长。", nameof(previous));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("统计范围名称不能为空。", nameof(label));
        }

        Label = label;
        ComparisonLabel = string.IsNullOrWhiteSpace(comparisonLabel) ? "较上一周期" : comparisonLabel;
    }

    public StatisticsDateRange Current { get; }

    public StatisticsDateRange Previous { get; }

    public string Label { get; }

    public string ComparisonLabel { get; }

    public static StatisticsComparisonRange ForPreset(StatisticsPeriod period, DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        var (currentStart, days, label, comparisonLabel) = period switch
        {
            StatisticsPeriod.Today => (today, 1, "今日", "较昨日"),
            StatisticsPeriod.Yesterday => (today.AddDays(-1), 1, "昨日", "较前日"),
            StatisticsPeriod.Last7Days => (today.AddDays(-6), 7, "最近7天", "较前7天"),
            StatisticsPeriod.Last30Days => (today.AddDays(-29), 30, "最近30天", "较前30天"),
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };

        var currentEnd = currentStart.AddDays(days);
        var previousStart = currentStart.AddDays(-days);
        return new StatisticsComparisonRange(
            new StatisticsDateRange(currentStart, currentEnd),
            new StatisticsDateRange(previousStart, currentStart),
            label,
            comparisonLabel);
    }
}
