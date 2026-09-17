using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Pages;

internal static class RfidYardFilter
{
    public static IReadOnlyList<RfidStationYardOption> BuildOptions(
        IEnumerable<StationConfig>? yards,
        IEnumerable<RfidStationConfig>? stations)
    {
        var options = (yards ?? Array.Empty<StationConfig>())
            .Where(yard => yard is not null && !string.IsNullOrWhiteSpace(yard.Id))
            .Select(yard => new RfidStationYardOption(yard.Id.Trim(), yard.Name))
            .Concat((stations ?? Array.Empty<RfidStationConfig>())
                .Where(station => station is not null && !string.IsNullOrWhiteSpace(station.YardId))
                .Select(station =>
                {
                    var yardId = station.YardId!.Trim();
                    return new RfidStationYardOption(yardId, $"{yardId} 站场");
                }))
            .GroupBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new[] { new RfidStationYardOption(RfidStationYardOption.AllId, "全部站场") }
            .Concat(options)
            .Concat(new[] { new RfidStationYardOption(RfidStationYardOption.UnboundId, "未归属") })
            .ToArray();
    }

    public static HashSet<string>? ResolveStationIds(
        string? yardId,
        IEnumerable<RfidStationConfig> stations)
    {
        if (string.IsNullOrWhiteSpace(yardId) ||
            string.Equals(yardId, RfidStationYardOption.AllId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var isUnbound = string.Equals(yardId, RfidStationYardOption.UnboundId, StringComparison.OrdinalIgnoreCase);
        return stations
            .Where(station => station is not null && !string.IsNullOrWhiteSpace(station.StationId))
            .Where(station => isUnbound
                ? string.IsNullOrWhiteSpace(station.YardId)
                : string.Equals(station.YardId?.Trim(), yardId!.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(station => station.StationId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static string? ResolveYardId(
        IEnumerable<string>? stationIds,
        IEnumerable<RfidStationConfig> stations)
    {
        if (stationIds is null)
        {
            return RfidStationYardOption.AllId;
        }

        var scope = stationIds
            .Where(stationId => !string.IsNullOrWhiteSpace(stationId))
            .Select(stationId => stationId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var yardIds = stations
            .Where(station => scope.Contains(station.StationId.Trim()))
            .Select(station => station.YardId?.Trim())
            .Where(yardId => !string.IsNullOrWhiteSpace(yardId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return yardIds.Length == 1
            ? yardIds[0]
            : scope.Count > 0 && stations.Any(station =>
                scope.Contains(station.StationId.Trim()) && string.IsNullOrWhiteSpace(station.YardId))
                ? RfidStationYardOption.UnboundId
                : RfidStationYardOption.AllId;
    }
}
