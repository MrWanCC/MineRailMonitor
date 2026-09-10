using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Protocol;

public static class RfidRequestFrameBuilder
{
    public static byte[] Build(RfidStationConfig station)
    {
        return BuildCore(station, station?.CommandBytes);
    }

    public static byte[] Build(RfidStationConfig station, RfidPollCommand command)
    {
        if (station is null)
        {
            throw new ArgumentNullException(nameof(station));
        }

        var commandBytes = station.CommandBytes?.ToArray();
        if (commandBytes is null || commandBytes.Length != 4)
        {
            throw new ArgumentException("CommandBytes must contain exactly 4 bytes.", nameof(station));
        }

        if (command == RfidPollCommand.Clear)
        {
            commandBytes[0] = 0x01;
        }
        else if (command != RfidPollCommand.Read)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        return BuildCore(station, commandBytes);
    }

    private static byte[] BuildCore(RfidStationConfig station, IReadOnlyList<byte>? commandBytes)
    {
        if (station is null)
        {
            throw new ArgumentNullException(nameof(station));
        }
        if (commandBytes is null || commandBytes.Count != 4)
        {
            throw new ArgumentException("CommandBytes must contain exactly 4 bytes.", nameof(station));
        }
        if (station.RequestPayload is null || station.RequestPayload.Length != 28)
        {
            throw new ArgumentException("RequestPayload must contain exactly 28 bytes.", nameof(station));
        }

        var frame = new byte[40];
        frame[0] = 0xB0;
        frame[1] = 0xB0;
        frame[2] = station.ProtocolAddress;
        frame[3] = station.Mode;
        Array.Copy(commandBytes.ToArray(), 0, frame, 4, 4);
        Array.Copy(station.RequestPayload, 0, frame, 8, 28);
        // 2026-09-09 hardware capture confirms CRC16/MODBUS over Byte0~35,
        // with the low byte on the wire first. Legacy configured CRC fields are
        // retained for JSON compatibility but are no longer used to build requests.
        var crc = ModbusCrc16.Compute(frame, 0, 36);
        frame[36] = ModbusCrc16.LowByte(crc);
        frame[37] = ModbusCrc16.HighByte(crc);
        frame[38] = 0xAA;
        frame[39] = 0xAA;
        return frame;
    }
}
