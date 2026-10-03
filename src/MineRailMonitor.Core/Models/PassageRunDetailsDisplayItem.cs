namespace MineRailMonitor.Core.Models;

public sealed class PassageRunDetailsDisplayItem
{
    public Guid PassageId { get; init; }

    public string PassageIdText { get; init; } = string.Empty;

    public string StationIdText { get; init; } = string.Empty;

    public string OutcomeReasonText { get; init; } = string.Empty;

    public string AlarmOccurredAtText { get; init; } = "--";

    public string AlarmAcknowledgedAtText { get; init; } = "--";

    public string AlarmRecoveredAtText { get; init; } = "--";

    public string VehicleNo { get; init; } = string.Empty;

    public string VehicleNumber => VehicleNo;

    public string VehicleName { get; init; } = string.Empty;

    public string HeadRfidText { get; init; } = string.Empty;

    public string ExpectedCountText { get; init; } = string.Empty;

    public string ActualCountText { get; init; } = string.Empty;

    public string MissingCountText { get; init; } = string.Empty;

    public string CurrentStatusText { get; init; } = string.Empty;

    public string StatusKind { get; init; } = string.Empty;

    public IReadOnlyList<PassageEventTimelineItem> Timeline { get; init; } = Array.Empty<PassageEventTimelineItem>();
}
