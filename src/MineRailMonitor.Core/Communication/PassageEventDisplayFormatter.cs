using System.Globalization;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Communication;

public static class PassageEventDisplayFormatter
{
    public static PassageEventDisplayItem Format(PassageEvent passageEvent)
    {
        if (passageEvent is null)
        {
            throw new ArgumentNullException(nameof(passageEvent));
        }

        var eventTypeText = GetEventTypeText(passageEvent.EventType);
        var vehicleName = FormatVehicleName(passageEvent.VehicleNo, passageEvent.VehicleName);
        return new PassageEventDisplayItem
        {
            PassageId = passageEvent.PassageId,
            StationIdText = FormatStationId(passageEvent.StationId),
            TimeText = passageEvent.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            EventTypeText = eventTypeText,
            VehicleNo = FormatVehicleNo(passageEvent.VehicleNo, passageEvent.VehicleName),
            VehicleName = vehicleName,
            Content = GetContent(passageEvent),
            Status = GetStatus(passageEvent.EventType),
            StatusKind = GetStatusKind(passageEvent.EventType),
            HeadRfidText = passageEvent.VehicleHeadRfid?.ToString("X2", CultureInfo.InvariantCulture) ?? "--",
            ExpectedCountText = FormatCount(passageEvent.ExpectedCount),
            ActualCountText = FormatCount(passageEvent.DetectedCount),
            MissingCountText = FormatCount(passageEvent.MissingCount),
            PassageIdText = passageEvent.PassageId.ToString("D")
        };
    }

    public static PassageRunDetailsDisplayItem? FormatPassage(
        IEnumerable<PassageEvent> passageEvents,
        PassageRecord? passageRecord = null)
    {
        if (passageEvents is null)
        {
            throw new ArgumentNullException(nameof(passageEvents));
        }

        var events = passageEvents.ToArray();
        if (events.Length == 0)
        {
            return null;
        }

        var passageId = events[0].PassageId;
        var ordered = events
            .Where(item => item.PassageId == passageId)
            .OrderBy(item => item.OccurredAt)
            .ToArray();
        if (ordered.Length == 0)
        {
            return null;
        }

        var latest = ordered[ordered.Length - 1];
        var vehicleName = ordered.LastOrDefault(item => !string.IsNullOrWhiteSpace(item.VehicleName))?.VehicleName ?? "--";
        var vehicleNo = ordered.LastOrDefault(item => !string.IsNullOrWhiteSpace(item.VehicleNo))?.VehicleNo;
        vehicleNo ??= ordered.LastOrDefault(item => !string.IsNullOrWhiteSpace(item.VehicleName))?.VehicleName;
        if (string.IsNullOrWhiteSpace(vehicleNo))
        {
            vehicleNo = "未配置车辆";
            vehicleName = "未配置车辆";
        }
        else if (string.IsNullOrWhiteSpace(vehicleName) || vehicleName == "--")
        {
            vehicleName = "未命名车辆";
        }
        vehicleNo ??= "未配置车辆";
        vehicleName ??= "未配置车辆";
        var headRfid = ordered.LastOrDefault(item => item.VehicleHeadRfid.HasValue)?.VehicleHeadRfid;
        var expectedCount = ordered.LastOrDefault(item => item.ExpectedCount.HasValue)?.ExpectedCount;
        var detectedCount = ordered.LastOrDefault(item => item.DetectedCount.HasValue)?.DetectedCount;
        var missingCount = ordered.LastOrDefault(item => item.MissingCount.HasValue)?.MissingCount;
        var alarmEvent = ordered.LastOrDefault(item => item.EventType == PassageEventTypes.Alarm);
        var isUnverified = passageRecord?.Outcome == PassageOutcome.Unverified ||
            latest.EventType == PassageEventTypes.Unverified;

        return new PassageRunDetailsDisplayItem
        {
            PassageId = passageId,
            PassageIdText = passageId.ToString("D"),
            StationIdText = FormatStationId(latest.StationId),
            OutcomeReasonText = isUnverified
                ? GetUnverifiedReason(latest.Reason)
                : "—",
            VehicleNo = vehicleNo,
            VehicleName = vehicleName,
            HeadRfidText = headRfid?.ToString("X2", CultureInfo.InvariantCulture) ?? "--",
            ExpectedCountText = FormatCount(expectedCount),
            ActualCountText = FormatCount(detectedCount),
            MissingCountText = FormatCount(missingCount),
            CurrentStatusText = isUnverified ? "完整性未判定" : GetEventTypeText(latest.EventType),
            StatusKind = isUnverified ? "Unverified" : GetStatusKind(latest.EventType),
            AlarmOccurredAtText = alarmEvent is not null
                ? FormatTime(alarmEvent.OccurredAt)
                : passageRecord?.Outcome == PassageOutcome.UncouplingAlarm
                    ? FormatTime(passageRecord.CompletedAt)
                    : "--",
            AlarmAcknowledgedAtText = FormatOptionalTime(passageRecord?.AlarmAcknowledgedAt),
            AlarmRecoveredAtText = FormatOptionalTime(passageRecord?.AlarmRecoveredAt),
            Timeline = ordered.Select(FormatTimelineItem).ToArray()
        };
    }

    private static string FormatTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FormatOptionalTime(DateTimeOffset? value) =>
        value.HasValue ? FormatTime(value.Value) : "--";

    private static string GetEventTypeText(string eventType) => eventType switch
    {
        PassageEventTypes.Start => "开始识别",
        PassageEventTypes.Progress => "识别进度",
        PassageEventTypes.Complete => "通过完成",
        PassageEventTypes.Alarm => "脱节报警",
        PassageEventTypes.Unverified => "完整性未判定",
        PassageEventTypes.Clear => "清除完成",
        PassageEventTypes.PersistenceFailed => "数据库保存失败",
        PassageEventTypes.PersistenceRecovered => "数据库保存恢复",
        _ => string.IsNullOrWhiteSpace(eventType) ? "未知事件" : eventType
    };

    private static string GetStatus(string eventType) => eventType switch
    {
        PassageEventTypes.Start => "进行中",
        PassageEventTypes.Progress => "进行中",
        PassageEventTypes.Complete => "完成",
        PassageEventTypes.Alarm => "报警",
        PassageEventTypes.Unverified => "完整性未判定",
        PassageEventTypes.Clear => "已清除",
        PassageEventTypes.PersistenceFailed => "待恢复",
        PassageEventTypes.PersistenceRecovered => "已恢复",
        _ => "未知"
    };

    private static string GetStatusKind(string eventType) => eventType switch
    {
        PassageEventTypes.Start => "InProgress",
        PassageEventTypes.Progress => "InProgress",
        PassageEventTypes.Complete => "Complete",
        PassageEventTypes.Alarm => "Alarm",
        PassageEventTypes.Unverified => "Unverified",
        PassageEventTypes.Clear => "Clear",
        PassageEventTypes.PersistenceFailed => "Warning",
        PassageEventTypes.PersistenceRecovered => "Clear",
        _ => "Unknown"
    };

    private static string GetContent(PassageEvent passageEvent)
    {
        var expected = FormatNumber(passageEvent.ExpectedCount);
        var detected = FormatNumber(passageEvent.DetectedCount);
        switch (passageEvent.EventType)
        {
            case PassageEventTypes.Start:
                var headRfid = passageEvent.VehicleHeadRfid?.ToString("X2", CultureInfo.InvariantCulture) ?? "--";
                return $"车头RFID:{headRfid} 理论{expected}节";
            case PassageEventTypes.Progress:
                return $"当前{detected}/{expected}";
            case PassageEventTypes.Complete:
                return $"实际{detected}/{expected}，通行完成";
            case PassageEventTypes.Alarm:
                return $"实际{detected}/{expected} 缺少{FormatNumber(passageEvent.MissingCount)}节";
            case PassageEventTypes.Clear:
                return "清除完成";
            case PassageEventTypes.Unverified:
                return $"完整性未判定；已识别{detected}个RFID；{GetUnverifiedReason(passageEvent.Reason)}";
            case PassageEventTypes.PersistenceFailed:
                return $"数据库保存失败（{FormatNumber(passageEvent.FailureCount)}次）；{passageEvent.Reason}";
            case PassageEventTypes.PersistenceRecovered:
                return $"Passage记录已恢复保存；此前失败{FormatNumber(passageEvent.FailureCount)}次";
            default:
                return string.IsNullOrWhiteSpace(passageEvent.Reason)
                    ? passageEvent.EventType
                    : passageEvent.Reason ?? string.Empty;
        }
    }

    private static string FormatCount(int? count) => count.HasValue ? $"{count.Value}节" : "--";

    private static string FormatVehicleNo(string? vehicleNo, string? vehicleName) =>
        !string.IsNullOrWhiteSpace(vehicleNo)
            ? vehicleNo!
            : !string.IsNullOrWhiteSpace(vehicleName)
                ? vehicleName!
                : "未配置车辆";

    private static string FormatVehicleName(string? vehicleNo, string? vehicleName) =>
        !string.IsNullOrWhiteSpace(vehicleName)
            ? vehicleName!
            : !string.IsNullOrWhiteSpace(vehicleNo)
                ? "未命名车辆"
                : "未配置车辆";

    private static string FormatNumber(int? count) => count?.ToString(CultureInfo.InvariantCulture) ?? "--";

    private static PassageEventTimelineItem FormatTimelineItem(PassageEvent passageEvent)
    {
        var details = passageEvent.EventType switch
        {
            PassageEventTypes.Start => new[]
            {
                $"车头标签: {FormatRfid(passageEvent.VehicleHeadRfid)}",
                $"理论编组: {FormatCount(passageEvent.ExpectedCount)}"
            },
            PassageEventTypes.Progress => new[]
            {
                $"当前: {FormatNumber(passageEvent.DetectedCount)} / {FormatNumber(passageEvent.ExpectedCount)}"
            },
            PassageEventTypes.Complete => new[]
            {
                $"实际: {FormatCount(passageEvent.DetectedCount)}"
            },
            PassageEventTypes.Alarm => new[]
            {
                $"实际: {FormatCount(passageEvent.DetectedCount)}",
                $"缺少: {FormatCount(passageEvent.MissingCount)}",
                $"原因: {FormatAlarmReason(passageEvent.Reason)}"
            },
            PassageEventTypes.Unverified => new[]
            {
                $"已识别: {FormatNumber(passageEvent.DetectedCount)}个唯一RFID",
                $"结果: 完整性未判定",
                $"原因: {GetUnverifiedReason(passageEvent.Reason)}"
            },
            PassageEventTypes.PersistenceFailed => new[]
            {
                $"失败次数: {FormatNumber(passageEvent.FailureCount)}",
                $"原因: {passageEvent.Reason ?? "--"}"
            },
            PassageEventTypes.PersistenceRecovered => new[]
            {
                "状态: 数据库保存恢复",
                $"此前失败: {FormatNumber(passageEvent.FailureCount)}次"
            },
            PassageEventTypes.Clear => new[] { "清除完成" },
            _ => new[] { passageEvent.Reason ?? passageEvent.EventType }
        };

        return new PassageEventTimelineItem
        {
            TimeText = passageEvent.OccurredAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            EventTypeText = GetEventTypeText(passageEvent.EventType),
            StatusKind = GetStatusKind(passageEvent.EventType),
            Details = details
        };
    }

    private static string FormatAlarmReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) || string.Equals(reason, "脱节报警", StringComparison.Ordinal)
            ? "识别超时未达到理论节数"
            : reason ?? string.Empty;

    private static string FormatRfid(ushort? rfid) => rfid?.ToString("X2", CultureInfo.InvariantCulture) ?? "--";

    private static string FormatStationId(string? stationId)
    {
        var value = stationId?.Trim();
        return value is null || value.Length == 0 ? "--" : value;
    }

    private static string GetUnverifiedReason(string? reason)
    {
        var value = reason?.Trim();
        return value is null || value.Length == 0
            ? "车头未匹配车辆配置，完整性未判定"
            : value;
    }
}
