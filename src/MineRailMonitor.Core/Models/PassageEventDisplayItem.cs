namespace MineRailMonitor.Core.Models;

public sealed class PassageEventDisplayItem
{
    public Guid PassageId { get; init; }

    public string StationIdText { get; init; } = string.Empty;

    public string TimeText { get; init; } = string.Empty;

    public string EventTypeText { get; init; } = string.Empty;

    public string VehicleNo { get; init; } = string.Empty;

    public string VehicleNumber => VehicleNo;

    public string VehicleName { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string StatusKind { get; init; } = string.Empty;

    public string HeadRfidText { get; init; } = string.Empty;

    public string ExpectedCountText { get; init; } = string.Empty;

    public string ActualCountText { get; init; } = string.Empty;

    public string MissingCountText { get; init; } = string.Empty;

    public string PassageIdText { get; init; } = string.Empty;
}
