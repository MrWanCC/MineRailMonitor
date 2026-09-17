namespace MineRailMonitor.Core.Models;

public readonly struct RfidStationStatusSummary
{
    private RfidStationStatusSummary(int onlineCount, int offlineCount, int waitingCount)
    {
        OnlineCount = onlineCount;
        OfflineCount = offlineCount;
        WaitingCount = waitingCount;
    }

    public int OnlineCount { get; }

    public int OfflineCount { get; }

    public int WaitingCount { get; }

    public int ActiveCount => OnlineCount + OfflineCount;

    public double? OnlineRatePercentage => ActiveCount == 0
        ? null
        : OnlineCount * 100d / ActiveCount;

    public static RfidStationStatusSummary Calculate(IEnumerable<RfidStationPollingStatus> statuses)
    {
        if (statuses is null) throw new ArgumentNullException(nameof(statuses));

        var onlineCount = 0;
        var offlineCount = 0;
        var waitingCount = 0;
        foreach (var status in statuses.Where(status => status is not null))
        {
            if (!status.LastSentAt.HasValue)
            {
                waitingCount++;
            }
            else if (status.IsOnline)
            {
                onlineCount++;
            }
            else
            {
                offlineCount++;
            }
        }

        return new RfidStationStatusSummary(onlineCount, offlineCount, waitingCount);
    }
}
