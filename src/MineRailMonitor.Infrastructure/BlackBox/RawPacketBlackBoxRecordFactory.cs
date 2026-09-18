using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Infrastructure.BlackBox;

public static class RawPacketBlackBoxRecordFactory
{
    public static RawPacketBlackBoxRecord FromReceived(
        YardCommunicationContext context,
        RfidUdpDatagramEventArgs datagram)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (datagram is null) throw new ArgumentNullException(nameof(datagram));

        var protocolAddress = datagram.Data.Length >= 3 ? datagram.Data[2] : (byte?)null;
        var station = FindStation(context, datagram.RemoteEndPoint, protocolAddress);
        return new RawPacketBlackBoxRecord
        {
            Time = datagram.ReceivedAt,
            Direction = "RX",
            YardId = context.YardId,
            StationId = station?.StationId,
            ProtocolAddress = protocolAddress?.ToString("X2"),
            LocalEndPoint = context.ListenerEndPoint?.ToString(),
            RemoteEndPoint = datagram.RemoteEndPoint.ToString(),
            Length = datagram.Data.Length,
            Hex = FormatHex(datagram.Data),
            Valid = datagram.IsValid,
            ValidationError = datagram.ValidationError
        };
    }

    public static RawPacketBlackBoxRecord FromSent(
        YardCommunicationContext context,
        RfidUdpDatagramSentEventArgs datagram)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (datagram is null) throw new ArgumentNullException(nameof(datagram));

        var protocolAddress = datagram.Data.Length >= 3 ? datagram.Data[2] : (byte?)null;
        var station = FindStation(context, datagram.DestinationEndPoint, protocolAddress);
        return new RawPacketBlackBoxRecord
        {
            Time = datagram.SentAt,
            Direction = "TX",
            YardId = context.YardId,
            StationId = station?.StationId,
            ProtocolAddress = protocolAddress?.ToString("X2"),
            LocalEndPoint = datagram.LocalEndPoint?.ToString(),
            RemoteEndPoint = datagram.DestinationEndPoint.ToString(),
            Length = datagram.Data.Length,
            Hex = FormatHex(datagram.Data),
            Command = datagram.Data.Length > 4 && datagram.Data[4] == 0x01 ? "Clear" : "Read"
        };
    }

    private static RfidStationConfig? FindStation(
        YardCommunicationContext context,
        IPEndPoint endpoint,
        byte? protocolAddress)
    {
        if (!protocolAddress.HasValue)
        {
            return null;
        }

        return context.Stations.FirstOrDefault(station =>
            station.TryResolveEndpoint(out var stationEndpoint) &&
            stationEndpoint.Address.Equals(endpoint.Address) &&
            stationEndpoint.Port == endpoint.Port &&
            station.ProtocolAddress == protocolAddress.Value);
    }

    private static string FormatHex(byte[] data) =>
        BitConverter.ToString(data).Replace('-', ' ');
}
