using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class StatisticsDashboardQueryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 14, 35, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Query_keeps_an_empty_station_scope_for_every_aggregate_and_recent_query()
    {
        var statistics = new RecordingStatisticsService();
        var passages = new RecordingPassageRecordStore();
        var service = new StatisticsDashboardQueryService(statistics, passages);
        var range = StatisticsComparisonRange.ForPreset(StatisticsPeriod.Today, Now);

        service.Query(range, Array.Empty<string>());

        Assert.Collection(statistics.OverviewScopes, _ => { }, _ => { });
        Assert.All(statistics.AllStationScopes, scope =>
        {
            Assert.NotNull(scope);
            Assert.Empty(scope!);
        });
        Assert.Empty(Assert.Single(passages.Queries).StationIds!);
        Assert.Contains(statistics.OverviewRanges, item => item.Start == range.Current.Start && item.End == range.Current.End);
        Assert.Contains(statistics.OverviewRanges, item => item.Start == range.Previous.Start && item.End == range.Previous.End);
    }

    [Fact]
    public void Query_pads_hourly_trend_to_all_24_hours_and_daily_trend_to_all_days()
    {
        var statistics = new RecordingStatisticsService();
        statistics.Trend = new[]
        {
            new StatisticsTrendBucket(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(8)), 2, 1, 0)
        };
        var service = new StatisticsDashboardQueryService(statistics, new RecordingPassageRecordStore());

        var today = service.Query(StatisticsComparisonRange.ForPreset(StatisticsPeriod.Today, Now), null);
        var sevenDays = service.Query(StatisticsComparisonRange.ForPreset(StatisticsPeriod.Last7Days, Now), null);

        Assert.Equal(24, today.Trend.Count);
        Assert.Equal(7, sevenDays.Trend.Count);
        Assert.Equal(0, today.Trend[0].TotalCount);
        Assert.Null(today.Trend[0].AlarmRatePercent);
        Assert.Equal(3, today.Trend[9].TotalCount);
        Assert.Contains(StatisticsBucketSize.Hour, statistics.BucketSizes);
        Assert.Contains(StatisticsBucketSize.Day, statistics.BucketSizes);
    }

    [Fact]
    public void Query_returns_all_alarm_statuses_even_when_a_status_has_zero_records()
    {
        var statistics = new RecordingStatisticsService
        {
            AlarmStatistics = new[] { new AlarmStatisticsItem(AlarmStatus.Unconfirmed, 2) }
        };
        var service = new StatisticsDashboardQueryService(statistics, new RecordingPassageRecordStore());

        var result = service.Query(StatisticsComparisonRange.ForPreset(StatisticsPeriod.Today, Now), null);

        Assert.Equal(3, result.AlarmStatuses.Count);
        Assert.Equal(2, Assert.Single(result.AlarmStatuses, item => item.Status == AlarmStatus.Unconfirmed).Count);
        Assert.Equal(0, Assert.Single(result.AlarmStatuses, item => item.Status == AlarmStatus.Acknowledged).Count);
        Assert.Equal(0, Assert.Single(result.AlarmStatuses, item => item.Status == AlarmStatus.Recovered).Count);
    }

    [Fact]
    public void Query_returns_today_and_yesterday_overviews_as_the_comparison_pair()
    {
        var current = new OverviewStatistics(128, 120, 6, 31.5, 2);
        var previous = new OverviewStatistics(116, 108, 8, 29.4, 0);
        var statistics = new RecordingStatisticsService
        {
            CurrentOverview = current,
            PreviousOverview = previous
        };
        var service = new StatisticsDashboardQueryService(statistics, new RecordingPassageRecordStore());

        var result = service.Query(StatisticsComparisonRange.ForPreset(StatisticsPeriod.Today, Now), null);

        Assert.Same(current, result.CurrentOverview);
        Assert.Same(previous, result.PreviousOverview);
        Assert.Contains(statistics.OverviewRanges, range => range.Start == new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(8)));
        Assert.Contains(statistics.OverviewRanges, range => range.Start == new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.FromHours(8)));
    }

    [Fact]
    public void Query_limits_recent_passages_to_ten_and_queries_the_requested_dashboard_sections()
    {
        var statistics = new RecordingStatisticsService();
        var passages = new RecordingPassageRecordStore
        {
            ResultCount = 14
        };
        var service = new StatisticsDashboardQueryService(statistics, passages);

        var result = service.Query(StatisticsComparisonRange.ForPreset(StatisticsPeriod.Today, Now), null);

        Assert.Equal(10, Assert.Single(passages.Queries).PageSize);
        Assert.Equal(10, result.RecentPassages.Count);
        Assert.Equal(3, statistics.LastRecentAlarmLimit);
        Assert.Equal(8, statistics.LastRecentExceptionLimit);
        Assert.Collection(
            statistics.RankingLimits,
            limit => Assert.Equal(5, limit),
            limit => Assert.Equal(5, limit),
            limit => Assert.Equal(5, limit));
    }

    private sealed class RecordingStatisticsService : IStatisticsService
    {
        public List<(DateTimeOffset Start, DateTimeOffset End)> OverviewRanges { get; } = new();
        public List<IReadOnlyList<string>?> OverviewScopes { get; } = new();
        public List<IReadOnlyList<string>?> AllStationScopes { get; } = new();
        public List<int> RankingLimits { get; } = new();
        public OverviewStatistics CurrentOverview { get; set; } = new(0, 0, 0);
        public OverviewStatistics PreviousOverview { get; set; } = new(0, 0, 0);
        public IReadOnlyList<StatisticsTrendBucket> Trend { get; set; } = Array.Empty<StatisticsTrendBucket>();
        public IReadOnlyList<AlarmStatisticsItem> AlarmStatistics { get; set; } = Array.Empty<AlarmStatisticsItem>();
        public List<StatisticsBucketSize> BucketSizes { get; } = new();
        public int LastRecentAlarmLimit { get; private set; }
        public int LastRecentExceptionLimit { get; private set; }

        public OverviewStatistics GetOverview(DateTimeOffset start, DateTimeOffset end, IEnumerable<string>? stationIds = null)
        {
            OverviewRanges.Add((start, end));
            OverviewScopes.Add(stationIds?.ToArray());
            AllStationScopes.Add(stationIds?.ToArray());
            return OverviewRanges.Count == 1 ? CurrentOverview : PreviousOverview;
        }

        public IReadOnlyList<VehicleStatisticsItem> GetVehicleStatistics(DateTimeOffset start, DateTimeOffset end, IEnumerable<string>? stationIds = null) =>
            Array.Empty<VehicleStatisticsItem>();

        public IReadOnlyList<AlarmStatisticsItem> GetAlarmStatistics(DateTimeOffset start, DateTimeOffset end, IEnumerable<string>? stationIds = null)
        {
            AllStationScopes.Add(stationIds?.ToArray());
            return AlarmStatistics;
        }

        public IReadOnlyList<StatisticsTrendBucket> GetTrend(DateTimeOffset start, DateTimeOffset end, StatisticsBucketSize bucketSize, IEnumerable<string>? stationIds = null)
        {
            BucketSizes.Add(bucketSize);
            AllStationScopes.Add(stationIds?.ToArray());
            return Trend;
        }

        public IReadOnlyList<StatisticsRankingItem> GetAlarmStationRanking(DateTimeOffset start, DateTimeOffset end, IEnumerable<string>? stationIds = null, int limit = 5)
        {
            RankingLimits.Add(limit);
            AllStationScopes.Add(stationIds?.ToArray());
            return Array.Empty<StatisticsRankingItem>();
        }

        public IReadOnlyList<StatisticsRankingItem> GetAlarmVehicleRanking(DateTimeOffset start, DateTimeOffset end, IEnumerable<string>? stationIds = null, int limit = 5)
        {
            RankingLimits.Add(limit);
            AllStationScopes.Add(stationIds?.ToArray());
            return Array.Empty<StatisticsRankingItem>();
        }

        public IReadOnlyList<StatisticsRankingItem> GetPassageVehicleRanking(DateTimeOffset start, DateTimeOffset end, IEnumerable<string>? stationIds = null, int limit = 5)
        {
            RankingLimits.Add(limit);
            AllStationScopes.Add(stationIds?.ToArray());
            return Array.Empty<StatisticsRankingItem>();
        }

        public IReadOnlyList<PassageRecord> GetRecentAlarms(DateTimeOffset start, DateTimeOffset end, IEnumerable<string>? stationIds = null, int limit = 3)
        {
            LastRecentAlarmLimit = limit;
            AllStationScopes.Add(stationIds?.ToArray());
            return Array.Empty<PassageRecord>();
        }

        public IReadOnlyList<PassageRecord> GetRecentExceptions(DateTimeOffset start, DateTimeOffset end, IEnumerable<string>? stationIds = null, int limit = 8)
        {
            LastRecentExceptionLimit = limit;
            AllStationScopes.Add(stationIds?.ToArray());
            return Array.Empty<PassageRecord>();
        }
    }

    private sealed class RecordingPassageRecordStore : IPassageRecordStore
    {
        public int ResultCount { get; set; }
        public List<PassageQuery> Queries { get; } = new();
        public IReadOnlyList<PassageRecord> Records => Array.Empty<PassageRecord>();
        public void Save(PassageRecord record) => throw new NotSupportedException();
        public void Add(PassageRecord record) => throw new NotSupportedException();
        public void MarkCleared(Guid passageId, DateTimeOffset clearedAt) => throw new NotSupportedException();
        public void MarkAlarmAcknowledged(Guid passageId, DateTimeOffset acknowledgedAt) => throw new NotSupportedException();
        public IReadOnlyList<PassageRecord> GetPendingClear() => throw new NotSupportedException();
        public IReadOnlyList<PassageRecord> GetUnacknowledgedAlarms() => throw new NotSupportedException();

        public PassageQueryResult Query(PassageQuery query)
        {
            Queries.Add(query);
            var items = Enumerable.Range(0, Math.Min(ResultCount, query.PageSize))
                .Select(index => CreateRecord(Now.AddMinutes(-index), index))
                .ToArray();
            return new PassageQueryResult(items, ResultCount, query.PageIndex, query.PageSize);
        }

        public PassageRecord? GetDetails(Guid passageId) => throw new NotSupportedException();
        public PassageStatistics GetStatistics(DateTimeOffset localNow, IEnumerable<string>? stationIds = null) => throw new NotSupportedException();

        private static PassageRecord CreateRecord(DateTimeOffset at, int index) => new(
            Guid.NewGuid(),
            "RFID-01",
            1,
            1,
            new ushort[] { 1, 17 },
            2,
            PassageOutcome.Completed,
            at.AddSeconds(-5),
            at,
            vehicleNo: $"V{index:00}");
    }
}
