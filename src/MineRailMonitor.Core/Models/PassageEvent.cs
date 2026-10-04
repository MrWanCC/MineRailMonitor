namespace MineRailMonitor.Core.Models;

public sealed class PassageEvent
{
    public Guid PassageId { get; init; }

    public string EventType { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; }

    public string StationId { get; init; } = string.Empty;

    public byte StationAddress { get; init; }

    public string? VehicleName { get; init; }

    public string? VehicleNo { get; init; }

    public ushort? VehicleHeadRfid { get; init; }

    public int? ExpectedCount { get; init; }

    public int? DetectedCount { get; init; }

    public int? MissingCount { get; init; }

    public long? DurationMilliseconds { get; init; }

    public int? TimeoutSeconds { get; init; }

    public int? FailureCount { get; init; }

    public string? Reason { get; init; }
}

public static class PassageEventTypes
{
    public const string Start = "PASSAGE_START";

    public const string Progress = "PASSAGE_PROGRESS";

    public const string Complete = "PASSAGE_COMPLETE";

    public const string Alarm = "PASSAGE_ALARM";

    public const string Unverified = "PASSAGE_UNVERIFIED";

    public const string Clear = "PASSAGE_CLEAR";

    public const string VehicleMatchLateResolved = "VEHICLE_MATCH_LATE_RESOLVED";

    public const string PersistenceFailed = "PASSAGE_PERSISTENCE_FAILED";

    public const string PersistenceRecovered = "PASSAGE_PERSISTENCE_RECOVERED";
}
