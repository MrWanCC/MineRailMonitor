namespace MineRailMonitor.Infrastructure.Persistence;

public interface IAsyncDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
