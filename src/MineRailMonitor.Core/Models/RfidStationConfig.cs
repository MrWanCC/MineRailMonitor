using System.Net;
using System.Collections.Generic;
using System.Linq;

namespace MineRailMonitor.Core.Models;

public sealed class RfidStationConfig
{
    /// <summary>Administrator-defined stable identifier for this station.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Administrator-visible station name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The station's independent UDP destination IP address.</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>The station's independent UDP destination port.</summary>
    public int Port { get; set; }

    /// <summary>The protocol address carried in Byte2 of the 40-byte frame.</summary>
    public byte ProtocolAddress { get; set; }

    /// <summary>Legacy alias retained for existing project files and callers.</summary>
    public byte Address
    {
        get => ProtocolAddress;
        set => ProtocolAddress = value;
    }

    public bool Enabled { get; set; } = true;

    public byte Mode { get; set; } = 0x04;

    public byte[] CommandBytes { get; set; } = new byte[4];

    public byte[] RequestPayload { get; set; } = new byte[28];

    public byte CrcHigh { get; set; }

    public byte CrcLow { get; set; }

    /// <summary>Legacy alias retained for existing project files and callers.</summary>
    public string DestinationAddress
    {
        get => IpAddress;
        set => IpAddress = value ?? string.Empty;
    }

    /// <summary>Legacy alias retained for existing project files and callers.</summary>
    public int DestinationPort
    {
        get => Port;
        set => Port = value;
    }

    public IPEndPoint DestinationEndpoint { get; set; } = new(IPAddress.None, 0);

    public bool TryResolveEndpoint(out IPEndPoint endpoint)
    {
        var hasCanonicalEndpointFields = !string.IsNullOrWhiteSpace(IpAddress) || Port != 0;
        if (hasCanonicalEndpointFields)
        {
            if (IPAddress.TryParse(IpAddress, out var address) && IsValidAddress(address) && Port is >= 1 and <= 65535)
            {
                endpoint = new IPEndPoint(address, Port);
                return true;
            }

            endpoint = new IPEndPoint(IPAddress.None, 0);
            return false;
        }

        if (!IsValidEndpoint(DestinationEndpoint))
        {
            endpoint = new IPEndPoint(IPAddress.None, 0);
            return false;
        }

        endpoint = new IPEndPoint(DestinationEndpoint.Address, DestinationEndpoint.Port);
        return true;
    }

    /// <summary>
    /// Finds duplicate communication identities among enabled stations.
    /// The identity is endpoint plus protocol address, not ProtocolAddress alone.
    /// Invalid endpoints are left to the normal endpoint validation errors.
    /// </summary>
    public static IReadOnlyList<string> FindDuplicateCommunicationKeys(IEnumerable<RfidStationConfig> stations)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));

        var seen = new HashSet<RfidStationEndpointKey>();
        var duplicates = new List<string>();
        foreach (var station in stations.Where(item => item is not null && item.Enabled))
        {
            if (!station.TryResolveEndpoint(out var endpoint))
            {
                continue;
            }

            var key = new RfidStationEndpointKey(endpoint, station.ProtocolAddress);
            if (seen.Add(key))
            {
                continue;
            }

            duplicates.Add($"启用的RFID基站通信键重复：{key}。");
        }

        return duplicates;
    }

    private static bool IsValidEndpoint(IPEndPoint? endpoint) =>
        endpoint is not null && IsValidAddress(endpoint.Address) && endpoint.Port is >= 1 and <= 65535;

    private static bool IsValidAddress(IPAddress? address) =>
        address is not null && !IPAddress.None.Equals(address) && !IPAddress.Any.Equals(address) && !IPAddress.IPv6Any.Equals(address);
}
