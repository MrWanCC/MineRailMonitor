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
}
