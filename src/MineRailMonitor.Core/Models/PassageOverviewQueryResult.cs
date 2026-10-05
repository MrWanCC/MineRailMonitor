namespace MineRailMonitor.Core.Models;

public sealed class PassageOverviewQueryResult
{
    public PassageOverviewQueryResult(
        IReadOnlyList<PassageRecord> recentPassages,
        int alarmCount,
        int unconfirmedAlarmCount,
        int acknowledgedAlarmCount,
        int recoveredAlarmCount)
    {
        RecentPassages = recentPassages ?? throw new ArgumentNullException(nameof(recentPassages));
        AlarmCount = alarmCount;
        UnconfirmedAlarmCount = unconfirmedAlarmCount;
        AcknowledgedAlarmCount = acknowledgedAlarmCount;
        RecoveredAlarmCount = recoveredAlarmCount;
    }

    public IReadOnlyList<PassageRecord> RecentPassages { get; }

    public int AlarmCount { get; }

    public int UnconfirmedAlarmCount { get; }

    public int AcknowledgedAlarmCount { get; }

    public int RecoveredAlarmCount { get; }
}
