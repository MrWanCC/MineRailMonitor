using MineRailMonitor.Simulator.Models;

namespace MineRailMonitor.Simulator.Protocol;

public static class RfidResponseFrameBuilder
{
    public static byte[] Build(SimulatorFrameInput input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var frame = new byte[RfidFrameLayout.FrameLength];
        frame[0] = 0xB0;
        frame[1] = 0xB0;
        frame[2] = input.Address;
        frame[3] = 0x04;
        if (input.CommandBytes is null || input.CommandBytes.Length != 4)
        {
            throw new ArgumentException("CommandBytes must contain exactly 4 bytes.", nameof(input));
        }

        // Real responses use Byte7 as the reported non-empty slot count.
        // Keep the first three command/status bytes and derive Byte7 from all 14 slots.
        Array.Copy(input.CommandBytes, 0, frame, 4, 3);

        if (input.Slots is null || input.Slots.Length != 14)
        {
            throw new ArgumentException("Slots must contain exactly 14 values.", nameof(input));
        }
        frame[7] = (byte)input.Slots.Count(value => value != input.EmptySlotValue);
        for (var index = 0; index < 14; index++)
        {
            var value = input.Slots[index];
            WriteUInt16LittleEndian(frame, 8 + index * 2, value);
        }

        frame[36] = input.CrcHigh;
        frame[37] = input.CrcLow;
        frame[38] = 0xAA;
        frame[39] = 0xAA;
        return frame;
    }

    private static void WriteUInt16LittleEndian(byte[] frame, int offset, ushort value)
    {
        frame[offset] = (byte)(value & 0xFF);
        frame[offset + 1] = (byte)(value >> 8);
    }
}
