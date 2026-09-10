using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Recognition;

public sealed class StationRecognitionSession
{
    private readonly TimeSpan _interVehicleTimeout;
    private readonly ushort _emptyRfidValue;
    private readonly List<ushort> _detectedVehicleRfids = new();
    private readonly HashSet<ushort> _detectedVehicleSet = new();
    private readonly List<IReadOnlyList<ushort>> _observedVehicleBatches = new();
    private readonly List<PassageRfidObservation> _rfidObservations = new();

    public StationRecognitionSession(
        byte stationAddress,
        int expectedVehicleCount,
        TimeSpan interVehicleTimeout,
        ushort emptyRfidValue = 0)
    {
        if (expectedVehicleCount < 1 || expectedVehicleCount > 14)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVehicleCount));
        }
        if (interVehicleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interVehicleTimeout));
        }
        StationAddress = stationAddress;
        ExpectedVehicleCount = expectedVehicleCount;
        _interVehicleTimeout = interVehicleTimeout;
        _emptyRfidValue = emptyRfidValue;
    }

    public byte StationAddress { get; }

    public ushort HeadRfid { get; private set; }

    public IReadOnlyList<ushort> DetectedVehicleRfids => _detectedVehicleRfids;

    public IReadOnlyList<ushort> ObservedVehicleSequence => _detectedVehicleRfids;

    public IReadOnlyCollection<ushort> SeenRfids => _detectedVehicleSet;

    /// <summary>
    /// New tags first seen in each polling response. Values in one batch have no intra-frame time order.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ushort>> ObservedVehicleBatches => _observedVehicleBatches;

    public IReadOnlyList<PassageRfidObservation> RfidObservations => _rfidObservations;

    public int DetectedVehicleCount => _detectedVehicleRfids.Count;

    public int ExpectedVehicleCount { get; }

    public DateTimeOffset? FirstVehicleAt { get; private set; }

    public DateTimeOffset? LastNewVehicleAt { get; private set; }

    public int HeadTagCount { get; private set; }

    public IReadOnlyList<ushort> HeadTagRfids { get; private set; } = Array.Empty<ushort>();

    public ushort? FirstValidRfid { get; private set; }

    public IReadOnlyList<RfidHeadWarning> HeadTagWarnings { get; private set; } = Array.Empty<RfidHeadWarning>();

    public bool HeadTagWarning => HeadTagWarnings.Count > 0;

    public StationRecognitionState State { get; private set; } = StationRecognitionState.Waiting;

    public byte ReportedCardCount { get; private set; }

    public int ActualNonZeroSlotCount { get; private set; }

    public bool ProtocolDataWarning { get; private set; }

    public void Apply(RfidStationFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        if (frame.StationAddress != StationAddress)
        {
            throw new ArgumentException("Frame station address does not match this session.", nameof(frame));
        }

        HeadRfid = frame.HeadRfid;
        ReportedCardCount = frame.ReportedCardCount;
        ActualNonZeroSlotCount = frame.ActualNonZeroSlotCount > 0 || frame.ValidRfids.Count > 0
            ? frame.ActualNonZeroSlotCount
            : frame.RawRfidSlots.Count(value => value != _emptyRfidValue);
        ProtocolDataWarning = frame.ProtocolDataWarning || ReportedCardCount != ActualNonZeroSlotCount;
        var analysis = RfidRecognitionRules.Analyze(frame, _emptyRfidValue);

        var added = false;
        var newBatch = new List<ushort>();
        foreach (var vehicleRfid in analysis.UniqueRfids)
        {
            if (_detectedVehicleSet.Add(vehicleRfid))
            {
                _detectedVehicleRfids.Add(vehicleRfid);
                newBatch.Add(vehicleRfid);
                added = true;
            }
        }

        if (newBatch.Count > 0)
        {
            var batchNo = _observedVehicleBatches.Count + 1;
            _observedVehicleBatches.Add(newBatch.AsReadOnly());
            foreach (var value in newBatch)
            {
                _rfidObservations.Add(new PassageRfidObservation(
                    _rfidObservations.Count + 1,
                    value,
                    frame.ReceivedAt,
                    batchNo));
            }
        }

        UpdateHeadDiagnostics();

        if (!added)
        {
            return;
        }
        if (DetectedVehicleCount >= 2)
        {
            FirstVehicleAt ??= frame.ReceivedAt;
            LastNewVehicleAt = frame.ReceivedAt;
        }

        State = DetectedVehicleCount >= ExpectedVehicleCount
            ? StationRecognitionState.Completed
            : DetectedVehicleCount >= 2
                ? StationRecognitionState.Recognizing
                : StationRecognitionState.Waiting;
    }

    private void UpdateHeadDiagnostics()
    {
        HeadTagRfids = _detectedVehicleRfids.Where(RfidRecognitionRules.IsHeadRfid).ToArray();
        HeadTagCount = HeadTagRfids.Count;
        FirstValidRfid = _detectedVehicleRfids.Count == 0 ? null : _detectedVehicleRfids[0];

        var warnings = new List<RfidHeadWarning>();
        if (_detectedVehicleRfids.Count > 0 && HeadTagCount == 0)
        {
            warnings.Add(RfidHeadWarning.MissingHeadTag);
        }
        if (FirstValidRfid.HasValue && !RfidRecognitionRules.IsHeadRfid(FirstValidRfid.Value))
        {
            warnings.Add(RfidHeadWarning.FirstTagIsNotHead);
        }
        if (HeadTagCount > 1)
        {
            warnings.Add(RfidHeadWarning.MultipleHeadTags);
        }

        HeadTagWarnings = warnings;
    }

    public void Evaluate(DateTimeOffset now)
    {
        if (State == StationRecognitionState.Recognizing &&
            LastNewVehicleAt.HasValue &&
            now - LastNewVehicleAt.Value >= _interVehicleTimeout)
        {
            State = StationRecognitionState.UncouplingAlarm;
        }
    }
}
