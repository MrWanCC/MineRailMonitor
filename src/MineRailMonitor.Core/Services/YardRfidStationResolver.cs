using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public sealed class YardRfidStationResolver : IYardRfidStationResolver
{
    private readonly IReadOnlyList<StationConfig> _yards;
    private readonly IReadOnlyList<RfidStationConfig> _rfidStations;
    private readonly IReadOnlyList<YardRfidStationBindingDiagnostic> _duplicateBindingDiagnostics;

    public YardRfidStationResolver(
        IEnumerable<StationConfig> yards,
        IEnumerable<RfidStationConfig> rfidStations)
    {
        if (yards is null) throw new ArgumentNullException(nameof(yards));
        if (rfidStations is null) throw new ArgumentNullException(nameof(rfidStations));

        _yards = yards.Where(yard => yard is not null).ToArray();
        _rfidStations = rfidStations.Where(station => station is not null).ToArray();
        _duplicateBindingDiagnostics = RfidStationBindingRules
            .FindDuplicateBindings(_yards)
            .GroupBy(
                binding => $"{binding.YardId}\u001F{binding.RfidStationId}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new YardRfidStationBindingDiagnostic(
                    first.YardId,
                    first.RfidStationId,
                    first.DeviceId,
                    $"项目中重复绑定 RFID 基站 {first.RfidStationId}：" +
                    $"{string.Join("、", group.Select(binding => binding.DisplayName))}。");
            })
            .ToArray();
    }

    public YardRfidStationScope Resolve(string yardId)
    {
        if (string.IsNullOrWhiteSpace(yardId))
        {
            throw new ArgumentException("站场编号不能为空。", nameof(yardId));
        }

        var normalizedYardId = yardId.Trim();
        var yard = _yards.FirstOrDefault(item =>
            string.Equals(item.Id?.Trim(), normalizedYardId, StringComparison.OrdinalIgnoreCase));
        if (yard is null)
        {
            return CreateScope(
                normalizedYardId,
                Array.Empty<string>(),
                Array.Empty<YardRfidStationBindingDiagnostic>(),
                Array.Empty<YardRfidStationBindingDiagnostic>(),
                Array.Empty<RfidStationConfig>());
        }

        var ownedStations = _rfidStations
            .Where(station => string.Equals(
                station.YardId?.Trim(),
                normalizedYardId,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var referencedIds = ownedStations.Select(station => station.StationId).ToList();
        var mappedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<YardRfidStationBindingDiagnostic>();

        foreach (var device in yard.Devices ?? Array.Empty<DeviceConfig>())
        {
            if (device is null || device.Type != DeviceType.RfidStation || string.IsNullOrWhiteSpace(device.RfidStationId))
            {
                continue;
            }

            var rfidStationId = device.RfidStationId!.Trim();
            mappedIds.Add(rfidStationId);

            if (FindRfidStation(rfidStationId) is null)
            {
                missing.Add(new YardRfidStationBindingDiagnostic(
                    normalizedYardId,
                    rfidStationId,
                    device.Id,
                    $"站场 {normalizedYardId} 的地图设备 {device.Id} 引用了不存在的 RFID 基站 {rfidStationId}。"));
            }
        }

        var duplicateBindings = _duplicateBindingDiagnostics
            .Where(item => string.Equals(item.YardId, normalizedYardId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.RfidStationId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var uniqueIds = referencedIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var unmappedStations = ownedStations
            .Where(station => !mappedIds.Contains(station.StationId.Trim()))
            .ToArray();
        return CreateScope(normalizedYardId, uniqueIds, missing, duplicateBindings, unmappedStations);
    }

    public YardRfidStationScope ResolveAll() => CreateScope(
        yardId: null,
        _rfidStations.Select(station => station.StationId),
        Array.Empty<YardRfidStationBindingDiagnostic>(),
        _duplicateBindingDiagnostics,
        Array.Empty<RfidStationConfig>(),
        isGlobal: true);

    public YardRfidStationScope ResolveUnbound()
    {
        var unassignedStations = _rfidStations
            .Where(station => string.IsNullOrWhiteSpace(station.YardId))
            .Select(station => station.StationId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return CreateScope(
            yardId: null,
            unassignedStations,
            Array.Empty<YardRfidStationBindingDiagnostic>(),
            _duplicateBindingDiagnostics,
            Array.Empty<RfidStationConfig>(),
            isUnbound: true);
    }

    private YardRfidStationScope CreateScope(
        string? yardId,
        IEnumerable<string> rfidStationIds,
        IEnumerable<YardRfidStationBindingDiagnostic> missingConfigurations,
        IEnumerable<YardRfidStationBindingDiagnostic> duplicateBindings,
        IEnumerable<RfidStationConfig> unmappedStations,
        bool isGlobal = false,
        bool isUnbound = false)
    {
        var ids = rfidStationIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var stations = ids
            .Select(FindRfidStation)
            .Where(station => station is not null)
            .Cast<RfidStationConfig>()
            .ToArray();

        return new YardRfidStationScope(
            yardId,
            isGlobal,
            isUnbound,
            stations.Select(station => station.StationId).ToArray(),
            stations,
            stations.Where(station => !station.Enabled).ToArray(),
            unmappedStations.ToArray(),
            missingConfigurations.ToArray(),
            duplicateBindings.ToArray());
    }

    private RfidStationConfig? FindRfidStation(string rfidStationId) =>
        _rfidStations.FirstOrDefault(station =>
            string.Equals(station.StationId?.Trim(), rfidStationId, StringComparison.OrdinalIgnoreCase));
}
