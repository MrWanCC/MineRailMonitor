namespace MineRailMonitor.Core.ExternalAlarm;

public enum ExternalAlarmStatus : byte
{
    Normal = 0x01,
    Recognizing = 0x02,
    UncouplingAlarm = 0x03
}
