namespace MineRailMonitor.Core.Protocol;

/// <summary>
/// CRC16/MODBUS helper. The returned value is the numeric CRC; on the wire the low byte is sent first.
/// </summary>
public static class ModbusCrc16
{
    public static ushort Compute(IReadOnlyList<byte> data, int offset = 0, int? length = null)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        if (offset < 0 || offset > data.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var count = length ?? data.Count - offset;
        if (count < 0 || offset + count > data.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        ushort crc = 0xFFFF;
        for (var index = offset; index < offset + count; index++)
        {
            crc ^= data[index];
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (ushort)((crc & 1) != 0
                    ? (crc >> 1) ^ 0xA001
                    : crc >> 1);
            }
        }

        return crc;
    }

    public static byte LowByte(ushort crc) => (byte)(crc & 0xFF);

    public static byte HighByte(ushort crc) => (byte)(crc >> 8);
}
