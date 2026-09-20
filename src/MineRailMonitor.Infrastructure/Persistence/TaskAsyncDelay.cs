namespace MineRailMonitor.Infrastructure.Persistence;

public sealed class TaskAsyncDelay : IAsyncDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
