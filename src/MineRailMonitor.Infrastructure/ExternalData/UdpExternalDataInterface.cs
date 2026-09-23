using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Core.Interfaces;

namespace MineRailMonitor.Infrastructure.ExternalData;

/// <summary>
/// Sends external alarm payloads as raw UDP datagrams.
/// </summary>
public sealed class UdpExternalDataInterface : IExternalDataInterface, IDisposable
{
    private readonly UdpClient _client = new();
    private readonly IPEndPoint _target;
    private bool _disposed;

    public UdpExternalDataInterface(IPEndPoint target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        _target = new IPEndPoint(target.Address, target.Port);
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
            throw new ObjectDisposedException(nameof(UdpExternalDataInterface));
        }
    }
}
