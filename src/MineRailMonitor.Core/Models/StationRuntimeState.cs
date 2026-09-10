using MineRailMonitor.Core.Recognition;

namespace MineRailMonitor.Core.Models;

public sealed class StationRuntimeState
{
    internal StationRuntimeState(byte stationAddress, int expectedVehicleCount, ushort emptyRfidValue)
        : this($"RFID-{stationAddress:X2}", $"RFID-{stationAddress:X2}", stationAddress, expectedVehicleCount, emptyRfidValue)
    {
    }

    internal StationRuntimeState(
        string stationId,
        string stationName,
        byte stationAddress,
        int expectedVehicleCount,
        ushort emptyRfidValue)
    {
        StationAddress = stationAddress;
        StationId = string.IsNullOrWhiteSpace(stationId) ? $"RFID-{stationAddress:X2}" : stationId;
        StationName = string.IsNullOrWhiteSpace(stationName) ? StationId : stationName;
        ExpectedVehicleCount = expectedVehicleCount;
        EmptyRfidValue = emptyRfidValue;
    }

    public string StationId { get; }

    public string StationName { get; }

    public byte StationAddress { get; }

    public PassageLifecycleState LifecycleState { get; internal set; } = PassageLifecycleState.Idle;

    public StationCommunicationState CommunicationState { get; internal set; } = StationCommunicationState.Offline;

    public RfidStationVisualState VisualState { get; internal set; } = RfidStationVisualState.Offline;

    public StationRecognitionSession? RecognitionSession { get; internal set; }

    public PassageRecord? LastPassageRecord { get; internal set; }

    public bool PersistenceWarning { get; internal set; }

    public string? PersistenceErrorMessage { get; internal set; }

    public int PersistenceAttemptCount { get; internal set; }

    public int ClearPersistenceAttemptCount { get; internal set; }

    public ushort? CurrentHeadRfid { get; internal set; }

    public IReadOnlyList<ushort> ObservedVehicleSequence { get; internal set; } = Array.Empty<ushort>();

    public IReadOnlyCollection<ushort> SeenRfids { get; internal set; } = Array.Empty<ushort>();

    public int DetectedVehicleCount { get; internal set; }

    public int ExpectedVehicleCount { get; internal set; }

    public ushort EmptyRfidValue { get; internal set; }

    public int HeadTagCount { get; internal set; }

    public IReadOnlyList<ushort> HeadTagRfids { get; internal set; } = Array.Empty<ushort>();

    public IReadOnlyList<string> WarningMessages { get; internal set; } = Array.Empty<string>();

    public string? AlarmMessage { get; internal set; }

    public DateTimeOffset? SessionStartedAt { get; internal set; }

    public DateTimeOffset? LastResponseAt { get; internal set; }

    public DateTimeOffset? LastNewVehicleAt { get; internal set; }

    public bool PendingClear { get; internal set; }

    public int ClearAttempts { get; internal set; }

    public int ConsecutiveEmptyReads { get; internal set; }
}
