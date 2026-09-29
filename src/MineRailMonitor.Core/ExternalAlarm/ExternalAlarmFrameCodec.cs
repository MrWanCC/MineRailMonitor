namespace MineRailMonitor.Core.ExternalAlarm;

public sealed class ExternalAlarmFrameCodec
{
    private const int FrameLength = 12;
    private const int CrcOffset = 8;
    private readonly IExternalAlarmCrcCalculator _crcCalculator;

    public ExternalAlarmFrameCodec()
        : this(new ExternalAlarmModbusCrcCalculator())
    {
    }

    public ExternalAlarmFrameCodec(IExternalAlarmCrcCalculator crcCalculator)
    {
        _crcCalculator = crcCalculator ?? throw new ArgumentNullException(nameof(crcCalculator));
    }

    public ExternalAlarmFrame CreateFrame(ExternalAlarmStationSnapshot snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

        var payload = new byte[FrameLength];
        payload[0] = 0xB0;
        payload[1] = 0xB0;
        payload[2] = snapshot.StationAddress;
        payload[3] = 0x04;
        payload[4] = (byte)snapshot.Status;
        payload[5] = 0x00;
        payload[6] = 0x00;
        payload[7] = (byte)snapshot.AccumulatedVehicleCount;

        // External Alarm follows the RFID wire order: CRC low byte first,
        // followed by the CRC high byte.
        var crc = _crcCalculator.Compute(payload, 0, CrcOffset);
        payload[8] = (byte)(crc & 0xFF);
        payload[9] = (byte)(crc >> 8);
        payload[10] = 0xAA;
        payload[11] = 0xAA;

        return new ExternalAlarmFrame(
            snapshot.StationAddress,
            snapshot.Status,
            (byte)snapshot.AccumulatedVehicleCount,
            crc);
    }

    public byte[] Encode(ExternalAlarmStationSnapshot snapshot)
    {
        var frame = CreateFrame(snapshot);
        var payload = new byte[FrameLength];
        payload[0] = 0xB0;
        payload[1] = 0xB0;
        payload[2] = frame.StationAddress;
        payload[3] = 0x04;
        payload[4] = (byte)frame.Status;
        payload[5] = 0x00;
        payload[6] = 0x00;
        payload[7] = frame.AccumulatedVehicleCount;
        payload[8] = (byte)(frame.Crc & 0xFF);
        payload[9] = (byte)(frame.Crc >> 8);
        payload[10] = 0xAA;
        payload[11] = 0xAA;
        return payload;
    }

    public byte[] EncodeClear(byte stationAddress)
    {
        var payload = new byte[FrameLength];
        payload[0] = 0xB0;
        payload[1] = 0xB0;
        payload[2] = stationAddress;
        payload[3] = 0x04;
        payload[4] = 0x01;
        payload[5] = 0x00;
        payload[6] = 0x00;
        payload[7] = 0x00;

        var crc = _crcCalculator.Compute(payload, 0, CrcOffset);
        payload[8] = (byte)(crc & 0xFF);
        payload[9] = (byte)(crc >> 8);
        payload[10] = 0xAA;
        payload[11] = 0xAA;
        return payload;
    }

    public bool TryDecodeClear(byte[] payload, out ExternalAlarmClearCommand command)
    {
        command = null!;
        if (payload is null || payload.Length != FrameLength ||
            payload[0] != 0xB0 || payload[1] != 0xB0 ||
            payload[3] != 0x04 || payload[4] != 0x01 ||
            payload[10] != 0xAA || payload[11] != 0xAA)
        {
            return false;
        }

        var expectedCrc = (ushort)(payload[8] | (payload[9] << 8));
        var actualCrc = _crcCalculator.Compute(payload, 0, CrcOffset);
        if (expectedCrc != actualCrc)
        {
            return false;
        }

        command = new ExternalAlarmClearCommand(payload[2], payload[4], payload);
        return true;
    }
}
