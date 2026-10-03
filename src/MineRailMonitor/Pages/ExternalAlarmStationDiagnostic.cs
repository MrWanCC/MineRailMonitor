using MineRailMonitor.Core.ExternalAlarm;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Pages;

public sealed class ExternalAlarmStationDiagnostic
{
    public string YardId { get; set; } = string.Empty;

    public string StationId { get; set; } = string.Empty;

    public byte StationAddress { get; set; }

    public PassageLifecycleState RuntimeState { get; set; }

    public ExternalAlarmStatus? ExternalStatus { get; set; }

    public int VehicleCount { get; set; }

    public DateTimeOffset? LastUpdatedAt { get; set; }
}
