using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Recognition;

namespace MineRailMonitor.Core.Services;

public sealed class RfidRuntimeCoordinator : IRfidPollCommandProvider, IRfidEndpointPollCommandProvider
{
    private readonly object _syncRoot = new();
    private readonly IReadOnlyList<RfidStationBinding> _bindings;
    private readonly Dictionary<RfidStationEndpointKey, StationRuntimeState> _statesByEndpoint;
    private readonly IReadOnlyDictionary<byte, IReadOnlyList<StationRuntimeState>> _statesByProtocol;
    private readonly RfidProtocolAddressCollectionView<StationRuntimeState> _states;
    private readonly IPassageRecordStore _recordStore;
    private readonly RfidRuntimePolicy _policy;
    private RfidSettings _defaults;

    public RfidRuntimeCoordinator(
        IEnumerable<byte> stationAddresses,
        RfidSettings settings,
        IPassageRecordStore recordStore,
        RfidRuntimePolicy? policy = null)
        : this(CreateCompatibilityStations(stationAddresses), settings, recordStore, policy)
    {
    }

    public RfidRuntimeCoordinator(
        IEnumerable<RfidStationConfig> stations,
        RfidSettings settings,
        IPassageRecordStore recordStore,
        RfidRuntimePolicy? policy = null)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));
        _defaults = CopySettings(settings ?? throw new ArgumentNullException(nameof(settings)));
        var errors = _defaults.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(settings));
        }

        _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        _policy = policy ?? RfidRuntimePolicy.Default;
        var enabledStations = stations.Where(station => station.Enabled).ToArray();
        if (enabledStations.Length == 0)
        {
            throw new ArgumentException("At least one enabled RFID station is required.", nameof(stations));
        }
        var duplicateCommunicationKeys = RfidStationConfig.FindDuplicateCommunicationKeys(enabledStations);
        if (duplicateCommunicationKeys.Count > 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, duplicateCommunicationKeys), nameof(stations));
        }

        _bindings = enabledStations
            .Select((station, index) => new RfidStationBinding(
                station,
                CreateStationKey(station, index),
                new StationRuntimeState(
                    station.StationId,
                    station.Name,
                    station.ProtocolAddress,
                    _defaults.ExpectedVehicleCount,
                    _defaults.EmptyRfidValue)))
            .ToArray();
        _statesByEndpoint = _bindings.ToDictionary(binding => binding.Key, binding => binding.State);
        _statesByProtocol = _bindings
            .GroupBy(binding => binding.Config.ProtocolAddress)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<StationRuntimeState>)group.Select(binding => binding.State).ToArray());
        _states = new RfidProtocolAddressCollectionView<StationRuntimeState>(
            _bindings.Select(binding => binding.State),
            state => state.StationAddress);
    }

    public IReadOnlyDictionary<byte, StationRuntimeState> States => _states;

    public IReadOnlyDictionary<RfidStationEndpointKey, StationRuntimeState> EndpointStates => _statesByEndpoint;

    public TimeSpan OfflineTimeout => _policy.OfflineTimeout;

    public event Action<byte, RfidPollCommand, DateTimeOffset>? CommandSent;

    public event Action<RfidStationConfig, RfidPollCommand, DateTimeOffset>? StationCommandSent;

    public void UpdateDefaults(RfidSettings settings)
    {
        lock (_syncRoot)
        {
            _defaults = CopySettings(settings ?? throw new ArgumentNullException(nameof(settings)));
            var errors = _defaults.Validate();
            if (errors.Count > 0)
            {
                throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(settings));
            }
        }
    }

    public StationRecognitionSession? ProcessFrame(RfidStationFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        lock (_syncRoot)
        {
            return ProcessFrameCore(frame);
        }
    }

    private StationRecognitionSession? ProcessFrameCore(RfidStationFrame frame)
    {
        var state = FindState(frame);
        if (state is null)
        {
            return null;
        }

        state.CommunicationState = StationCommunicationState.Online;
        state.LastResponseAt = frame.ReceivedAt;

        if (state.LifecycleState == PassageLifecycleState.Clearing)
        {
            state.LifecycleState = PassageLifecycleState.WaitForEmpty;
            ProcessWaitForEmptyFrame(state, frame);
            UpdateVisualState(state);
            return state.RecognitionSession;
        }

        if (state.LifecycleState == PassageLifecycleState.Finalizing)
        {
            TryPersistPendingPassage(state);
            UpdateVisualState(state);
            return state.RecognitionSession;
        }

        if (state.LifecycleState == PassageLifecycleState.WaitForEmpty)
        {
            ProcessWaitForEmptyFrame(state, frame);
            UpdateVisualState(state);
            return state.RecognitionSession;
        }

        if (state.LifecycleState is PassageLifecycleState.Completed or PassageLifecycleState.Alarm)
        {
            UpdateVisualState(state);
            return state.RecognitionSession;
        }

        if (state.RecognitionSession is null)
        {
            if (!HasNonEmptySlot(frame, _defaults.EmptyRfidValue))
            {
                UpdateVisualState(state);
                return null;
            }

            state.RecognitionSession = new StationRecognitionSession(
                frame.StationAddress,
                _defaults.ExpectedVehicleCount,
                TimeSpan.FromSeconds(_defaults.InterVehicleTimeoutSeconds),
                _defaults.EmptyRfidValue);
            state.SessionStartedAt = frame.ReceivedAt;
            state.ExpectedVehicleCount = _defaults.ExpectedVehicleCount;
            state.LastPassageRecord = null;
            state.AlarmMessage = null;
            state.EmptyRfidValue = _defaults.EmptyRfidValue;
            state.ClearAttempts = 0;
            state.ConsecutiveEmptyReads = 0;
        }

        state.RecognitionSession.Apply(frame);
        UpdateFromRecognition(state);
        state.LifecycleState = PassageLifecycleState.Recognizing;
        if (state.RecognitionSession.State == StationRecognitionState.Completed)
        {
            FreezePassage(state, PassageOutcome.Completed, frame.ReceivedAt);
        }
        UpdateVisualState(state);
        return state.RecognitionSession;
    }

    private StationRuntimeState? FindState(RfidStationFrame frame)
    {
        if (IsUsableEndpoint(frame.SourceEndpoint))
        {
            var key = new RfidStationEndpointKey(frame.SourceEndpoint, frame.StationAddress);
            if (_statesByEndpoint.TryGetValue(key, out var endpointState))
            {
                return endpointState;
            }

            // Compatibility-only coordinators have no configured endpoint and
            // historically accepted frames from any source.
            return CanUseProtocolFallback(frame.StationAddress) &&
                TryGetFirstState(frame.StationAddress, out var compatibilityState)
                ? compatibilityState
                : null;
        }

        // Frames created by legacy callers do not carry a source endpoint. They
        // remain unambiguous when only one configured station uses that address.
        return TryGetFirstState(frame.StationAddress, out var state) ? state : null;
    }

    private bool CanUseProtocolFallback(byte stationAddress) =>
        _bindings.Where(binding => binding.Config.ProtocolAddress == stationAddress).All(binding =>
            !binding.Config.TryResolveEndpoint(out _));

    private bool TryGetFirstState(byte stationAddress, out StationRuntimeState state)
    {
        if (_statesByProtocol.TryGetValue(stationAddress, out var states) && states.Count > 0)
        {
            state = states[0];
            return true;
        }

        state = null!;
        return false;
    }

    private bool TryGetState(RfidStationConfig station, out StationRuntimeState state)
    {
        if (station.TryResolveEndpoint(out var endpoint) &&
            _statesByEndpoint.TryGetValue(new RfidStationEndpointKey(endpoint, station.ProtocolAddress), out state!))
        {
            return true;
        }

        var binding = _bindings.FirstOrDefault(item => ReferenceEquals(item.Config, station));
        if (binding is not null)
        {
            state = binding.State;
            return true;
        }

        if (CanUseProtocolFallback(station.ProtocolAddress) && TryGetFirstState(station.ProtocolAddress, out var compatibilityState))
        {
            state = compatibilityState;
            return true;
        }

        state = null!;
        return false;
    }

    private static RfidStationEndpointKey CreateStationKey(RfidStationConfig station, int index)
    {
        if (station.TryResolveEndpoint(out var endpoint))
        {
            return new RfidStationEndpointKey(endpoint, station.ProtocolAddress);
        }

        return new RfidStationEndpointKey(new System.Net.IPEndPoint(System.Net.IPAddress.None, index + 1), station.ProtocolAddress);
    }

    private static bool IsUsableEndpoint(System.Net.IPEndPoint? endpoint) =>
        endpoint is not null &&
        endpoint.Port is >= 1 and <= 65535 &&
        !System.Net.IPAddress.None.Equals(endpoint.Address) &&
        !System.Net.IPAddress.Any.Equals(endpoint.Address) &&
        !System.Net.IPAddress.IPv6Any.Equals(endpoint.Address);

    public void Evaluate(DateTimeOffset now)
    {
        lock (_syncRoot)
        {
            EvaluateCore(now);
        }
    }

    private void EvaluateCore(DateTimeOffset now)
    {
        foreach (var state in _statesByEndpoint.Values)
        {
            if (!state.LastResponseAt.HasValue || now - state.LastResponseAt.Value >= _policy.OfflineTimeout)
            {
                state.CommunicationState = StationCommunicationState.Offline;
                UpdateVisualState(state);
                continue;
            }

            state.CommunicationState = StationCommunicationState.Online;
            if (state.LifecycleState == PassageLifecycleState.Finalizing)
            {
                TryPersistPendingPassage(state);
                UpdateVisualState(state);
                continue;
            }
            if (state.LifecycleState != PassageLifecycleState.Recognizing || state.RecognitionSession is null)
            {
                UpdateVisualState(state);
                continue;
            }

            state.RecognitionSession.Evaluate(now);
            UpdateFromRecognition(state);
            if (state.RecognitionSession.State == StationRecognitionState.UncouplingAlarm)
            {
                FreezePassage(state, PassageOutcome.UncouplingAlarm, now);
            }
            UpdateVisualState(state);
        }
    }

    public RfidPollCommand GetCommand(byte stationAddress)
    {
        lock (_syncRoot)
        {
            return TryGetFirstState(stationAddress, out var state) && state.PendingClear
                ? RfidPollCommand.Clear
                : RfidPollCommand.Read;
        }
    }

    public RfidPollCommand GetCommand(RfidStationConfig station)
    {
        if (station is null) throw new ArgumentNullException(nameof(station));
        lock (_syncRoot)
        {
            return TryGetState(station, out var state) && state.PendingClear
                ? RfidPollCommand.Clear
                : RfidPollCommand.Read;
        }
    }

    public void RestorePendingClear(IEnumerable<PassageRecord> pendingRecords)
    {
        if (pendingRecords is null) throw new ArgumentNullException(nameof(pendingRecords));
        lock (_syncRoot)
        {
            foreach (var record in pendingRecords.OrderBy(item => item.CompletedAt))
            {
                if (!TryGetFirstState(record.StationAddress, out var state))
                {
                    continue;
                }

                state.RecognitionSession = null;
                state.LastPassageRecord = record;
                state.CurrentHeadRfid = record.HeadRfid;
                state.ObservedVehicleSequence = record.ObservedRfids.ToArray();
                state.SeenRfids = record.ObservedRfids.ToArray();
                state.DetectedVehicleCount = record.DetectedVehicleCount;
                state.ExpectedVehicleCount = record.ExpectedVehicleCount;
                state.HeadTagRfids = record.ObservedRfids.Where(RfidRecognitionRules.IsHeadRfid).ToArray();
                state.HeadTagCount = state.HeadTagRfids.Count;
                state.WarningMessages = record.WarningMessages.ToArray();
                state.AlarmMessage = record.AlarmMessage;
                state.SessionStartedAt = record.StartedAt;
                state.LastNewVehicleAt = record.CompletedAt;
                state.PendingClear = true;
                state.ClearAttempts = 0;
                state.ConsecutiveEmptyReads = 0;
                state.PersistenceWarning = false;
                state.PersistenceErrorMessage = null;
                state.PersistenceAttemptCount = 0;
                state.ClearPersistenceAttemptCount = 0;
                state.LifecycleState = PassageLifecycleState.Clearing;
                UpdateVisualState(state);
            }
        }
    }

    public void MarkCommandSent(byte stationAddress, RfidPollCommand command, DateTimeOffset sentAt)
    {
        var marked = false;
        lock (_syncRoot)
        {
            marked = TryGetFirstState(stationAddress, out var state) && MarkCommandSentCore(state, command, sentAt);
        }

        if (marked)
        {
            CommandSent?.Invoke(stationAddress, command, sentAt);
        }
    }

    public void MarkCommandSent(RfidStationConfig station, RfidPollCommand command, DateTimeOffset sentAt)
    {
        if (station is null) throw new ArgumentNullException(nameof(station));
        var marked = false;
        lock (_syncRoot)
        {
            marked = TryGetState(station, out var state) && MarkCommandSentCore(state, command, sentAt);
        }

        if (marked)
        {
            CommandSent?.Invoke(station.ProtocolAddress, command, sentAt);
            StationCommandSent?.Invoke(station, command, sentAt);
        }
    }

    private bool MarkCommandSentCore(StationRuntimeState state, RfidPollCommand command, DateTimeOffset sentAt)
    {
        if (command != RfidPollCommand.Clear || !state.PendingClear)
        {
            return false;
        }

        state.PendingClear = false;
        state.ClearAttempts++;
        state.ConsecutiveEmptyReads = 0;
        state.LifecycleState = PassageLifecycleState.Clearing;
        state.LastResponseAt ??= sentAt;
        UpdateVisualState(state);
        return true;
    }

    private void ProcessWaitForEmptyFrame(StationRuntimeState state, RfidStationFrame frame)
    {
        if (IsEmptySnapshot(frame, state.EmptyRfidValue))
        {
            state.ConsecutiveEmptyReads++;
            if (state.ConsecutiveEmptyReads >= _policy.EmptyConfirmationReads)
            {
                TryMarkCleared(state, frame.ReceivedAt);
            }
            return;
        }

        state.ConsecutiveEmptyReads = 0;
        if (state.ClearAttempts < _policy.MaxClearAttempts)
        {
            state.PendingClear = true;
            return;
        }

        AddWarning(state, $"清除重试已达{_policy.MaxClearAttempts}次，继续等待标签离开");
    }

    private void FreezePassage(StationRuntimeState state, PassageOutcome outcome, DateTimeOffset completedAt)
    {
        if (state.RecognitionSession is null)
        {
            return;
        }

        if (state.LastPassageRecord is null)
        {
            UpdateFromRecognition(state);
            state.AlarmMessage = outcome == PassageOutcome.UncouplingAlarm ? "脱节报警" : null;
            state.LastPassageRecord = new PassageRecord(
                Guid.NewGuid(),
                state.StationId,
                state.StationAddress,
                state.CurrentHeadRfid,
                state.RecognitionSession.ObservedVehicleSequence,
                state.RecognitionSession.ExpectedVehicleCount,
                outcome,
                state.SessionStartedAt ?? completedAt,
                completedAt,
                state.WarningMessages,
                state.AlarmMessage,
                state.RecognitionSession.RfidObservations);
        }

        TryPersistPendingPassage(state);
    }

    private void TryPersistPendingPassage(StationRuntimeState state)
    {
        var record = state.LastPassageRecord;
        if (record is null || state.PersistenceAttemptCount >= _policy.MaxPersistenceAttempts)
        {
            return;
        }

        state.PersistenceAttemptCount++;
        try
        {
            // Freeze and save before exposing PendingClear to the round-robin poller.
            _recordStore.Save(record);
            state.PersistenceWarning = false;
            state.PersistenceErrorMessage = null;
            state.PersistenceAttemptCount = 0;
            state.ClearPersistenceAttemptCount = 0;
            state.LifecycleState = record.Outcome == PassageOutcome.UncouplingAlarm
                ? PassageLifecycleState.Alarm
                : PassageLifecycleState.Completed;
            state.PendingClear = true;
            state.ClearAttempts = 0;
            state.ConsecutiveEmptyReads = 0;
        }
        catch (Exception exception)
        {
            state.PersistenceWarning = true;
            state.PersistenceErrorMessage = $"记录保存失败：{exception.Message}";
            state.LifecycleState = PassageLifecycleState.Finalizing;
            AddWarning(state, state.PersistenceErrorMessage);
            state.PendingClear = false;
        }
    }

    private void TryMarkCleared(StationRuntimeState state, DateTimeOffset clearedAt)
    {
        if (state.LastPassageRecord is null)
        {
            ResetToIdle(state);
            return;
        }

        if (state.ClearPersistenceAttemptCount >= _policy.MaxPersistenceAttempts)
        {
            return;
        }

        state.ClearPersistenceAttemptCount++;
        try
        {
            _recordStore.MarkCleared(state.LastPassageRecord.PassageId, clearedAt);
            ResetToIdle(state);
        }
        catch (Exception exception)
        {
            state.PersistenceWarning = true;
            state.PersistenceErrorMessage = $"清除状态保存失败：{exception.Message}";
            state.LifecycleState = PassageLifecycleState.WaitForEmpty;
            AddWarning(state, state.PersistenceErrorMessage);
        }
    }

    private void UpdateFromRecognition(StationRuntimeState state)
    {
        var session = state.RecognitionSession!;
        state.DetectedVehicleCount = session.DetectedVehicleCount;
        state.ExpectedVehicleCount = session.ExpectedVehicleCount;
        state.ObservedVehicleSequence = session.ObservedVehicleSequence.ToArray();
        state.SeenRfids = session.SeenRfids.ToArray();
        state.LastNewVehicleAt = session.LastNewVehicleAt;
        state.HeadTagCount = session.HeadTagCount;
        state.HeadTagRfids = session.HeadTagRfids.ToArray();
        state.CurrentHeadRfid = session.HeadTagRfids.Count > 0
            ? session.HeadTagRfids[0]
            : null;

        var warnings = new List<string>();
        if (session.ProtocolDataWarning)
        {
            warnings.Add($"协议数据告警：上报 {session.ReportedCardCount}，实际有效 {session.ActualNonZeroSlotCount}");
        }
        if (session.HeadTagWarnings.Contains(RfidHeadWarning.MissingHeadTag))
        {
            warnings.Add("未检测到车头标签");
        }
        if (session.HeadTagWarnings.Contains(RfidHeadWarning.FirstTagIsNotHead))
        {
            warnings.Add("首个识别标签不是有效车头标签");
        }
        if (session.HeadTagWarnings.Contains(RfidHeadWarning.MultipleHeadTags))
        {
            var tags = string.Join(", ", session.HeadTagRfids.Select(rfid => rfid.ToString("X4")));
            warnings.Add($"检测到多个车头标签（{tags}）");
        }

        state.WarningMessages = warnings.AsReadOnly();
    }

    private void ResetToIdle(StationRuntimeState state)
    {
        var keepAlarmVisual = state.VisualState == RfidStationVisualState.Alarm ||
                              state.LifecycleState == PassageLifecycleState.Alarm ||
                              state.LastPassageRecord?.Outcome == PassageOutcome.UncouplingAlarm;

        state.LifecycleState = PassageLifecycleState.Idle;
        state.PendingClear = false;
        state.ConsecutiveEmptyReads = 0;
        state.RecognitionSession = null;
        state.SessionStartedAt = null;
        state.CurrentHeadRfid = null;
        state.ObservedVehicleSequence = Array.Empty<ushort>();
        state.SeenRfids = Array.Empty<ushort>();
        state.DetectedVehicleCount = 0;
        state.HeadTagCount = 0;
        state.HeadTagRfids = Array.Empty<ushort>();
        state.WarningMessages = Array.Empty<string>();
        state.AlarmMessage = null;
        state.LastPassageRecord = null;
        state.PersistenceWarning = false;
        state.PersistenceErrorMessage = null;
        state.PersistenceAttemptCount = 0;
        state.ClearPersistenceAttemptCount = 0;
        if (keepAlarmVisual)
        {
            state.VisualState = RfidStationVisualState.Alarm;
        }
        else
        {
            UpdateVisualState(state);
        }
    }

    private void AddWarning(StationRuntimeState state, string warning)
    {
        if (state.WarningMessages.Contains(warning))
        {
            return;
        }

        state.WarningMessages = state.WarningMessages.Concat(new[] { warning }).ToArray();
    }

    private static bool HasNonEmptySlot(RfidStationFrame frame, ushort emptyRfidValue) =>
        frame.ValidRfids is not null && frame.ValidRfids.Any(value => value != emptyRfidValue)
        || frame.RawRfidSlots is not null && frame.RawRfidSlots.Any(value => value != emptyRfidValue);

    private static bool IsEmptySnapshot(RfidStationFrame frame, ushort emptyRfidValue) =>
        frame.RawRfidSlots is not null && frame.RawRfidSlots.Count == 14 && frame.RawRfidSlots.All(value => value == emptyRfidValue);

    private static void UpdateVisualState(StationRuntimeState state)
    {
        // Keep an uncoupling alarm visible while the clear handshake and empty-slot
        // confirmation are still in progress. The lifecycle continues normally;
        // this only preserves the alarm indication for the visual layer.
        if (state.LifecycleState == PassageLifecycleState.Alarm ||
            state.LastPassageRecord?.Outcome == PassageOutcome.UncouplingAlarm)
        {
            state.VisualState = RfidStationVisualState.Alarm;
            return;
        }

        if (state.LifecycleState == PassageLifecycleState.Idle &&
            state.VisualState == RfidStationVisualState.Alarm)
        {
            return;
        }

        if (state.CommunicationState == StationCommunicationState.Offline)
        {
            state.VisualState = RfidStationVisualState.Offline;
            return;
        }

        state.VisualState = state.LifecycleState switch
        {
            PassageLifecycleState.Alarm => RfidStationVisualState.Alarm,
            PassageLifecycleState.Finalizing => RfidStationVisualState.Warning,
            PassageLifecycleState.Completed when state.WarningMessages.Count > 0 => RfidStationVisualState.Warning,
            PassageLifecycleState.Completed => RfidStationVisualState.Idle,
            PassageLifecycleState.Clearing => RfidStationVisualState.Clearing,
            PassageLifecycleState.WaitForEmpty when state.WarningMessages.Count > 0 => RfidStationVisualState.Warning,
            PassageLifecycleState.WaitForEmpty => RfidStationVisualState.Clearing,
            PassageLifecycleState.Recognizing when state.WarningMessages.Count > 0 => RfidStationVisualState.Warning,
            PassageLifecycleState.Recognizing => RfidStationVisualState.Recognizing,
            _ when state.WarningMessages.Count > 0 => RfidStationVisualState.Warning,
            _ => RfidStationVisualState.Idle
        };
    }

    private static RfidSettings CopySettings(RfidSettings settings) => new()
    {
        PollIntervalMs = settings.PollIntervalMs,
        ExpectedVehicleCount = settings.ExpectedVehicleCount,
        InterVehicleTimeoutSeconds = settings.InterVehicleTimeoutSeconds,
        EmptyRfidValue = settings.EmptyRfidValue
    };

    private static IEnumerable<RfidStationConfig> CreateCompatibilityStations(IEnumerable<byte> stationAddresses)
    {
        if (stationAddresses is null) throw new ArgumentNullException(nameof(stationAddresses));
        return stationAddresses.Select(address => new RfidStationConfig
        {
            StationId = $"RFID-{address:X2}",
            Name = $"RFID-{address:X2}",
            ProtocolAddress = address,
            Enabled = true
        });
    }

    private sealed class RfidStationBinding
    {
        public RfidStationBinding(RfidStationConfig config, RfidStationEndpointKey key, StationRuntimeState state)
        {
            Config = config;
            Key = key;
            State = state;
        }

        public RfidStationConfig Config { get; }

        public RfidStationEndpointKey Key { get; }

        public StationRuntimeState State { get; }
    }
}
