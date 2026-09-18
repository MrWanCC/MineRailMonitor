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

    public async Task RunAsync(
        Func<byte[], IPEndPoint, CancellationToken, Task<byte[]?>> responseFactory,
        CancellationToken cancellationToken)
    {
        if (responseFactory is null)
        {
            throw new ArgumentNullException(nameof(responseFactory));
        }

        using var registration = cancellationToken.Register(_client.Close);
        var pendingResponses = new List<Task>();
        var receiveTask = _client.ReceiveAsync();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var waitTasks = new List<Task>(pendingResponses.Count + 1) { receiveTask };
                waitTasks.AddRange(pendingResponses);
                var completed = await Task.WhenAny(waitTasks).ConfigureAwait(false);
                if (ReferenceEquals(completed, receiveTask))
                {
                    var request = await receiveTask.ConfigureAwait(false);
                    pendingResponses.Add(ProcessRequestAsync(request, responseFactory, cancellationToken));
                    receiveTask = _client.ReceiveAsync();
                    continue;
                }

                pendingResponses.Remove(completed);
                await completed.ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _client.Close();
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }

            if (pendingResponses.Count > 0)
            {
                try
                {
                    await Task.WhenAll(pendingResponses).ConfigureAwait(false);
                }
                catch
                {
                    // Awaiting the set observes every response task. The original receive or
                    // response exception, if any, is preserved by the outer flow.
                }
            }
        }
    }

    private async Task ProcessRequestAsync(
        UdpReceiveResult request,
        Func<byte[], IPEndPoint, CancellationToken, Task<byte[]?>> responseFactory,
        CancellationToken cancellationToken)
    {
        var response = await responseFactory(request.Buffer, request.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
        if (response is not null)
        {
            await _client.SendAsync(response, response.Length, request.RemoteEndPoint).ConfigureAwait(false);
        }

        PacketHandled?.Invoke(request.RemoteEndPoint, request.Buffer, response);
    }

    public void Dispose() => _client.Dispose();
}
