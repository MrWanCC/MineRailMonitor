using System.Net;

namespace MineRailMonitor.Core.Communication;

public sealed class RfidUdpDatagramEventArgs : EventArgs
{
    public RfidUdpDatagramEventArgs(byte[] data, IPEndPoint remoteEndPoint, DateTimeOffset receivedAt)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        RemoteEndPoint = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));
        ReceivedAt = receivedAt;
        (IsValid, ValidationError) = Validate(data);
    }

    public byte[] Data { get; }

    public IPEndPoint RemoteEndPoint { get; }

    public DateTimeOffset ReceivedAt { get; }

    public bool IsValid { get; }

    public string? ValidationError { get; }

    private static (bool IsValid, string? Error) Validate(byte[] data)
    {
        if (data.Length != 40)
        {
            return (false, "Length must be 40 bytes.");
        }

        if (data[0] != 0xB0 || data[1] != 0xB0)
        {
            return (false, "Invalid frame header.");
        }

        if (data[38] != 0xAA || data[39] != 0xAA)
        {
            return (false, "Invalid frame tail.");
        }

        return (true, null);
    }
}
