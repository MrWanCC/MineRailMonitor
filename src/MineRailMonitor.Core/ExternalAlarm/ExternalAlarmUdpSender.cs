using System.Net;
using System.Net.Sockets;

namespace MineRailMonitor.Core.ExternalAlarm;

public sealed class ExternalAlarmUdpSender : IExternalAlarmStatusSender, IDisposable
{
    private readonly UdpClient _client;
    private readonly IPEndPoint _target;
    private bool _disposed;

    public ExternalAlarmUdpSender(IPEndPoint target, IPEndPoint? localEndpoint = null)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        _target = new IPEndPoint(target.Address, target.Port);
        _client = localEndpoint is null
            ? new UdpClient()
            : new UdpClient(new IPEndPoint(localEndpoint.Address, localEndpoint.Port));
    }

    public IPEndPoint Target => new(_target.Address, _target.Port);

    public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        await _client.SendAsync(payload, payload.Length, _target).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ExternalAlarmUdpSender));
        }
    }
}
