namespace MineRailMonitor.Core.Interfaces;

public interface IExternalDataInterface
{
    Task SendAsync(byte[] payload, CancellationToken cancellationToken);
}
