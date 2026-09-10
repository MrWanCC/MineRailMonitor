namespace MineRailMonitor.Core.Models;

public sealed class ProjectConfig
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DefaultStationId { get; set; } = string.Empty;

    public IReadOnlyList<StationConfig> Stations { get; set; } = new List<StationConfig>();

    public RfidSettings RfidSettings { get; set; } = new();

    public IReadOnlyList<RfidStationConfig> RfidStations { get; set; } = new List<RfidStationConfig>();
}
