using System.Globalization;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Communication;

public static class RfidStationStatusDisplayFormatter
{
    public static string FormatStatus(RfidStationConfig station, RfidStationPollingStatus? status)
    {
        if (station is null) throw new ArgumentNullException(nameof(station));

        if (!station.Enabled)
        {
            return "已停用";
        }

        if (status?.LastReceivedAt is null)
        {
            return "等待通信";
        }

        return status.IsOnline ? "在线" : "离线";
    }

    public static string FormatLastReceivedTime(DateTimeOffset? lastReceivedAt) =>
        lastReceivedAt.HasValue
            ? lastReceivedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "—";

    public static bool HasEndpointMismatch(RfidStationConfig station, RfidStationPollingStatus? status)
    {
        if (station is null) throw new ArgumentNullException(nameof(station));
        if (status?.EndpointKey is not RfidStationEndpointKey runtimeEndpoint ||
            !station.TryResolveEndpoint(out var configuredEndpoint))
        {
            return false;
        }

        return runtimeEndpoint != new RfidStationEndpointKey(configuredEndpoint, station.ProtocolAddress);
    }
}
