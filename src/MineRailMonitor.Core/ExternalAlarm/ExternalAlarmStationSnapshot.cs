namespace MineRailMonitor.Core.ExternalAlarm;

public sealed class ExternalAlarmStationSnapshot
{
    public ExternalAlarmStationSnapshot(
        byte stationAddress,
        ExternalAlarmStatus status,
        int accumulatedVehicleCount,
        string? stationId = null,
        bool alarmLatched = false,
        DateTimeOffset? alarmTime = null)
    {
        if (accumulatedVehicleCount is < 0 or > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accumulatedVehicleCount),
                accumulatedVehicleCount,
                "外部报警接口车厢数量必须在 0 到 255 之间。");
        }

        StationId = string.IsNullOrWhiteSpace(stationId)
            ? $"RFID-{stationAddress:X2}"
            : stationId!.Trim();
        StationAddress = stationAddress;
        Status = status;
        AccumulatedVehicleCount = accumulatedVehicleCount;
        AlarmLatched = alarmLatched || status == ExternalAlarmStatus.UncouplingAlarm;
        AlarmTime = AlarmLatched ? alarmTime : null;
    }

    public string StationId { get; }

    public byte StationAddress { get; }

    public ExternalAlarmStatus Status { get; }

    public int AccumulatedVehicleCount { get; }

    public bool AlarmLatched { get; }

    public DateTimeOffset? AlarmTime { get; }
}
