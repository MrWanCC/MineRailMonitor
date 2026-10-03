namespace MineRailMonitor.Core.Models;

public sealed class VehicleStatisticsItem
{
    public VehicleStatisticsItem(
        string? vehicleNo,
        string? vehicleName,
        int totalCount,
        int normalCount,
        int alarmCount)
    {
        VehicleNo = vehicleNo;
        VehicleName = vehicleName;
        TotalCount = totalCount;
        NormalCount = normalCount;
        AlarmCount = alarmCount;
        AlarmRatePercent = totalCount == 0 ? 0d : alarmCount * 100d / totalCount;
    }

    public string? VehicleNo { get; }

    public string? VehicleName { get; }

    public int TotalCount { get; }

    public int NormalCount { get; }

    public int AlarmCount { get; }

    public double AlarmRatePercent { get; }
}
