using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

/// <summary>
/// Validates the explicit station-to-yard ownership metadata without changing
/// legacy map bindings. A station may be owned by a yard without a map point,
/// but an existing map binding must be in that same yard.
/// </summary>
public static class RfidStationOwnershipRules
{
    public static IReadOnlyList<string> Validate(
        IEnumerable<StationConfig> yards,
        IEnumerable<RfidStationConfig> stations)
    {
        if (yards is null) throw new ArgumentNullException(nameof(yards));
        if (stations is null) throw new ArgumentNullException(nameof(stations));

        var yardList = yards.Where(yard => yard is not null).ToArray();
        var errors = new List<string>();

        foreach (var station in stations.Where(item => item is not null))
        {
            var stationId = station.StationId?.Trim() ?? string.Empty;
            var yardId = station.YardId?.Trim() ?? string.Empty;
            if (yardId.Length == 0)
            {
                continue;
            }

            var yard = yardList.FirstOrDefault(item =>
                string.Equals(item.Id?.Trim(), yardId, StringComparison.OrdinalIgnoreCase));
            if (yard is null)
            {
                errors.Add($"RFID 基站“{stationId}”所属站场不存在：{yardId}。请先选择有效站场。");
                continue;
            }

            var bindings = RfidStationBindingRules.FindBindings(yardList, stationId);
            var otherYardBindings = bindings
                .Where(binding => !string.Equals(
                    binding.YardId?.Trim(), yardId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (otherYardBindings.Length == 0)
            {
                continue;
            }

            var locations = string.Join(
                "、",
                otherYardBindings.Select(binding =>
                    $"{binding.YardName} / {binding.DeviceName}"));
            errors.Add(
                $"RFID 基站“{stationId}”配置所属站场为“{yard.Name}（{yardId}）”，" +
                $"但地图已绑定到其它站场：{locations}。请先解绑后再保存。");
        }

        return errors;
    }
}
