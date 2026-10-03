using System.Globalization;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public static class ConfigurationOperationRecordFormatter
{
    public static IReadOnlyList<OperationRecord> GetVehicleChanges(
        IReadOnlyList<VehicleConfig> before,
        IReadOnlyList<VehicleConfig> after)
    {
        if (before is null) throw new ArgumentNullException(nameof(before));
        if (after is null) throw new ArgumentNullException(nameof(after));

        var unmatchedPrevious = before.Where(item => item is not null).ToList();
        var records = new List<OperationRecord>();

        foreach (var vehicle in after.Where(item => item is not null))
        {
            var vehicleNo = vehicle.VehicleNo?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(vehicleNo))
            {
                continue;
            }

            var previous = unmatchedPrevious.FirstOrDefault(item =>
                string.Equals(item.VehicleNo?.Trim(), vehicleNo, StringComparison.OrdinalIgnoreCase));
            previous ??= unmatchedPrevious.FirstOrDefault(item => item.HeadRfid == vehicle.HeadRfid);
            if (previous is null)
            {
                records.Add(CreateRecord(
                    "新增车辆配置",
                    "车辆",
                    vehicleNo,
                    null,
                    FormatVehicleSnapshot(vehicle)));
                continue;
            }

            unmatchedPrevious.Remove(previous);
            var changedFields = GetVehicleFieldChanges(previous, vehicle);
            if (changedFields.Count == 0)
            {
                continue;
            }

            records.Add(CreateRecord(
                "修改车辆配置",
                "车辆",
                vehicleNo,
                string.Join(Environment.NewLine, changedFields.Select(item => $"{item.Label}: {item.Before}")),
                string.Join(Environment.NewLine, changedFields.Select(item => $"{item.Label}: {item.After}"))));
        }

        foreach (var previous in unmatchedPrevious)
        {
            var vehicleNo = previous.VehicleNo?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(vehicleNo))
            {
                continue;
            }

            records.Add(CreateRecord(
                "删除车辆配置",
                "车辆",
                vehicleNo,
                FormatVehicleSnapshot(previous),
                null));
        }

        return records;
    }

    public static IReadOnlyList<OperationRecord> GetRfidSettingsChanges(RfidSettings before, RfidSettings after)
    {
        if (before is null) throw new ArgumentNullException(nameof(before));
        if (after is null) throw new ArgumentNullException(nameof(after));

        var changedFields = new List<(string Label, string Before, string After)>();
        AddChange(changedFields, "轮询周期", $"{before.PollIntervalMs}ms", $"{after.PollIntervalMs}ms");
        AddChange(changedFields, "标准列车节数", $"{before.ExpectedVehicleCount}节", $"{after.ExpectedVehicleCount}节");
        AddChange(changedFields, "相邻车辆识别超时", $"{before.InterVehicleTimeoutSeconds}秒", $"{after.InterVehicleTimeoutSeconds}秒");
        AddChange(
            changedFields,
            "空槽RFID",
            before.EmptyRfidValue.ToString("X2", CultureInfo.InvariantCulture),
            after.EmptyRfidValue.ToString("X2", CultureInfo.InvariantCulture));

        return changedFields.Count == 0
            ? Array.Empty<OperationRecord>()
            : new[]
            {
                CreateRecord(
                    "修改RFID参数",
                    "RFID参数",
                    "RFID识别参数",
                    string.Join(Environment.NewLine, changedFields.Select(item => $"{item.Label}: {item.Before}")),
                    string.Join(Environment.NewLine, changedFields.Select(item => $"{item.Label}: {item.After}")))
            };
    }

    public static IReadOnlyList<OperationRecord> GetStationChanges(
        IReadOnlyList<RfidStationConfig> before,
        IReadOnlyList<RfidStationConfig> after)
    {
        if (before is null) throw new ArgumentNullException(nameof(before));
        if (after is null) throw new ArgumentNullException(nameof(after));

        var previousById = before
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.StationId))
            .ToDictionary(item => item.StationId.Trim(), StringComparer.OrdinalIgnoreCase);
        var nextIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var records = new List<OperationRecord>();

        foreach (var station in after.Where(item => item is not null))
        {
            var stationId = station.StationId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(stationId))
            {
                continue;
            }

            nextIds.Add(stationId);
            var targetName = GetStationTargetName(station);
            if (!previousById.TryGetValue(stationId, out var previous))
            {
                records.Add(CreateRecord(
                    "新增基站配置",
                    "基站",
                    targetName,
                    null,
                    FormatStationSnapshot(station)));
                continue;
            }

            var changedFields = GetStationFieldChanges(previous, station);
            if (changedFields.Count == 0)
            {
                continue;
            }

            records.Add(CreateRecord(
                "修改基站配置",
                "基站",
                targetName,
                string.Join(Environment.NewLine, changedFields.Select(item => $"{item.Label}: {item.Before}")),
                string.Join(Environment.NewLine, changedFields.Select(item => $"{item.Label}: {item.After}"))));
        }

        foreach (var previous in before.Where(item => item is not null))
        {
            var stationId = previous.StationId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(stationId) || nextIds.Contains(stationId))
            {
                continue;
            }

            records.Add(CreateRecord(
                "删除基站配置",
                "基站",
                GetStationTargetName(previous),
                FormatStationSnapshot(previous),
                null));
        }

        return records;
    }

    private static List<(string Label, string Before, string After)> GetVehicleFieldChanges(
        VehicleConfig before,
        VehicleConfig after)
    {
        var changedFields = new List<(string Label, string Before, string After)>();
        AddChange(changedFields, "车辆编号", before.VehicleNo, after.VehicleNo);
        AddChange(changedFields, "车辆名称", before.VehicleName, after.VehicleName);
        AddChange(changedFields, "车头RFID", FormatRfid(before.HeadRfid), FormatRfid(after.HeadRfid));
        AddChange(changedFields, "总节数", $"{before.TotalSectionCount}节", $"{after.TotalSectionCount}节");
        AddChange(changedFields, "状态", FormatEnabled(before.Enabled), FormatEnabled(after.Enabled));
        var previousTags = FormatVehicleTags(before);
        var nextTags = FormatVehicleTags(after);
        AddChange(changedFields, "标签编组", previousTags, nextTags);
        return changedFields;
    }

    private static List<(string Label, string Before, string After)> GetStationFieldChanges(
        RfidStationConfig before,
        RfidStationConfig after)
    {
        var changedFields = new List<(string Label, string Before, string After)>();
        AddChange(changedFields, "基站名称", before.Name, after.Name);
        AddChange(changedFields, "站场", before.YardId ?? string.Empty, after.YardId ?? string.Empty);
        AddChange(changedFields, "IP地址", before.IpAddress, after.IpAddress);
        AddChange(changedFields, "端口", before.Port.ToString(CultureInfo.InvariantCulture), after.Port.ToString(CultureInfo.InvariantCulture));
        AddChange(
            changedFields,
            "协议地址",
            before.ProtocolAddress.ToString("X2", CultureInfo.InvariantCulture),
            after.ProtocolAddress.ToString("X2", CultureInfo.InvariantCulture));
        AddChange(
            changedFields,
            "标准列车节数",
            before.ExpectedVehicleCount?.ToString(CultureInfo.InvariantCulture) ?? "全局默认",
            after.ExpectedVehicleCount?.ToString(CultureInfo.InvariantCulture) ?? "全局默认");
        AddChange(changedFields, "状态", FormatEnabled(before.Enabled), FormatEnabled(after.Enabled));
        return changedFields;
    }

    private static string FormatVehicleSnapshot(VehicleConfig vehicle) => string.Join(Environment.NewLine, new[]
    {
        $"车辆编号: {vehicle.VehicleNo}",
        $"车辆名称: {vehicle.VehicleName}",
        $"车头RFID: {FormatRfid(vehicle.HeadRfid)}",
        $"总节数: {vehicle.TotalSectionCount}节",
        $"状态: {FormatEnabled(vehicle.Enabled)}",
        $"标签编组: {FormatVehicleTags(vehicle)}"
    });

    private static string FormatStationSnapshot(RfidStationConfig station) => string.Join(Environment.NewLine, new[]
    {
        $"基站名称: {station.Name}",
        $"站场: {station.YardId ?? string.Empty}",
        $"IP地址: {station.IpAddress}",
        $"端口: {station.Port.ToString(CultureInfo.InvariantCulture)}",
        $"协议地址: {station.ProtocolAddress.ToString("X2", CultureInfo.InvariantCulture)}",
        $"标准列车节数: {station.ExpectedVehicleCount?.ToString(CultureInfo.InvariantCulture) ?? "全局默认"}",
        $"状态: {FormatEnabled(station.Enabled)}"
    });

    private static string FormatVehicleTags(VehicleConfig vehicle) => string.Join(
        ", ",
        (vehicle.Tags ?? new List<VehicleTagConfig>())
            .OrderBy(item => item.Position)
            .Select(item => $"位置{item.Position}={FormatRfid(item.Rfid)}({(item.Type == VehicleTagType.Head ? "车头" : "车节")})"));

    private static string GetStationTargetName(RfidStationConfig station) =>
        !string.IsNullOrWhiteSpace(station.Name) ? station.Name.Trim() : station.StationId.Trim();

    private static string FormatRfid(ushort rfid) => rfid.ToString("X2", CultureInfo.InvariantCulture);

    private static string FormatEnabled(bool enabled) => enabled ? "启用" : "停用";

    private static void AddChange(
        ICollection<(string Label, string Before, string After)> changes,
        string label,
        string before,
        string after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add((label, before, after));
        }
    }

    private static OperationRecord CreateRecord(
        string operationType,
        string targetType,
        string targetName,
        string? beforeValue,
        string? afterValue) => new()
    {
        OperatorName = "管理员",
        OperationType = operationType,
        TargetType = targetType,
        TargetName = targetName,
        BeforeValue = beforeValue,
        AfterValue = afterValue,
        Result = "成功"
    };
}
