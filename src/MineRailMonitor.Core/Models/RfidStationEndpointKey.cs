using System.Net;

namespace MineRailMonitor.Core.Models;

/// <summary>
/// The complete communication identity of an RFID station.
/// ProtocolAddress is only unique within the station endpoint.
/// </summary>
public readonly struct RfidStationEndpointKey : IEquatable<RfidStationEndpointKey>
{
    public RfidStationEndpointKey(IPEndPoint endpoint, byte protocolAddress)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        ProtocolAddress = protocolAddress;
    }

    public IPEndPoint Endpoint { get; }

    public byte ProtocolAddress { get; }

    public bool Equals(RfidStationEndpointKey other)
    {
        if (Endpoint is null || other.Endpoint is null)
        {
            return Endpoint is null && other.Endpoint is null && ProtocolAddress == other.ProtocolAddress;
        }

        return ProtocolAddress == other.ProtocolAddress &&
            Endpoint.Port == other.Endpoint.Port &&
            Endpoint.Address.Equals(other.Endpoint.Address);
    }

    public override bool Equals(object? obj) => obj is RfidStationEndpointKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            if (Endpoint is null)
            {
                return ProtocolAddress;
            }

            var hash = 17;
            foreach (var value in Endpoint.Address.GetAddressBytes())
            {
                hash = (hash * 31) + value;
            }

            hash = (hash * 31) + Endpoint.Port;
            return (hash * 31) + ProtocolAddress;
        }
    }

    public override string ToString() =>
        $"{Endpoint.Address}:{Endpoint.Port}/{ProtocolAddress:X2}";

    public static bool operator ==(RfidStationEndpointKey left, RfidStationEndpointKey right) => left.Equals(right);

    public static bool operator !=(RfidStationEndpointKey left, RfidStationEndpointKey right) => !left.Equals(right);
}
