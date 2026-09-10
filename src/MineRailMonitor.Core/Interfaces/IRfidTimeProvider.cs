namespace MineRailMonitor.Core.Interfaces;

public interface IRfidTimeProvider
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
