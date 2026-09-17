using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public static class RfidStationConfigurationRules
{
    public static IReadOnlyList<string> FindDuplicateStationIds(
        IEnumerable<RfidStationConfig> stations)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));

        return stations
            .Where(station => station is not null && !string.IsNullOrWhiteSpace(station.StationId))
            .GroupBy(station => station.StationId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
    }

    public static IReadOnlyList<RfidStationConfig> TakeFirstByStationId(
        IEnumerable<RfidStationConfig> stations)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));

        return stations
            .Where(station => station is not null)
            .GroupBy(station => station.StationId?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }
}
