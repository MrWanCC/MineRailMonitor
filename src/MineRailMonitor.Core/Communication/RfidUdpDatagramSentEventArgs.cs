using System.Net;

namespace MineRailMonitor.Core.Communication;

public sealed class RfidUdpDatagramSentEventArgs : EventArgs
{
    public RfidUdpDatagramSentEventArgs(
        byte[] data,
        IPEndPoint destinationEndPoint,
        DateTimeOffset sentAt,
        IPEndPoint? localEndPoint)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (destinationEndPoint is null) throw new ArgumentNullException(nameof(destinationEndPoint));

        Data = data.ToArray();
        DestinationEndPoint = Clone(destinationEndPoint);
        SentAt = sentAt;
        LocalEndPoint = localEndPoint is null ? null : Clone(localEndPoint);
    }

    public byte[] Data { get; }

    public IPEndPoint DestinationEndPoint { get; }

    public DateTimeOffset SentAt { get; }

    public IPEndPoint? LocalEndPoint { get; }

    private static IPEndPoint Clone(IPEndPoint endPoint) =>
        new(endPoint.Address, endPoint.Port);
}
