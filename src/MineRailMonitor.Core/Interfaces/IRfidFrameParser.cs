using System.Net;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Interfaces;

public interface IRfidFrameParser
{
    bool TryParse(byte[] buffer, IPEndPoint sourceEndpoint, DateTimeOffset receivedAt, out RfidStationFrame? frame);
}
