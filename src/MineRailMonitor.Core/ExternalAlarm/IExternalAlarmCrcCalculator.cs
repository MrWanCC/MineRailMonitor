namespace MineRailMonitor.Core.ExternalAlarm;

/// <summary>
/// Calculates the CRC for the external alarm status protocol.
/// The implementation is intentionally separate from the RFID CRC implementation.
/// </summary>
public interface IExternalAlarmCrcCalculator
{
    ushort Compute(byte[] buffer, int offset, int length);
}
