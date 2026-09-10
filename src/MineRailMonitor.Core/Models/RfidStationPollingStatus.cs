namespace MineRailMonitor.Core.Models;

public sealed class RfidStationPollingStatus
{
    public byte StationAddress { get; set; }

    public DateTimeOffset? LastRequestAt { get; internal set; }

    public DateTimeOffset? LastResponseAt { get; internal set; }

    public long RequestCount { get; internal set; }

    public long ResponseCount { get; internal set; }

    public DateTimeOffset? LastErrorAt { get; internal set; }

    public string? LastError { get; internal set; }
}
