namespace MineRailMonitor.Core.Models;

public enum PassageLifecycleState
{
    Idle,
    Recognizing,
    Completed,
    Alarm,
    Finalizing,
    Clearing,
    WaitForEmpty
}
