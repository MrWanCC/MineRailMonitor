namespace MineRailMonitor.Core.Interfaces;

public interface IRfidTransport : IDisposable
{
    Task<int> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken);

    Task SendAsync(byte[] buffer, CancellationToken cancellationToken);
}
