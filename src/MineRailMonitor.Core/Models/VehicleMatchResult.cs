namespace MineRailMonitor.Core.Models;

public sealed class VehicleMatchResult
{
    public bool IsMatched { get; init; }

    public ushort? HeadRfid { get; init; }

    public string VehicleNo { get; init; } = string.Empty;

    public string VehicleName { get; init; } = string.Empty;

    public int? ExpectedVehicleCount { get; init; }

    public int? ExpectedCount => ExpectedVehicleCount;

    public VehicleMatchSource Source { get; init; }
}

public enum VehicleMatchSource
{
    VehicleConfiguration,
    StationConfiguration,
    GlobalConfiguration,
    Unconfigured
}
