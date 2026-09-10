using System.Net;

namespace MineRailMonitor.Core.Interfaces;

public interface IRfidRequestSender
{
    Task SendAsync(byte[] request, IPEndPoint destination, CancellationToken cancellationToken);
}
