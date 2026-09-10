namespace MineRailMonitor.Core.Models;

public sealed class TrainSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string StationId { get; set; } = string.Empty;

    public ulong HeadRfid { get; set; }

    public int ExpectedVehicleCount { get; set; }

    public int DetectedVehicleCount { get; set; }

    public IReadOnlyList<ulong> VehicleRfids { get; set; } = new List<ulong>();

    public DateTimeOffset StartTime { get; set; }

    public DateTimeOffset LastReadTime { get; set; }

    public TrainSessionStatus Status { get; set; } = TrainSessionStatus.Identifying;
}
