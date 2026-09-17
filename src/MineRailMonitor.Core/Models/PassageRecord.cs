namespace MineRailMonitor.Core.Models;

public sealed class PassageRecord
{
    public const string LegacyStationId = "Legacy";

    public PassageRecord(
        Guid passageId,
        string stationId,
        byte stationAddress,
        ushort? headRfid,
        IEnumerable<ushort> observedRfids,
        int expectedVehicleCount,
        PassageOutcome outcome,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        IEnumerable<string>? warningMessages = null,
        string? alarmMessage = null,
        IEnumerable<PassageRfidObservation>? rfidObservations = null,
        PassageClearState clearState = PassageClearState.PendingClear,
        DateTimeOffset? clearedAt = null,
        DateTimeOffset? createdAt = null)
    {
        if (passageId == Guid.Empty)
        {
            throw new ArgumentException("PassageId must not be empty.", nameof(passageId));
        }
        if (string.IsNullOrWhiteSpace(stationId))
        {
            throw new ArgumentException("StationId must not be empty.", nameof(stationId));
        }
        if (observedRfids is null)
        {
            throw new ArgumentNullException(nameof(observedRfids));
        }
        if (expectedVehicleCount < 1 || expectedVehicleCount > 14)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVehicleCount));
        }

        PassageId = passageId;
        StationId = stationId;
        StationAddress = stationAddress;
        HeadRfid = headRfid;
        var observedValues = observedRfids.ToArray();
        var suppliedObservations = rfidObservations?.ToArray();
        if (suppliedObservations is not null &&
            (suppliedObservations.Length != observedValues.Length ||
             suppliedObservations.Select(item => item.RfidValue).SequenceEqual(observedValues) == false))
        {
            throw new ArgumentException("RFID明细必须与观察序列一致。", nameof(rfidObservations));
        }

        ObservedRfids = Array.AsReadOnly(observedValues);
        RfidObservations = Array.AsReadOnly(suppliedObservations ?? observedValues
            .Select((value, index) => new PassageRfidObservation(index + 1, value, startedAt, 1))
            .ToArray());
        ExpectedVehicleCount = expectedVehicleCount;
        DetectedVehicleCount = ObservedRfids.Count;
        Outcome = outcome;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        WarningMessages = Array.AsReadOnly((warningMessages ?? Array.Empty<string>()).ToArray());
        AlarmMessage = alarmMessage;
        ClearState = clearState;
        ClearedAt = clearedAt;
        CreatedAt = createdAt ?? completedAt;
    }

    public Guid PassageId { get; }

    public string StationId { get; }

    public byte StationAddress { get; }

    public ushort? HeadRfid { get; }

    public IReadOnlyList<ushort> ObservedRfids { get; }

    public IReadOnlyList<ushort> ObservedVehicleSequence => ObservedRfids;

    public IReadOnlyList<PassageRfidObservation> RfidObservations { get; }

    public int ExpectedVehicleCount { get; }

    public int DetectedVehicleCount { get; }

    public PassageOutcome Outcome { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CompletedAt { get; }

    public IReadOnlyList<string> WarningMessages { get; }

    public bool HasWarnings => WarningMessages.Count > 0;

    public bool IsAlert => Outcome == PassageOutcome.UncouplingAlarm || HasWarnings;

    public bool IsWarningOnly => HasWarnings && Outcome != PassageOutcome.UncouplingAlarm;

    public string? AlarmMessage { get; }

    public PassageClearState ClearState { get; }

    public DateTimeOffset? ClearedAt { get; }

    public DateTimeOffset CreatedAt { get; }
}
