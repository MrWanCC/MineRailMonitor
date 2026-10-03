namespace MineRailMonitor.Core.ExternalAlarm;

public sealed class ExternalAlarmCoordinatorState
{
    public ExternalAlarmCoordinatorState(
        string yardId,
        string stationId,
        byte stationAddress,
        ExternalAlarmStatus status,
        int vehicleCount,
        bool alarmLatched,
        DateTimeOffset? alarmTime)
    {
        YardId = string.IsNullOrWhiteSpace(yardId)
            ? throw new ArgumentException("站场编号不能为空。", nameof(yardId))
            : yardId.Trim();
        StationId = string.IsNullOrWhiteSpace(stationId)
            ? throw new ArgumentException("基站编号不能为空。", nameof(stationId))
            : stationId.Trim();
        StationAddress = stationAddress;
        Status = status;
        VehicleCount = vehicleCount;
        AlarmLatched = alarmLatched;
        AlarmTime = alarmTime;
    }

    public string YardId { get; }

    public string Yard => YardId;

    public string StationId { get; }

    public byte StationAddress { get; }

    public ExternalAlarmStatus Status { get; }

    public int VehicleCount { get; }

    public bool AlarmLatched { get; }

    public DateTimeOffset? AlarmTime { get; }
}
