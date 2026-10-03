using System.Globalization;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Communication;

public static class PassageAlarmDisplayFormatter
{
    public static string FormatSummary(PassageRecord record)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        if (!HasMatchedVehicle(record))
        {
            return record.AlarmMessage ?? "脱节报警";
        }

        return $"车辆：{VehicleName(record)}，车头：{FormatRfid(record.VehicleHeadRfid)}，实际：{record.DetectedVehicleCount}/{FormatExpectedCount(record)}节，缺少：{MissingCount(record)}节";
    }

    public static string FormatDetails(PassageRecord record)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        if (!HasMatchedVehicle(record))
        {
            return record.AlarmMessage ?? "脱节报警";
        }

        return $"车辆:\n{VehicleName(record)}\n车头:\n{FormatRfid(record.VehicleHeadRfid)}\n理论节数:\n{FormatExpectedCount(record)}\n实际:\n{record.DetectedVehicleCount.ToString(CultureInfo.InvariantCulture)}\n缺少:\n{MissingCount(record).ToString(CultureInfo.InvariantCulture)}节";
    }

    private static bool HasMatchedVehicle(PassageRecord record) =>
        record.Outcome == PassageOutcome.UncouplingAlarm &&
        record.VehicleHeadRfid.HasValue;

    private static string VehicleName(PassageRecord record) =>
        string.IsNullOrWhiteSpace(record.VehicleName) ? "未命名车辆" : record.VehicleName!;

    private static int MissingCount(PassageRecord record) =>
        Math.Max(record.ExpectedVehicleCount.GetValueOrDefault() - record.DetectedVehicleCount, 0);

    private static string FormatExpectedCount(PassageRecord record) =>
        record.ExpectedVehicleCount?.ToString(CultureInfo.InvariantCulture) ?? "未配置";

    private static string FormatRfid(ushort? value) =>
        value.HasValue ? value.Value.ToString("X2", CultureInfo.InvariantCulture) : "-";
}
