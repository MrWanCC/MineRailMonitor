namespace MineRailMonitor.Core.ExternalAlarm;

public sealed class ExternalAlarmClearCommand
{
    public ExternalAlarmClearCommand(byte stationAddress, byte command, byte[] rawFrame)
    {
        RawFrame = rawFrame is null
            ? throw new ArgumentNullException(nameof(rawFrame))
            : (byte[])rawFrame.Clone();
        StationAddress = stationAddress;
        Command = command;
    }

    public byte StationAddress { get; }

    public byte Command { get; }

    public byte[] RawFrame { get; }
}
