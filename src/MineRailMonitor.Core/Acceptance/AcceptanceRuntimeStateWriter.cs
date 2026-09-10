using System.Text.Json;
using System.Text.Json.Serialization;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Acceptance;

public sealed class AcceptanceRuntimeStateWriter : IDisposable
{
    private const int MaxHistoryEntries = 512;
    private readonly object _syncRoot = new();
    private readonly string _path;
    private readonly RfidRuntimeCoordinator _coordinator;
    private readonly List<AcceptanceRuntimeHistoryEntry> _history = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public AcceptanceRuntimeStateWriter(string path, RfidRuntimeCoordinator coordinator)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Runtime state path must not be empty.", nameof(path));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _path = Path.GetFullPath(path);
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public void Write(string trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger)) throw new ArgumentException("Snapshot trigger must not be empty.", nameof(trigger));

        lock (_syncRoot)
        {
            var capturedAt = DateTimeOffset.Now;
            var stations = SnapshotStations();
            var historyEntry = new AcceptanceRuntimeHistoryEntry
            {
                CapturedAt = capturedAt,
                Trigger = trigger,
                Stations = stations
            };
            _history.Add(historyEntry);
            if (_history.Count > MaxHistoryEntries)
            {
                _history.RemoveAt(0);
            }

            var snapshot = new AcceptanceRuntimeSnapshot
            {
                CapturedAt = capturedAt,
                Trigger = trigger,
                Stations = stations,
                History = _history.ToArray()
            };
            AtomicFileWriter.WriteAllText(_path, JsonSerializer.Serialize(snapshot, _jsonOptions));
        }
    }

    public void Dispose()
    {
    }

    private IReadOnlyList<AcceptanceRuntimeStationSnapshot> SnapshotStations() =>
        _coordinator.States.Values
            .OrderBy(state => state.StationAddress)
            .Select(state => new AcceptanceRuntimeStationSnapshot
            {
                StationId = state.StationId,
                StationName = state.StationName,
                StationAddress = state.StationAddress,
                LifecycleState = state.LifecycleState,
                CommunicationState = state.CommunicationState,
                VisualState = state.VisualState,
                PassageId = state.LastPassageRecord?.PassageId.ToString("D"),
                HeadRfid = state.CurrentHeadRfid?.ToString("X4"),
                Expected = state.ExpectedVehicleCount,
                Detected = state.DetectedVehicleCount,
                Result = state.LastPassageRecord?.Outcome,
                ClearState = state.LastPassageRecord?.ClearState,
                PendingClear = state.PendingClear,
                ConsecutiveEmptyReads = state.ConsecutiveEmptyReads,
                Warning = state.WarningMessages.ToArray(),
                ObservedRfids = state.ObservedVehicleSequence.Select(value => value.ToString("X4")).ToArray()
            })
            .ToArray();
}

public sealed class AcceptanceRuntimeSnapshot
{
    public DateTimeOffset CapturedAt { get; set; }

    public string Trigger { get; set; } = string.Empty;

    public IReadOnlyList<AcceptanceRuntimeStationSnapshot> Stations { get; set; } = Array.Empty<AcceptanceRuntimeStationSnapshot>();

    public IReadOnlyList<AcceptanceRuntimeHistoryEntry> History { get; set; } = Array.Empty<AcceptanceRuntimeHistoryEntry>();
}

public sealed class AcceptanceRuntimeHistoryEntry
{
    public DateTimeOffset CapturedAt { get; set; }

    public string Trigger { get; set; } = string.Empty;

    public IReadOnlyList<AcceptanceRuntimeStationSnapshot> Stations { get; set; } = Array.Empty<AcceptanceRuntimeStationSnapshot>();
}

public sealed class AcceptanceRuntimeStationSnapshot
{
    public string StationId { get; set; } = string.Empty;

    public string StationName { get; set; } = string.Empty;

    public byte StationAddress { get; set; }

    public PassageLifecycleState LifecycleState { get; set; }

    public StationCommunicationState CommunicationState { get; set; }

    public RfidStationVisualState VisualState { get; set; }

    public string? PassageId { get; set; }

    public string? HeadRfid { get; set; }

    public int Expected { get; set; }

    public int Detected { get; set; }

    public PassageOutcome? Result { get; set; }

    public PassageClearState? ClearState { get; set; }

    public bool PendingClear { get; set; }

    public int ConsecutiveEmptyReads { get; set; }

    public IReadOnlyList<string> Warning { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ObservedRfids { get; set; } = Array.Empty<string>();
}
