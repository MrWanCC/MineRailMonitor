using System.Net;
using System.Net.Sockets;

namespace MineRailMonitor.Simulator.Communication;

public sealed class SimulatorUdpResponder : IDisposable
{
    private readonly UdpClient _client;

    public SimulatorUdpResponder(IPAddress localAddress, int localPort)
    {
        _client = new UdpClient(new IPEndPoint(localAddress, localPort));
        LocalEndPoint = (IPEndPoint)_client.Client.LocalEndPoint!;
    }

    public IPEndPoint LocalEndPoint { get; }

    // Read-only UI telemetry hook. It does not alter the request/response flow.
    public event Action<IPEndPoint, byte[], byte[]?>? PacketHandled;

    public async Task RunAsync(Func<byte[], byte[]?> responseFactory, CancellationToken cancellationToken)
    {
        if (responseFactory is null)
        {
            throw new ArgumentNullException(nameof(responseFactory));
        }

        using var registration = cancellationToken.Register(_client.Close);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var request = await _client.ReceiveAsync().ConfigureAwait(false);
                var response = responseFactory(request.Buffer);
                if (response is not null)
                {
                    await _client.SendAsync(response, response.Length, request.RemoteEndPoint).ConfigureAwait(false);
                }

                PacketHandled?.Invoke(request.RemoteEndPoint, request.Buffer, response);
            }
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void Dispose() => _client.Dispose();
}
