using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Interfaces;

public interface IStatisticsService
{
    OverviewStatistics GetOverview(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<string>? stationIds = null);

    IReadOnlyList<VehicleStatisticsItem> GetVehicleStatistics(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<string>? stationIds = null);

    IReadOnlyList<AlarmStatisticsItem> GetAlarmStatistics(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<string>? stationIds = null);

    IReadOnlyList<StatisticsTrendBucket> GetTrend(
        DateTimeOffset start,
        DateTimeOffset end,
        StatisticsBucketSize bucketSize,
        IEnumerable<string>? stationIds = null);

    IReadOnlyList<StatisticsRankingItem> GetAlarmStationRanking(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<string>? stationIds = null,
        int limit = 5);

    IReadOnlyList<StatisticsRankingItem> GetAlarmVehicleRanking(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<string>? stationIds = null,
        int limit = 5);

    IReadOnlyList<StatisticsRankingItem> GetPassageVehicleRanking(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<string>? stationIds = null,
        int limit = 5);

    IReadOnlyList<PassageRecord> GetRecentAlarms(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<string>? stationIds = null,
        int limit = 3);

    IReadOnlyList<PassageRecord> GetRecentExceptions(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<string>? stationIds = null,
        int limit = 8);
}
