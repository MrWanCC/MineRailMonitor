namespace MineRailMonitor.Core.Models;

public sealed class StatisticsDashboardQueryResult
{
    public StatisticsDashboardQueryResult(
        StatisticsComparisonRange range,
        OverviewStatistics currentOverview,
        OverviewStatistics previousOverview,
        StatisticsBucketSize trendBucketSize,
        IReadOnlyList<StatisticsTrendBucket> trend,
        IReadOnlyList<AlarmStatisticsItem> alarmStatuses,
        IReadOnlyList<StatisticsRankingItem> alarmStations,
        IReadOnlyList<StatisticsRankingItem> alarmVehicles,
        IReadOnlyList<StatisticsRankingItem> passageVehicles,
        IReadOnlyList<PassageRecord> recentPassages,
        IReadOnlyList<PassageRecord> recentAlarms,
        IReadOnlyList<PassageRecord> recentExceptions)
    {
        Range = range ?? throw new ArgumentNullException(nameof(range));
        CurrentOverview = currentOverview ?? throw new ArgumentNullException(nameof(currentOverview));
        PreviousOverview = previousOverview ?? throw new ArgumentNullException(nameof(previousOverview));
        TrendBucketSize = trendBucketSize;
        Trend = trend ?? throw new ArgumentNullException(nameof(trend));
        AlarmStatuses = alarmStatuses ?? throw new ArgumentNullException(nameof(alarmStatuses));
        AlarmStations = alarmStations ?? throw new ArgumentNullException(nameof(alarmStations));
        AlarmVehicles = alarmVehicles ?? throw new ArgumentNullException(nameof(alarmVehicles));
        PassageVehicles = passageVehicles ?? throw new ArgumentNullException(nameof(passageVehicles));
        RecentPassages = recentPassages ?? throw new ArgumentNullException(nameof(recentPassages));
        RecentAlarms = recentAlarms ?? throw new ArgumentNullException(nameof(recentAlarms));
        RecentExceptions = recentExceptions ?? throw new ArgumentNullException(nameof(recentExceptions));
    }

    public StatisticsComparisonRange Range { get; }

    public OverviewStatistics CurrentOverview { get; }

    public OverviewStatistics PreviousOverview { get; }

    public StatisticsBucketSize TrendBucketSize { get; }

    public IReadOnlyList<StatisticsTrendBucket> Trend { get; }

    public IReadOnlyList<AlarmStatisticsItem> AlarmStatuses { get; }

    public IReadOnlyList<StatisticsRankingItem> AlarmStations { get; }

    public IReadOnlyList<StatisticsRankingItem> AlarmVehicles { get; }

    public IReadOnlyList<StatisticsRankingItem> PassageVehicles { get; }

    public IReadOnlyList<PassageRecord> RecentPassages { get; }

    public IReadOnlyList<PassageRecord> RecentAlarms { get; }

    public IReadOnlyList<PassageRecord> RecentExceptions { get; }
}
