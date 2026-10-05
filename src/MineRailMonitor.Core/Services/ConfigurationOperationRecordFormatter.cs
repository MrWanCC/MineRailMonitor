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

    public static IReadOnlyList<OperationRecord> GetYardCommunicationChanges(
        IReadOnlyList<YardCommunicationConfig> before,
        IReadOnlyList<YardCommunicationConfig> after) => GetYardChanges(
        before,
        after,
        "站场通信配置",
        GetYardId,
        GetYardCommunicationFieldChanges,
        FormatYardCommunicationSnapshot);

    public static IReadOnlyList<OperationRecord> GetYardAlarmForwardChanges(
        IReadOnlyList<YardAlarmForwardConfig> before,
        IReadOnlyList<YardAlarmForwardConfig> after) => GetYardChanges(
        before,
        after,
        "外部报警转发配置",
        GetYardId,
        GetYardAlarmForwardFieldChanges,
        FormatYardAlarmForwardSnapshot);

    private static IReadOnlyList<OperationRecord> GetYardChanges<T>(
        IReadOnlyList<T> before,
        IReadOnlyList<T> after,
        string targetType,
        Func<T, string> getYardId,
        Func<T, T, List<(string Label, string Before, string After)>> getFieldChanges,
        Func<T, string> formatSnapshot)
        where T : class
    {
        if (before is null) throw new ArgumentNullException(nameof(before));
        if (after is null) throw new ArgumentNullException(nameof(after));

        var previousByYardId = before
            .Where(item => item is not null)
            .Select(item => (Item: item, YardId: getYardId(item)))
            .Where(item => !string.IsNullOrWhiteSpace(item.YardId))
            .ToDictionary(item => item.YardId, item => item.Item, StringComparer.OrdinalIgnoreCase);
        var nextIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var records = new List<OperationRecord>();

        foreach (var configuration in after.Where(item => item is not null))
        {
            var yardId = getYardId(configuration);
            if (string.IsNullOrWhiteSpace(yardId))
            {
                continue;
            }

            nextIds.Add(yardId);
            if (!previousByYardId.TryGetValue(yardId, out var previous))
            {
                records.Add(CreateRecord(
                    "COMMUNICATION_CONFIG_CHANGED",
                    targetType,
                    yardId,
                    null,
                    formatSnapshot(configuration)));
                continue;
            }

            var changedFields = getFieldChanges(previous, configuration);
            if (changedFields.Count == 0)
            {
                continue;
            }

            records.Add(CreateRecord(
                "COMMUNICATION_CONFIG_CHANGED",
                targetType,
                yardId,
                string.Join(Environment.NewLine, changedFields.Select(item => $"{item.Label}: {item.Before}")),
                string.Join(Environment.NewLine, changedFields.Select(item => $"{item.Label}: {item.After}"))));
        }

        foreach (var previous in before.Where(item => item is not null))
        {
            var yardId = getYardId(previous);
            if (string.IsNullOrWhiteSpace(yardId) || nextIds.Contains(yardId))
            {
                continue;
            }

            records.Add(CreateRecord(
                "COMMUNICATION_CONFIG_CHANGED",
                targetType,
                yardId,
                formatSnapshot(previous),
                null));
        }

        return records;
    }

    private static List<(string Label, string Before, string After)> GetYardCommunicationFieldChanges(
        YardCommunicationConfig before,
        YardCommunicationConfig after)
    {
        var changedFields = new List<(string Label, string Before, string After)>();
        AddChange(changedFields, "RFID监听IP", before.ListenIp, after.ListenIp);
        AddChange(changedFields, "RFID监听端口", FormatNumber(before.ListenPort), FormatNumber(after.ListenPort));
        AddChange(changedFields, "外部报警监听IP", before.ExternalAlarmListenIp, after.ExternalAlarmListenIp);
        AddChange(changedFields, "外部报警监听端口", FormatNumber(before.ExternalAlarmListenPort), FormatNumber(after.ExternalAlarmListenPort));
        AddChange(changedFields, "启用状态", FormatEnabled(before.Enabled), FormatEnabled(after.Enabled));
        return changedFields;
    }

    private static List<(string Label, string Before, string After)> GetYardAlarmForwardFieldChanges(
        YardAlarmForwardConfig before,
        YardAlarmForwardConfig after)
    {
        var changedFields = new List<(string Label, string Before, string After)>();
        AddChange(changedFields, "转发状态", FormatEnabled(before.Enabled), FormatEnabled(after.Enabled));
        AddChange(changedFields, "目标IP", before.TargetIp, after.TargetIp);
        AddChange(changedFields, "目标端口", FormatNumber(before.TargetPort), FormatNumber(after.TargetPort));
        AddChange(changedFields, "本机来源端口", FormatNumber(before.SourcePort), FormatNumber(after.SourcePort));
        return changedFields;
    }

    private static string FormatYardCommunicationSnapshot(YardCommunicationConfig configuration) => string.Join(
        Environment.NewLine,
        $"RFID监听IP: {configuration.ListenIp}",
        $"RFID监听端口: {FormatNumber(configuration.ListenPort)}",
        $"外部报警监听IP: {configuration.ExternalAlarmListenIp}",
        $"外部报警监听端口: {FormatNumber(configuration.ExternalAlarmListenPort)}",
        $"启用状态: {FormatEnabled(configuration.Enabled)}");

    private static string FormatYardAlarmForwardSnapshot(YardAlarmForwardConfig configuration) => string.Join(
        Environment.NewLine,
        $"转发状态: {FormatEnabled(configuration.Enabled)}",
        $"目标IP: {configuration.TargetIp}",
        $"目标端口: {FormatNumber(configuration.TargetPort)}",
        $"本机来源端口: {FormatNumber(configuration.SourcePort)}");

    private static string GetYardId(YardCommunicationConfig configuration) => configuration.YardId?.Trim() ?? string.Empty;

    private static string GetYardId(YardAlarmForwardConfig configuration) => configuration.YardId?.Trim() ?? string.Empty;

    private static string FormatNumber(int value) => value.ToString(CultureInfo.InvariantCulture);

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
