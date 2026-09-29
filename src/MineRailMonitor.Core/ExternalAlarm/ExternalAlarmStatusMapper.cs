using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.ExternalAlarm;

public static class ExternalAlarmStatusMapper
{
    public static ExternalAlarmStatus FromLifecycleState(PassageLifecycleState lifecycleState) =>
        lifecycleState switch
        {
            PassageLifecycleState.Recognizing => ExternalAlarmStatus.Recognizing,
            PassageLifecycleState.Alarm => ExternalAlarmStatus.UncouplingAlarm,
            PassageLifecycleState.Clearing => ExternalAlarmStatus.UncouplingAlarm,
            PassageLifecycleState.WaitForEmpty => ExternalAlarmStatus.UncouplingAlarm,
            _ => ExternalAlarmStatus.Normal
        };

    public static ExternalAlarmStationSnapshot FromRuntimeState(StationRuntimeState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        return new ExternalAlarmStationSnapshot(
            state.StationAddress,
            FromLifecycleState(state.LifecycleState),
            state.DetectedVehicleCount);
    }
}
