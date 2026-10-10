using System.Globalization;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Communication;

public static class VehicleMatchDiagnosticFormatter
{
    public static string Format(VehicleMatchResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));

        var rfidText = result.HeadRfid.HasValue
            ? result.HeadRfid.Value.ToString("X2", CultureInfo.InvariantCulture)
            : "未读取";
        var sourceText = result.Source switch
        {
            VehicleMatchSource.VehicleConfiguration => "车辆配置",
            VehicleMatchSource.StationConfiguration => "基站配置",
            VehicleMatchSource.GlobalConfiguration => "全局配置",
            _ => "未配置车辆节数"
        };

        if (!result.IsMatched)
        {
            return $"[VehicleMatch] 未配置车辆: 车头RFID={rfidText}{Environment.NewLine}" +
                   $"采用节数: {FormatExpectedCount(result.ExpectedCount)}{Environment.NewLine}" +
                   $"来源: {sourceText}";
        }

        return $"[VehicleMatch]{Environment.NewLine}" +
               $"车头RFID: {rfidText}{Environment.NewLine}" +
               $"匹配车辆: {result.VehicleName}{Environment.NewLine}" +
               $"配置节数: {FormatExpectedCount(result.ExpectedCount)}{Environment.NewLine}" +
               $"来源: {sourceText}";
    }

    public static string Format(ushort headRfid, VehicleConfig? vehicle)
    {
        var rfidText = headRfid.ToString("X2", CultureInfo.InvariantCulture);
        if (vehicle is null)
        {
            return $"[VehicleMatch] 未配置车辆: 车头RFID={rfidText}";
        }

        return $"[VehicleMatch]{Environment.NewLine}" +
               $"车头RFID: {rfidText}{Environment.NewLine}" +
               $"匹配车辆: {vehicle.VehicleName ?? string.Empty}{Environment.NewLine}" +
               $"配置节数: {vehicle.TotalSectionCount.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatExpectedCount(int? expectedCount) =>
        expectedCount.HasValue
            ? expectedCount.Value.ToString(CultureInfo.InvariantCulture)
            : "未配置";
}
