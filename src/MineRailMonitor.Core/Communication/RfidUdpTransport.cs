using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Core.Interfaces;

namespace MineRailMonitor.Core.Communication;

public sealed class RfidUdpTransport : IRfidUdpTransport, IRfidRequestSender
{
    private readonly object _syncRoot = new();
    private readonly UdpClient _client;
    private CancellationTokenSource? _stopCts;
    private Task? _receiveTask;
    private bool _stopped;
    private bool _disposed;

    public RfidUdpTransport(IPAddress localAddress, int localPort)
    {
        if (localAddress is null)
        {
            throw new ArgumentNullException(nameof(localAddress));
        }

        _client = new UdpClient(new IPEndPoint(localAddress, localPort));
        LocalEndPoint = (IPEndPoint)_client.Client.LocalEndPoint!;
    }

    public IPEndPoint LocalEndPoint { get; }

    public event EventHandler<RfidUdpDatagramEventArgs>? DatagramReceived;

    public event EventHandler<RfidUdpDatagramSentEventArgs>? DatagramSent;

    public event Action<Exception>? ReceiveError;

    public async Task SendAsync(byte[] request, IPEndPoint destination, CancellationToken cancellationToken)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _client.SendAsync(request, request.Length, destination).ConfigureAwait(false);

        var args = new RfidUdpDatagramSentEventArgs(
            request,
            destination,
            DateTimeOffset.Now,
            LocalEndPoint);

        try
        {
            DatagramSent?.Invoke(this, args);
        }
        catch (Exception exception)
        {
            Trace.TraceError("RFID UDP 发送旁路观察者发生异常，不影响已成功发送的报文：{0}", exception);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            if (_stopped)
            {
                throw new InvalidOperationException("The UDP transport has been stopped.");
            }

            if (_receiveTask is not null)
            {
                throw new InvalidOperationException("The UDP transport is already running.");
            }

            _stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveTask = ReceiveLoopAsync(_stopCts.Token);
            return _receiveTask;
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            if (_disposed || _stopped)
            {
                return;
            }

            _stopped = true;
            _stopCts?.Cancel();
            _client.Close();
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopCts?.Dispose();
            _client.Dispose();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        using var cancellationRegistration = cancellationToken.Register(_client.Close);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _client.ReceiveAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionReset)
                {
                    Trace.TraceWarning("RFID UDP 接收遇到 ConnectionReset，继续监听：{0}", exception.Message);
                    continue;
                }

                var args = new RfidUdpDatagramEventArgs(
                    result.Buffer,
                    new IPEndPoint(result.RemoteEndPoint.Address, result.RemoteEndPoint.Port),
                    DateTimeOffset.Now);
                RaiseDatagramReceived(args);
            }
        }
        catch (Exception exception)
        {
            RaiseReceiveError(exception);
        }
    }

    private void RaiseDatagramReceived(RfidUdpDatagramEventArgs args)
    {
        try
        {
            DatagramReceived?.Invoke(this, args);
        }
        catch (Exception exception)
        {
            RaiseReceiveError(exception);
        }
    }

    private void RaiseReceiveError(Exception exception)
    {
        try
        {
            ReceiveError?.Invoke(exception);
        }
        catch
        {
            // Observers must not bring down the receive loop.
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RfidUdpTransport));
        }
    }
}
