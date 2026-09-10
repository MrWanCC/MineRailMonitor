using System.Net;
using System.Net.Sockets;

namespace MineRailMonitor.Simulator.Communication;

public sealed class SimulatorUdpTransport : IDisposable
{
    private readonly UdpClient _client;
    private readonly IPEndPoint _remoteEndPoint;

    public SimulatorUdpTransport(IPAddress localAddress, int localPort, IPAddress remoteAddress, int remotePort)
    {
        _client = new UdpClient(new IPEndPoint(localAddress, localPort));
        _remoteEndPoint = new IPEndPoint(remoteAddress, remotePort);
    }

    public Task SendAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return _client.SendAsync(buffer, buffer.Length, _remoteEndPoint);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
