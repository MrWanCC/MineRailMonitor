using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Interfaces;

public interface IRfidPollCommandProvider
{
    RfidPollCommand GetCommand(byte stationAddress);

    void MarkCommandSent(byte stationAddress, RfidPollCommand command, DateTimeOffset sentAt);
}

/// <summary>
/// Endpoint-aware command provider used when multiple stations share a protocol address.
/// The legacy byte-based interface remains available for existing callers.
/// </summary>
public interface IRfidEndpointPollCommandProvider
{
    RfidPollCommand GetCommand(RfidStationConfig station);

    void MarkCommandSent(RfidStationConfig station, RfidPollCommand command, DateTimeOffset sentAt);
}
