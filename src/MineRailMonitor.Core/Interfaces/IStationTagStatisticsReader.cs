using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Interfaces;

public interface IStationTagStatisticsReader
{
    StationTagStatisticsQueryResult GetStationTagStatistics(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<string>? stationIds = null);
}
