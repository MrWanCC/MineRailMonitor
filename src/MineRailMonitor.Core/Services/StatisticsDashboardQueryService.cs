using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public sealed class StatisticsDashboardQueryService
{
    private const int RecentPassageLimit = 10;
    private const int RecentAlarmLimit = 3;
    private const int RecentExceptionLimit = 8;
    private const int RankingLimit = 5;

    private readonly IStatisticsService _statisticsService;
    private readonly IPassageRecordStore _passageRecordStore;

    public StatisticsDashboardQueryService(
        IStatisticsService statisticsService,
        IPassageRecordStore passageRecordStore)
    {
        _statisticsService = statisticsService ?? throw new ArgumentNullException(nameof(statisticsService));
        _passageRecordStore = passageRecordStore ?? throw new ArgumentNullException(nameof(passageRecordStore));
    }

    public StatisticsDashboardQueryResult Query(
        StatisticsComparisonRange range,
        IEnumerable<string>? stationIds)
    {
        if (range is null) throw new ArgumentNullException(nameof(range));
        var scope = stationIds?.Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var current = range.Current;
        var previous = range.Previous;

        var currentOverview = _statisticsService.GetOverview(current.Start, current.End, scope);
        var previousOverview = _statisticsService.GetOverview(previous.Start, previous.End, scope);
        var bucketSize = current.Duration <= TimeSpan.FromDays(1)
            ? StatisticsBucketSize.Hour
            : StatisticsBucketSize.Day;
        var trend = BuildTrend(current, bucketSize, _statisticsService.GetTrend(current.Start, current.End, bucketSize, scope));
        var alarmStatuses = CompleteAlarmStatuses(_statisticsService.GetAlarmStatistics(current.Start, current.End, scope));
        var alarmStations = _statisticsService.GetAlarmStationRanking(current.Start, current.End, scope, RankingLimit)
            .Take(RankingLimit).ToArray();
        var alarmVehicles = _statisticsService.GetAlarmVehicleRanking(current.Start, current.End, scope, RankingLimit)
            .Take(RankingLimit).ToArray();
        var passageVehicles = _statisticsService.GetPassageVehicleRanking(current.Start, current.End, scope, RankingLimit)
            .Take(RankingLimit).ToArray();
        var recentAlarms = _statisticsService.GetRecentAlarms(current.Start, current.End, scope, RecentAlarmLimit)
            .Take(RecentAlarmLimit).ToArray();
        var recentExceptions = _statisticsService.GetRecentExceptions(current.Start, current.End, scope, RecentExceptionLimit)
            .Take(RecentExceptionLimit).ToArray();
        var recentQuery = _passageRecordStore.Query(new PassageQuery
        {
            From = current.Start,
            To = current.End,
            StationIds = scope,
            PageSize = RecentPassageLimit
        });

        return new StatisticsDashboardQueryResult(
            range,
            currentOverview,
            previousOverview,
            bucketSize,
            trend,
            alarmStatuses,
            alarmStations,
            alarmVehicles,
            passageVehicles,
            recentQuery.Items.Take(RecentPassageLimit).ToArray(),
            recentAlarms,
            recentExceptions);
    }

    private static IReadOnlyList<StatisticsTrendBucket> BuildTrend(
        StatisticsDateRange range,
        StatisticsBucketSize bucketSize,
        IReadOnlyList<StatisticsTrendBucket> actual)
    {
        var step = bucketSize == StatisticsBucketSize.Hour ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
        var bucketStart = bucketSize == StatisticsBucketSize.Hour
            ? new DateTimeOffset(range.Start.Year, range.Start.Month, range.Start.Day, range.Start.Hour, 0, 0, range.Start.Offset)
            : new DateTimeOffset(range.Start.Year, range.Start.Month, range.Start.Day, 0, 0, 0, range.Start.Offset);
        var byStart = actual.ToDictionary(bucket => bucket.Start);
        var result = new List<StatisticsTrendBucket>();
        for (var at = bucketStart; at < range.End; at = at.Add(step))
        {
            result.Add(byStart.TryGetValue(at, out var bucket)
                ? bucket
                : new StatisticsTrendBucket(at, 0, 0, 0));
        }

        return result;
    }

    private static IReadOnlyList<AlarmStatisticsItem> CompleteAlarmStatuses(IReadOnlyList<AlarmStatisticsItem> actual)
    {
        var counts = actual.ToDictionary(item => item.Status, item => item.Count);
        return new[]
        {
            AlarmStatus.Unconfirmed,
            AlarmStatus.Acknowledged,
            AlarmStatus.Recovered
        }
        .Select(status => new AlarmStatisticsItem(status, counts.TryGetValue(status, out var count) ? count : 0))
        .ToArray();
    }
}
