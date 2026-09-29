namespace MineRailMonitor.Core.ExternalAlarm;

/// <summary>
/// Tracks the last successfully sent status for each yard/station key so
/// repeated scheduler ticks do not become repeated status-change log entries.
/// </summary>
public sealed class ExternalAlarmStatusChangeTracker
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, ExternalAlarmStatus> _lastStatuses =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryRecord(
        string stationKey,
        ExternalAlarmStatus status,
        out ExternalAlarmStatus? previousStatus)
    {
        if (string.IsNullOrWhiteSpace(stationKey))
        {
            throw new ArgumentException("外部报警状态变化跟踪键不能为空。", nameof(stationKey));
        }

        lock (_syncRoot)
        {
            var key = stationKey.Trim();
            if (_lastStatuses.TryGetValue(key, out var previous))
            {
                previousStatus = previous;
                if (previous == status)
                {
                    return false;
                }
            }
            else
            {
                previousStatus = null;
            }

            _lastStatuses[key] = status;
            return true;
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _lastStatuses.Clear();
        }
    }
}
