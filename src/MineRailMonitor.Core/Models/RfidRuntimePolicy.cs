namespace MineRailMonitor.Core.Models;

public sealed class RfidRuntimePolicy
{
    public RfidRuntimePolicy(
        TimeSpan offlineTimeout,
        int maxClearAttempts = 3,
        int emptyConfirmationReads = 2,
        int maxPersistenceAttempts = 3)
    {
        if (offlineTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(offlineTimeout));
        }
        if (maxClearAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxClearAttempts));
        }
        if (emptyConfirmationReads < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(emptyConfirmationReads));
        }
        if (maxPersistenceAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPersistenceAttempts));
        }

        OfflineTimeout = offlineTimeout;
        MaxClearAttempts = maxClearAttempts;
        EmptyConfirmationReads = emptyConfirmationReads;
        MaxPersistenceAttempts = maxPersistenceAttempts;
    }

    public static RfidRuntimePolicy Default { get; } = new(TimeSpan.FromSeconds(5));

    public TimeSpan OfflineTimeout { get; }

    public int MaxClearAttempts { get; }

    public int EmptyConfirmationReads { get; }

    public int MaxPersistenceAttempts { get; }
}
