namespace MineRailMonitor.Core.ExternalAlarm;

public sealed class ExternalAlarmFrame
{
    public ExternalAlarmFrame(
        byte stationAddress,
        ExternalAlarmStatus status,
        byte accumulatedVehicleCount,
        ushort crc)
    {
        StationAddress = stationAddress;
        Status = status;
        AccumulatedVehicleCount = accumulatedVehicleCount;
        Crc = crc;
    }

    public byte StationAddress { get; }

    public ExternalAlarmStatus Status { get; }

    public byte AccumulatedVehicleCount { get; }

    public ushort Crc { get; }
}
