using MineRailMonitor.Core.Protocol;

namespace MineRailMonitor.Core.ExternalAlarm;

/// <summary>
/// CRC16/MODBUS calculator for the external alarm status protocol.
/// </summary>
public sealed class ExternalAlarmModbusCrcCalculator : IExternalAlarmCrcCalculator
{
    public ushort Compute(byte[] buffer, int offset, int length) =>
        ModbusCrc16.Compute(buffer, offset, length);
}
