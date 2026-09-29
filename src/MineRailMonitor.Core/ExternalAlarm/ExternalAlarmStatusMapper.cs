using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.ExternalAlarm;

public static class ExternalAlarmStatusMapper
{
    public static ExternalAlarmStatus FromLifecycleState(PassageLifecycleState lifecycleState) =>
        lifecycleState switch
        {
            PassageLifecycleState.Idle => ExternalAlarmStatus.NoVehicle,
            PassageLifecycleState.Recognizing => ExternalAlarmStatus.Recognizing,
            PassageLifecycleState.Completed => ExternalAlarmStatus.Normal,
            PassageLifecycleState.Alarm => ExternalAlarmStatus.UncouplingAlarm,
            PassageLifecycleState.Clearing => ExternalAlarmStatus.UncouplingAlarm,
            PassageLifecycleState.WaitForEmpty => ExternalAlarmStatus.UncouplingAlarm,
            PassageLifecycleState.Finalizing => ExternalAlarmStatus.Normal,
            _ => ExternalAlarmStatus.NoVehicle
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
