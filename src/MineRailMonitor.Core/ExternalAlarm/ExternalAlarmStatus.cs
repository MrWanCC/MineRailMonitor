namespace MineRailMonitor.Core.ExternalAlarm;

public enum ExternalAlarmStatus : byte
{
    NoVehicle = 0x00,
    Normal = 0x01,
    Recognizing = 0x02,
    UncouplingAlarm = 0x03
}
