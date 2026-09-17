using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

/// <summary>
/// Identifies one explicit RFID station-to-map-point reference.
/// ProtocolAddress is deliberately not part of this identity.
/// </summary>
public sealed class RfidStationBindingLocation
{
    public RfidStationBindingLocation(
        string yardId,
        string yardName,
        string deviceId,
        string deviceName,
        string? rfidStationId)
    {
        YardId = yardId ?? string.Empty;
        YardName = yardName ?? string.Empty;
        DeviceId = deviceId ?? string.Empty;
        DeviceName = deviceName ?? string.Empty;
        RfidStationId = rfidStationId ?? string.Empty;
    }

    public string YardId { get; }

    public string YardName { get; }

    public string DeviceId { get; }

    public string DeviceName { get; }

    public string RfidStationId { get; }

    public string DisplayName =>
        $"{(string.IsNullOrWhiteSpace(YardName) ? YardId : YardName)} / " +
        $"{(string.IsNullOrWhiteSpace(DeviceName) ? DeviceId : DeviceName)}";
}

public sealed class RfidStationBindingValidationResult
{
    private RfidStationBindingValidationResult(
        bool succeeded,
        string message,
        IReadOnlyList<RfidStationBindingLocation> conflicts)
    {
        Succeeded = succeeded;
        Message = message;
        Conflicts = conflicts;
    }

    public bool Succeeded { get; }

    public string Message { get; }

    public IReadOnlyList<RfidStationBindingLocation> Conflicts { get; }

    internal static RfidStationBindingValidationResult Success() =>
        new(true, string.Empty, Array.Empty<RfidStationBindingLocation>());

    internal static RfidStationBindingValidationResult Failure(
        string message,
        IReadOnlyList<RfidStationBindingLocation> conflicts) =>
        new(false, message, conflicts);
}

/// <summary>
/// Centralizes the strict one-to-one binding invariant used by map and settings UI.
/// Legacy duplicate data is only diagnosed; this class never mutates project data.
/// </summary>
public static class RfidStationBindingRules
{
    public static IReadOnlyList<RfidStationBindingLocation> FindBindings(
        IEnumerable<StationConfig> yards,
        string rfidStationId)
    {
        if (yards is null) throw new ArgumentNullException(nameof(yards));

        var normalizedStationId = Normalize(rfidStationId);
        if (normalizedStationId.Length == 0)
        {
            return Array.Empty<RfidStationBindingLocation>();
        }

        return EnumerateBindings(yards)
            .Where(binding => string.Equals(binding.RfidStationId, normalizedStationId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static IReadOnlyList<RfidStationBindingLocation> FindDuplicateBindings(
        IEnumerable<StationConfig> yards)
    {
        if (yards is null) throw new ArgumentNullException(nameof(yards));

        return EnumerateBindings(yards)
            .GroupBy(binding => binding.RfidStationId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToArray();
    }

    public static RfidStationBindingValidationResult ValidateBinding(
        IEnumerable<StationConfig> yards,
        IEnumerable<RfidStationConfig>? rfidStations,
        string yardId,
        string deviceId,
        string? rfidStationId)
    {
        if (yards is null) throw new ArgumentNullException(nameof(yards));

        var normalizedYardId = Normalize(yardId);
        var normalizedDeviceId = Normalize(deviceId);
        var normalizedRfidStationId = Normalize(rfidStationId);
        var yard = yards.FirstOrDefault(item =>
            item is not null && string.Equals(item.Id?.Trim(), normalizedYardId, StringComparison.OrdinalIgnoreCase));
        if (yard is null)
        {
            return RfidStationBindingValidationResult.Failure(
                $"地图站场不存在：{normalizedYardId}。",
                Array.Empty<RfidStationBindingLocation>());
        }

        var device = (yard.Devices ?? Array.Empty<DeviceConfig>()).FirstOrDefault(item =>
            item is not null &&
            item.Type == DeviceType.RfidStation &&
            string.Equals(item.Id?.Trim(), normalizedDeviceId, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            return RfidStationBindingValidationResult.Failure(
                $"地图点位不存在或不是 RFID 基站点位：{normalizedDeviceId}。",
                Array.Empty<RfidStationBindingLocation>());
        }

        if (normalizedRfidStationId.Length == 0)
        {
            return RfidStationBindingValidationResult.Failure(
                "请选择有效的 RFID 基站。",
                Array.Empty<RfidStationBindingLocation>());
        }

        var rfidStation = rfidStations?.FirstOrDefault(item =>
            item is not null &&
            string.Equals(item.StationId?.Trim(), normalizedRfidStationId, StringComparison.OrdinalIgnoreCase));
        if (rfidStations is not null && rfidStation is null)
        {
            return RfidStationBindingValidationResult.Failure(
                $"RFID 基站不存在：{normalizedRfidStationId}。",
                Array.Empty<RfidStationBindingLocation>());
        }

        var ownedYardId = rfidStation?.YardId?.Trim();
        if (ownedYardId is not null &&
            ownedYardId.Length > 0 &&
            !string.Equals(ownedYardId, normalizedYardId, StringComparison.OrdinalIgnoreCase))
        {
            return RfidStationBindingValidationResult.Failure(
                $"RFID 基站“{normalizedRfidStationId}”的通信归属站场为“{ownedYardId}”，" +
                $"不能绑定到地图站场“{normalizedYardId}”。",
                Array.Empty<RfidStationBindingLocation>());
        }

        var currentDeviceStationId = Normalize(device.RfidStationId);
        if (currentDeviceStationId.Length > 0 &&
            !string.Equals(currentDeviceStationId, normalizedRfidStationId, StringComparison.OrdinalIgnoreCase))
        {
            return RfidStationBindingValidationResult.Failure(
                $"地图点位“{DeviceDisplayName(device)}”已绑定 RFID 基站“{currentDeviceStationId}”，请先解绑后再绑定。",
                new[]
                {
                    new RfidStationBindingLocation(
                        yard.Id,
                        yard.Name,
                        device.Id,
                        device.Name,
                        currentDeviceStationId)
                });
        }

        var conflicts = FindBindings(yards, normalizedRfidStationId)
            .Where(binding =>
                !string.Equals(binding.YardId, normalizedYardId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(binding.DeviceId, normalizedDeviceId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (conflicts.Length > 0)
        {
            var locations = string.Join(
                "、",
                conflicts.Select(binding => binding.DisplayName));
            return RfidStationBindingValidationResult.Failure(
                $"RFID 基站“{normalizedRfidStationId}”已绑定其它地图点位：{locations}。请先解绑后再绑定。",
                conflicts);
        }

        return RfidStationBindingValidationResult.Success();
    }

    private static IEnumerable<RfidStationBindingLocation> EnumerateBindings(IEnumerable<StationConfig> yards)
    {
        foreach (var yard in yards.Where(item => item is not null))
        {
            foreach (var device in (yard.Devices ?? Array.Empty<DeviceConfig>()).Where(item =>
                         item is not null &&
                         item.Type == DeviceType.RfidStation &&
                         !string.IsNullOrWhiteSpace(item.RfidStationId)))
            {
                yield return new RfidStationBindingLocation(
                    yard.Id,
                    yard.Name,
                    device.Id,
                    device.Name,
                    Normalize(device.RfidStationId));
            }
        }
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static string DeviceDisplayName(DeviceConfig device) =>
        string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name;
}
