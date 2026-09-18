using System.Net;

namespace MineRailMonitor.Core.Communication;

public interface IRfidUdpTransport : IDisposable
{
    IPEndPoint LocalEndPoint { get; }

    event EventHandler<RfidUdpDatagramEventArgs>? DatagramReceived;

    event EventHandler<RfidUdpDatagramSentEventArgs>? DatagramSent;

    event Action<Exception>? ReceiveError;

    Task StartAsync(CancellationToken cancellationToken);

    void Stop();
}
