namespace MineRailMonitor.Core.ExternalAlarm;

public sealed class ExternalAlarmStationSnapshot
{
    public ExternalAlarmStationSnapshot(
        byte stationAddress,
        ExternalAlarmStatus status,
        int accumulatedVehicleCount)
    {
        if (accumulatedVehicleCount is < 0 or > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accumulatedVehicleCount),
                accumulatedVehicleCount,
                "外部报警接口车厢数量必须在 0 到 255 之间。");
        }

        StationAddress = stationAddress;
        Status = status;
        AccumulatedVehicleCount = accumulatedVehicleCount;
    }

    public byte StationAddress { get; }

    public ExternalAlarmStatus Status { get; }

    public int AccumulatedVehicleCount { get; }
}
