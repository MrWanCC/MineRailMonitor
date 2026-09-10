using System.Net;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Protocol;

public sealed class RfidFrameParser : IRfidFrameParser
{
    private const int FrameLength = 40;
    private const byte RfidMode = 0x04;
    private readonly ushort _emptyRfidValue;

    public RfidFrameParser(RfidFrameParserOptions? options = null)
    {
        _emptyRfidValue = options?.EmptyRfidValue ?? 0;
    }

    public bool TryParse(
        byte[] buffer,
        IPEndPoint sourceEndpoint,
        DateTimeOffset receivedAt,
        out RfidStationFrame? frame)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (sourceEndpoint is null)
        {
            throw new ArgumentNullException(nameof(sourceEndpoint));
        }

        frame = null;
        if (buffer.Length != FrameLength ||
            buffer[0] != 0xB0 ||
            buffer[1] != 0xB0 ||
            buffer[38] != 0xAA ||
            buffer[39] != 0xAA ||
            buffer[3] != RfidMode)
        {
            return false;
        }

        var rawSlots = new ushort[14];
        for (var index = 0; index < rawSlots.Length; index++)
        {
            rawSlots[index] = ReadUInt16LittleEndian(buffer, 8 + index * 2);
        }

        // Response CRC is intentionally preserved but not enforced yet. The repository does not
        // contain the complete set of real 2026-09-09 response samples needed to prove Byte0~31.
        var validRfids = rawSlots.Where(value => value != _emptyRfidValue).ToArray();
        var reportedCardCount = buffer[7];

        frame = new RfidStationFrame
        {
            StationAddress = buffer[2],
            Mode = buffer[3],
            CommandBytes = buffer.Skip(4).Take(4).ToArray(),
            HeadRfid = rawSlots[0],
            WagonRfids = rawSlots.Skip(1).Where(value => value != _emptyRfidValue).ToArray(),
            RawRfidSlots = rawSlots,
            ValidRfids = validRfids,
            ReportedCardCount = reportedCardCount,
            ActualNonZeroSlotCount = validRfids.Length,
            ProtocolDataWarning = reportedCardCount != validRfids.Length,
            CrcHigh = buffer[36],
            CrcLow = buffer[37],
            ReceivedAt = receivedAt,
            SourceEndpoint = new IPEndPoint(sourceEndpoint.Address, sourceEndpoint.Port),
            RawData = (byte[])buffer.Clone()
        };
        return true;
    }

    private static ushort ReadUInt16LittleEndian(byte[] buffer, int offset) =>
        (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
}
