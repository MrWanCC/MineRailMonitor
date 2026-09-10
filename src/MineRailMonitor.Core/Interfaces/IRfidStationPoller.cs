using System.Net;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Interfaces;

public interface IRfidStationPoller
{
    IReadOnlyDictionary<byte, RfidStationPollingStatus> StationStatuses { get; }

    Task RunAsync(CancellationToken cancellationToken);

    bool RecordResponse(IPEndPoint sourceEndpoint, byte protocolAddress, DateTimeOffset receivedAt);
}
