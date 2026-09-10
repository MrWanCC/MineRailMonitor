using MineRailMonitor.Core.Interfaces;

namespace MineRailMonitor.Core.Communication;

public sealed class SystemRfidTimeProvider : IRfidTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
