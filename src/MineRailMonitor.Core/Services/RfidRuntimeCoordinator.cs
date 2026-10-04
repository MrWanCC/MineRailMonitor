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
    private readonly IPassageEventLogger? _passageEventLogger;
    private readonly RfidRuntimePolicy _policy;
    private readonly ProjectConfig _vehicleMatchProject;
    private readonly VehicleMatchResolver _vehicleMatchResolver;
    private RfidSettings _defaults;

    public RfidRuntimeCoordinator(
        IEnumerable<byte> stationAddresses,
        RfidSettings settings,
        IPassageRecordStore recordStore,
        RfidRuntimePolicy? policy = null,
        IEnumerable<VehicleConfig>? vehicleConfigurations = null,
        IPassageEventLogger? passageEventLogger = null)
        : this(CreateCompatibilityStations(stationAddresses), settings, recordStore, policy, vehicleConfigurations, passageEventLogger)
    {
    }

    public RfidRuntimeCoordinator(
        IEnumerable<RfidStationConfig> stations,
        RfidSettings settings,
        IPassageRecordStore recordStore,
        RfidRuntimePolicy? policy = null,
        IEnumerable<VehicleConfig>? vehicleConfigurations = null,
        IPassageEventLogger? passageEventLogger = null)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));
        _defaults = CopySettings(settings ?? throw new ArgumentNullException(nameof(settings)));
        var errors = _defaults.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(settings));
        }

        _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        _passageEventLogger = passageEventLogger;
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
        var invalidVehicleCountStations = enabledStations
            .Where(station => station.ExpectedVehicleCount is < 1 or > 14)
            .Select(station => station.StationId)
            .ToArray();
        if (invalidVehicleCountStations.Length > 0)
        {
            throw new ArgumentException(
                $"Expected vehicle count must be between 1 and 14 for: {string.Join(", ", invalidVehicleCountStations)}.",
                nameof(stations));
        }

        _bindings = enabledStations
            .Select((station, index) => new RfidStationBinding(
                station,
                CreateStationKey(station, index),
                new StationRuntimeState(
                    station.StationId,
                    station.Name,
                    station.ProtocolAddress,
                    station.ExpectedVehicleCount ?? _defaults.ExpectedVehicleCount,
                    _defaults.EmptyRfidValue)))
            .ToArray();
        _vehicleMatchProject = new ProjectConfig
        {
            RfidSettings = CopySettings(_defaults),
            RfidStations = enabledStations,
            Vehicles = (vehicleConfigurations ?? Array.Empty<VehicleConfig>()).ToArray()
        };
        _vehicleMatchResolver = new VehicleMatchResolver(_vehicleMatchProject);
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

    public event Action<AlarmForwardRequest>? AlarmForwardRequested;

    public event Action<VehicleMatchResult>? VehicleMatchResolved;

    public event Action<PassageRecord>? AlarmRecovered;

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

            _vehicleMatchProject.RfidSettings = CopySettings(_defaults);
        }
    }

    public void UpdateVehicleConfigurations(IEnumerable<VehicleConfig> vehicleConfigurations)
    {
        if (vehicleConfigurations is null) throw new ArgumentNullException(nameof(vehicleConfigurations));

        var configurations = vehicleConfigurations.ToArray();
        lock (_syncRoot)
        {
            _vehicleMatchProject.Vehicles = configurations;
        }
    }

    public int RetryPendingPassages()
    {
        var alarmForwardRequests = new List<AlarmForwardRequest>();
        var recoveredAlarms = new List<PassageRecord>();
        var recoveredCount = 0;
        var attemptedAt = DateTimeOffset.UtcNow;
        lock (_syncRoot)
        {
            foreach (var state in _statesByEndpoint.Values)
            {
                foreach (var fault in state.PendingPersistenceFaults)
                {
                    try
                    {
                        var before = fault.ClearedAt.HasValue
                            ? _recordStore.GetDetails(fault.Record.PassageId)
                            : null;
                        _recordStore.Save(fault.Record);
                        PassageRecord? after = null;
                        if (fault.ClearedAt.HasValue)
                        {
                            _recordStore.MarkCleared(fault.Record.PassageId, fault.ClearedAt.Value);
                            after = _recordStore.GetDetails(fault.Record.PassageId);
                        }

                        if (before?.AlarmStatus != AlarmStatus.Recovered && after?.AlarmStatus == AlarmStatus.Recovered)
                        {
                            recoveredAlarms.Add(after);
                        }

                        if (fault.Record.RequiresAlarmAcknowledgement)
                        {
                            state.AddUnacknowledgedAlarm(fault.Record.PassageId);
                            alarmForwardRequests.Add(new AlarmForwardRequest(
                                fault.Record.PassageId,
                                fault.Record.StationId,
                                fault.Record.StationAddress,
                                fault.LastVehiclePacket,
                                fault.Record.CompletedAt));
                        }

                        WritePassageEvent(CreatePersistenceEvent(
                            fault.Record,
                            PassageEventTypes.PersistenceRecovered,
                            attemptedAt,
                            fault.FailureCount,
                            $"Passage记录已恢复保存；此前失败{fault.FailureCount}次"));
                        state.RemovePersistenceFault(fault);
                        recoveredCount++;
                    }
                    catch (Exception exception)
                    {
                        fault.RecordFailure(exception, attemptedAt);
                        WritePassageEvent(CreatePersistenceEvent(
                            fault.Record,
                            PassageEventTypes.PersistenceFailed,
                            attemptedAt,
                            fault.FailureCount,
                            fault.LastException));
                    }

                    RefreshPersistenceFaultDisplay(state);
                }

                UpdateVisualState(state);
            }
        }

        foreach (var request in alarmForwardRequests)
        {
            PublishAlarmForwardRequest(request);
        }
        foreach (var alarm in recoveredAlarms)
        {
            PublishAlarmRecovered(alarm);
        }

        return recoveredCount;
    }

    public StationRecognitionSession? ProcessFrame(RfidStationFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        AlarmForwardRequest? alarmForwardRequest;
        VehicleMatchResult? vehicleMatchResult;
        StationRecognitionSession? result;
        lock (_syncRoot)
        {
            result = ProcessFrameCore(frame, out alarmForwardRequest, out vehicleMatchResult);
        }

        PublishAlarmForwardRequest(alarmForwardRequest);
        if (vehicleMatchResult is not null)
        {
            VehicleMatchResolved?.Invoke(vehicleMatchResult);
        }
        return result;
    }

    private StationRecognitionSession? ProcessFrameCore(
        RfidStationFrame frame,
        out AlarmForwardRequest? alarmForwardRequest,
        out VehicleMatchResult? vehicleMatchResult)
    {
        alarmForwardRequest = null;
        vehicleMatchResult = null;
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
            alarmForwardRequest = TryPersistPendingPassage(state, frame.ReceivedAt);
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

        var sessionCreated = false;
        if (state.RecognitionSession is null)
        {
            if (!HasNonEmptySlot(frame, _defaults.EmptyRfidValue))
            {
                UpdateVisualState(state);
                return null;
            }

            var resolvedVehicle = _vehicleMatchResolver.Resolve(frame, state);
            vehicleMatchResult = resolvedVehicle;
            state.RecognitionSession = new StationRecognitionSession(
                frame.StationAddress,
                vehicleMatchResult.ExpectedCount,
                TimeSpan.FromSeconds(_defaults.InterVehicleTimeoutSeconds),
                _defaults.EmptyRfidValue,
                vehicleMatchResult);
            sessionCreated = true;
            state.SessionStartedAt = frame.ReceivedAt;
            state.ExpectedVehicleCount = state.RecognitionSession.ExpectedVehicleCount;
            state.LastPassageRecord = null;
            state.AlarmMessage = null;
            state.LastNewVehicleRawPacket = null;
            state.LastNewVehicleRawPacketAt = null;
            state.AlarmForwardTriggeredPassageId = null;
            state.EmptyRfidValue = _defaults.EmptyRfidValue;
            state.ClearAttempts = 0;
            state.ConsecutiveEmptyReads = 0;
            WritePassageEvent(CreatePassageEvent(
                PassageEventTypes.Start,
                state,
                state.RecognitionSession,
                frame.ReceivedAt,
                frame.HeadRfid));
        }

        var session = state.RecognitionSession;
        if (!sessionCreated &&
            session.VehicleMatchResult is null &&
            frame.RawRfidSlots is { Count: > 0 } &&
            frame.RawRfidSlots[0] != state.EmptyRfidValue)
        {
            var lateMatch = _vehicleMatchResolver.Resolve(frame, state);
            if (session.TrySupplementVehicleIdentity(lateMatch))
            {
                WritePassageEvent(CreatePassageEvent(
                    PassageEventTypes.VehicleMatchLateResolved,
                    state,
                    session,
                    frame.ReceivedAt));
            }
        }

        var detectedVehicleCountBeforeApply = session.DetectedVehicleCount;
        session.Apply(frame);
        if (session.DetectedVehicleCount > detectedVehicleCountBeforeApply)
        {
            state.LastNewVehicleRawPacket = frame.RawData is { Length: > 0 }
                ? (byte[])frame.RawData.Clone()
                : Array.Empty<byte>();
            state.LastNewVehicleRawPacketAt = frame.ReceivedAt;
            WritePassageEvent(CreatePassageEvent(
                PassageEventTypes.Progress,
                state,
                session,
                frame.ReceivedAt));
        }
        UpdateFromRecognition(state);
        state.LifecycleState = PassageLifecycleState.Recognizing;
        if (session.State == StationRecognitionState.Completed)
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
        if (_statesByProtocol.TryGetValue(stationAddress, out var states) && states.Count == 1)
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

    private bool TryGetStateForRecord(PassageRecord record, out StationRuntimeState state)
    {
        var matches = _bindings
            .Where(binding => string.Equals(binding.State.StationId, record.StationId, StringComparison.OrdinalIgnoreCase))
            .Select(binding => binding.State)
            .ToArray();
        if (matches.Length == 1)
        {
            state = matches[0];
            return true;
        }

        if (string.Equals(record.StationId, PassageRecord.LegacyStationId, StringComparison.OrdinalIgnoreCase) &&
            _statesByProtocol.TryGetValue(record.StationAddress, out var protocolStates) &&
            protocolStates.Count == 1)
        {
            state = protocolStates[0];
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
        var alarmForwardRequests = new List<AlarmForwardRequest>();
        lock (_syncRoot)
        {
            EvaluateCore(now, alarmForwardRequests);
        }

        foreach (var request in alarmForwardRequests)
        {
            PublishAlarmForwardRequest(request);
        }
    }

    private void EvaluateCore(DateTimeOffset now, ICollection<AlarmForwardRequest> alarmForwardRequests)
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
                var retryRequest = TryPersistPendingPassage(state, now);
                if (retryRequest is not null)
                {
                    alarmForwardRequests.Add(retryRequest);
                }
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
                var alarmRequest = FreezePassage(state, PassageOutcome.UncouplingAlarm, now);
                if (alarmRequest is not null)
                {
                    alarmForwardRequests.Add(alarmRequest);
                }
            }
            else if (state.RecognitionSession.State == StationRecognitionState.Unverified)
            {
                FreezePassage(state, PassageOutcome.Unverified, now);
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
                if (!TryGetStateForRecord(record, out var state))
                {
                    continue;
                }

                state.RecognitionSession = null;
                state.LastPassageRecord = record;
                if (record.RequiresAlarmAcknowledgement && !record.AlarmAcknowledgedAt.HasValue)
                {
                    state.AddUnacknowledgedAlarm(record.PassageId);
                }
                state.CurrentHeadRfid = record.VehicleHeadRfid ?? record.HeadRfid;
                state.ObservedVehicleSequence = record.ObservedRfids.ToArray();
                state.SeenRfids = record.ObservedRfids.ToArray();
                state.DetectedVehicleCount = record.DetectedVehicleCount;
                state.ExpectedVehicleCount = record.ExpectedVehicleCount;
                state.HeadTagRfids = state.CurrentHeadRfid.HasValue
                    ? new[] { state.CurrentHeadRfid.Value }
                    : Array.Empty<ushort>();
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

    public void RestoreUnacknowledgedAlarms(IEnumerable<PassageRecord> records)
    {
        if (records is null) throw new ArgumentNullException(nameof(records));
        lock (_syncRoot)
        {
            foreach (var record in records
                .Where(item => item.RequiresAlarmAcknowledgement && !item.AlarmAcknowledgedAt.HasValue)
                .OrderBy(item => item.CompletedAt))
            {
                if (!TryGetStateForRecord(record, out var state))
                {
                    continue;
                }

                state.AddUnacknowledgedAlarm(record.PassageId);
                if (state.LastPassageRecord is null || state.LastPassageRecord.CompletedAt <= record.CompletedAt)
                {
                    state.LastPassageRecord = record;
                    state.AlarmMessage = record.AlarmMessage;
                }

                UpdateVisualState(state);
            }
        }
    }

    public bool HasUnacknowledgedAlarm(Guid passageId)
    {
        lock (_syncRoot)
        {
            return _statesByEndpoint.Values.Any(state => state.ContainsUnacknowledgedAlarm(passageId));
        }
    }

    public void AcknowledgeAlarm(Guid passageId, DateTimeOffset acknowledgedAt)
    {
        PassageRecord? recoveredRecord = null;
        lock (_syncRoot)
        {
            var state = _statesByEndpoint.Values.FirstOrDefault(item => item.ContainsUnacknowledgedAlarm(passageId));
            if (state is null)
            {
                throw new InvalidOperationException($"未找到待确认的脱节报警：{passageId}");
            }

            var before = _recordStore.GetDetails(passageId);
            _recordStore.MarkAlarmAcknowledged(passageId, acknowledgedAt);
            var after = _recordStore.GetDetails(passageId);
            if (before?.AlarmStatus != AlarmStatus.Recovered && after?.AlarmStatus == AlarmStatus.Recovered)
            {
                recoveredRecord = after;
            }
            if (after is not null && state.LastPassageRecord?.PassageId == passageId)
            {
                state.LastPassageRecord = after;
            }
            state.RemoveUnacknowledgedAlarm(passageId);
            UpdateVisualState(state);
        }

        if (recoveredRecord is not null)
        {
            PublishAlarmRecovered(recoveredRecord);
        }
    }

    public bool TryAcknowledgeAlarm(byte stationAddress, DateTimeOffset acknowledgedAt)
    {
        PassageRecord? recoveredRecord = null;
        lock (_syncRoot)
        {
            var unacknowledgedRecords = _recordStore.GetUnacknowledgedAlarms()
                .Where(record => record.StationAddress == stationAddress && record.RequiresAlarmAcknowledgement)
                .ToArray();
            var matches = _statesByEndpoint.Values
                .Where(state => state.StationAddress == stationAddress)
                .SelectMany(state => unacknowledgedRecords
                    .Where(record => string.Equals(
                                        record.StationId,
                                        state.StationId,
                                        StringComparison.OrdinalIgnoreCase) &&
                                    state.ContainsUnacknowledgedAlarm(record.PassageId))
                    .Select(record => new { State = state, Record = record }))
                .ToArray();
            if (matches.Length != 1)
            {
                return false;
            }

            var state = matches[0].State;
            var passageId = matches[0].Record.PassageId;
            var before = _recordStore.GetDetails(passageId);
            _recordStore.MarkAlarmAcknowledged(passageId, acknowledgedAt);
            var after = _recordStore.GetDetails(passageId);
            if (before?.AlarmStatus != AlarmStatus.Recovered && after?.AlarmStatus == AlarmStatus.Recovered)
            {
                recoveredRecord = after;
            }
            if (after is not null && state.LastPassageRecord?.PassageId == passageId)
            {
                state.LastPassageRecord = after;
            }
            state.RemoveUnacknowledgedAlarm(passageId);
            UpdateVisualState(state);
        }

        if (recoveredRecord is not null)
        {
            PublishAlarmRecovered(recoveredRecord);
        }
        return true;
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

    private AlarmForwardRequest? FreezePassage(StationRuntimeState state, PassageOutcome outcome, DateTimeOffset completedAt)
    {
        if (state.RecognitionSession is null)
        {
            return null;
        }

        if (state.LastPassageRecord is null)
        {
            UpdateFromRecognition(state);
            state.AlarmMessage = outcome == PassageOutcome.UncouplingAlarm ? "脱节报警" : null;
            state.LastPassageRecord = new PassageRecord(
                state.RecognitionSession.PassageId,
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
                state.RecognitionSession.RfidObservations,
                vehicleName: state.RecognitionSession.VehicleName,
                vehicleHeadRfid: state.RecognitionSession.VehicleHeadRfid,
                vehicleNo: state.RecognitionSession.VehicleNo);

            var session = state.RecognitionSession;
            if (outcome == PassageOutcome.Completed)
            {
                var startedAt = session.FirstVehicleAt ?? state.SessionStartedAt ?? completedAt;
                WritePassageEvent(CreatePassageEvent(
                    PassageEventTypes.Complete,
                    state,
                    session,
                    completedAt,
                    durationMilliseconds: Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds)));
            }
            else if (outcome == PassageOutcome.UncouplingAlarm)
            {
                WritePassageEvent(CreatePassageEvent(
                    PassageEventTypes.Alarm,
                    state,
                    session,
                    completedAt,
                    missingCount: Math.Max(0, session.ExpectedVehicleCount!.Value - session.DetectedVehicleCount),
                    timeoutSeconds: (int)Math.Ceiling(session.InterVehicleTimeout.TotalSeconds),
                    reason: state.AlarmMessage));
            }
            else if (outcome == PassageOutcome.Unverified)
            {
                WritePassageEvent(CreatePassageEvent(
                    PassageEventTypes.Unverified,
                    state,
                    session,
                    completedAt,
                    reason: "车辆未匹配，且未配置完整性判断回退节数"));
            }
        }

        return TryPersistPendingPassage(state, completedAt);
    }

    private AlarmForwardRequest? TryPersistPendingPassage(StationRuntimeState state, DateTimeOffset attemptedAt)
    {
        var record = state.LastPassageRecord;
        if (record is null || state.PersistenceAttemptCount >= _policy.MaxPersistenceAttempts)
        {
            return null;
        }

        state.PersistenceAttemptCount++;
        try
        {
            // Freeze and save before exposing PendingClear to the round-robin poller.
            _recordStore.Save(record);
            RefreshPersistenceFaultDisplay(state);
            state.PersistenceAttemptCount = 0;
            state.ClearPersistenceAttemptCount = 0;
            if (record.RequiresAlarmAcknowledgement && !record.AlarmAcknowledgedAt.HasValue)
            {
                state.AddUnacknowledgedAlarm(record.PassageId);
            }
            state.LifecycleState = record.Outcome == PassageOutcome.UncouplingAlarm
                ? PassageLifecycleState.Alarm
                : PassageLifecycleState.Completed;
            state.PendingClear = true;
            state.ClearAttempts = 0;
            state.ConsecutiveEmptyReads = 0;

            if (record.Outcome == PassageOutcome.UncouplingAlarm &&
                state.AlarmForwardTriggeredPassageId != record.PassageId)
            {
                state.AlarmForwardTriggeredPassageId = record.PassageId;
                return new AlarmForwardRequest(
                    record.PassageId,
                    state.StationId,
                    state.StationAddress,
                    state.LastNewVehicleRawPacket ?? Array.Empty<byte>(),
                    record.CompletedAt);
            }
        }
        catch (Exception exception)
        {
            state.PersistenceWarning = true;
            state.PersistenceErrorMessage = $"记录保存失败：{exception.Message}";
            state.LifecycleState = PassageLifecycleState.Finalizing;
            AddWarning(state, state.PersistenceErrorMessage);
            state.PendingClear = false;

            if (state.PersistenceAttemptCount >= _policy.MaxPersistenceAttempts)
            {
                var fault = new PassagePersistenceFault(
                    record,
                    state.PersistenceAttemptCount,
                    exception,
                    attemptedAt,
                    state.LastNewVehicleRawPacket);
                state.AddPersistenceFault(fault);
                RefreshPersistenceFaultDisplay(state);
                WritePassageEvent(CreatePersistenceEvent(
                    record,
                    PassageEventTypes.PersistenceFailed,
                    attemptedAt,
                    fault.FailureCount,
                    fault.LastException));

                // Preserve the frozen record for recovery while allowing the existing clear handshake to run.
                state.LifecycleState = PassageLifecycleState.Clearing;
                state.PendingClear = true;
                state.ClearAttempts = 0;
                state.ClearPersistenceAttemptCount = 0;
                state.ConsecutiveEmptyReads = 0;
            }
        }

        return null;
    }

    private void TryMarkCleared(StationRuntimeState state, DateTimeOffset clearedAt)
    {
        if (state.LastPassageRecord is null)
        {
            ResetToIdle(state);
            return;
        }

        var pendingPersistenceFault = state.FindPersistenceFault(state.LastPassageRecord.PassageId);
        if (pendingPersistenceFault is not null)
        {
            pendingPersistenceFault.ClearedAt ??= clearedAt;
            WritePassageEvent(CreatePassageEvent(state.LastPassageRecord, clearedAt));
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
            var before = _recordStore.GetDetails(state.LastPassageRecord.PassageId);
            _recordStore.MarkCleared(state.LastPassageRecord.PassageId, clearedAt);
            var after = _recordStore.GetDetails(state.LastPassageRecord.PassageId);
            WritePassageEvent(CreatePassageEvent(state.LastPassageRecord, clearedAt));
            var recovered = before?.AlarmStatus != AlarmStatus.Recovered && after?.AlarmStatus == AlarmStatus.Recovered
                ? after
                : null;
            ResetToIdle(state);
            if (recovered is not null)
            {
                PublishAlarmRecovered(recovered);
            }
        }
        catch (Exception exception)
        {
            state.PersistenceWarning = true;
            state.PersistenceErrorMessage = $"清除状态保存失败：{exception.Message}";
            state.LifecycleState = PassageLifecycleState.WaitForEmpty;
            AddWarning(state, state.PersistenceErrorMessage);
        }
    }

    private void PublishAlarmRecovered(PassageRecord record)
    {
        var handlers = AlarmRecovered;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<PassageRecord> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(record);
            }
            catch
            {
                // Recovery logging is best-effort and must not interrupt passage handling.
            }
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
            warnings.Add("未检测到车头槽位(Position 0)");
        }
        if (session.HeadTagWarnings.Contains(RfidHeadWarning.FirstTagIsNotHead))
        {
            warnings.Add("首个识别RFID未出现在车头槽位(Position 0)");
        }
        if (session.HeadTagWarnings.Contains(RfidHeadWarning.MultipleHeadTags))
        {
            var tags = string.Join(", ", session.HeadTagRfids.Select(rfid => rfid.ToString("X4")));
            warnings.Add($"检测到多个车头槽位RFID（{tags}）");
        }

        state.WarningMessages = warnings.AsReadOnly();
    }

    private void ResetToIdle(StationRuntimeState state)
    {
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
        state.LastNewVehicleRawPacket = null;
        state.LastNewVehicleRawPacketAt = null;
        state.AlarmForwardTriggeredPassageId = null;
        state.PersistenceAttemptCount = 0;
        state.ClearPersistenceAttemptCount = 0;
        RefreshPersistenceFaultDisplay(state);
        UpdateVisualState(state);
    }

    private static void RefreshPersistenceFaultDisplay(StationRuntimeState state)
    {
        var warnings = state.WarningMessages
            .Where(message => !message.StartsWith("记录保存失败：", StringComparison.Ordinal) &&
                              !message.StartsWith("数据库保存失败；", StringComparison.Ordinal))
            .ToList();
        var latestFault = state.PendingPersistenceFaults
            .OrderBy(fault => fault.FailedAt)
            .LastOrDefault();
        if (latestFault is null)
        {
            if (state.PersistenceErrorMessage?.StartsWith("记录保存失败：", StringComparison.Ordinal) == true ||
                state.PersistenceErrorMessage?.StartsWith("数据库保存失败；", StringComparison.Ordinal) == true)
            {
                state.PersistenceWarning = false;
                state.PersistenceErrorMessage = null;
            }
        }
        else
        {
            state.PersistenceWarning = true;
            state.PersistenceErrorMessage = latestFault.ToDiagnosticMessage();
            warnings.Add(state.PersistenceErrorMessage);
        }

        state.WarningMessages = warnings.AsReadOnly();
    }

    private void AddWarning(StationRuntimeState state, string warning)
    {
        if (state.WarningMessages.Contains(warning))
        {
            return;
        }

        state.WarningMessages = state.WarningMessages.Concat(new[] { warning }).ToArray();
    }

    private static PassageEvent CreatePassageEvent(
        string eventType,
        StationRuntimeState state,
        StationRecognitionSession session,
        DateTimeOffset occurredAt,
        ushort? fallbackHeadRfid = null,
        long? durationMilliseconds = null,
        int? missingCount = null,
        int? timeoutSeconds = null,
        string? reason = null) => new()
        {
            PassageId = session.PassageId,
            EventType = eventType,
            OccurredAt = occurredAt,
            StationId = state.StationId,
            StationAddress = state.StationAddress,
            VehicleName = session.VehicleName,
            VehicleNo = session.VehicleNo,
            VehicleHeadRfid = session.VehicleHeadRfid ??
                (session.HeadRfid != 0 ? session.HeadRfid : session.FirstValidRfid ?? fallbackHeadRfid),
            ExpectedCount = session.ExpectedVehicleCount,
            DetectedCount = session.DetectedVehicleCount,
            DurationMilliseconds = durationMilliseconds,
            MissingCount = missingCount,
            TimeoutSeconds = timeoutSeconds,
            Reason = reason
        };

    private static PassageEvent CreatePassageEvent(PassageRecord record, DateTimeOffset occurredAt) => new()
    {
        PassageId = record.PassageId,
        EventType = PassageEventTypes.Clear,
        OccurredAt = occurredAt,
        StationId = record.StationId,
        StationAddress = record.StationAddress,
        VehicleName = record.VehicleName,
        VehicleNo = record.VehicleNo,
        VehicleHeadRfid = record.VehicleHeadRfid ?? record.HeadRfid,
        ExpectedCount = record.ExpectedVehicleCount,
        DetectedCount = record.DetectedVehicleCount
    };

    private static PassageEvent CreatePersistenceEvent(
        PassageRecord record,
        string eventType,
        DateTimeOffset occurredAt,
        int failureCount,
        string reason) => new()
        {
            PassageId = record.PassageId,
            EventType = eventType,
            OccurredAt = occurredAt,
            StationId = record.StationId,
            StationAddress = record.StationAddress,
            VehicleName = record.VehicleName,
            VehicleNo = record.VehicleNo,
            VehicleHeadRfid = record.VehicleHeadRfid ?? record.HeadRfid,
            ExpectedCount = record.ExpectedVehicleCount,
            DetectedCount = record.DetectedVehicleCount,
            FailureCount = failureCount,
            Reason = reason
        };

    private void WritePassageEvent(PassageEvent passageEvent)
    {
        try
        {
            _passageEventLogger?.Write(passageEvent);
        }
        catch
        {
            // Logging must not change passage recognition behavior.
        }
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
        if (state.HasUnacknowledgedAlarms
            || state.LifecycleState == PassageLifecycleState.Alarm
            || (state.LastPassageRecord?.Outcome == PassageOutcome.UncouplingAlarm
                && !state.LastPassageRecord.IsAlarmRecovered))
        {
            state.VisualState = RfidStationVisualState.Alarm;
            return;
        }

        if (state.CommunicationState == StationCommunicationState.Offline)
        {
            state.VisualState = RfidStationVisualState.Offline;
            return;
        }

        if (state.PersistenceState == PassagePersistenceState.PersistenceFault)
        {
            state.VisualState = RfidStationVisualState.Warning;
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

    private void PublishAlarmForwardRequest(AlarmForwardRequest? request)
    {
        if (request is not null)
        {
            AlarmForwardRequested?.Invoke(request);
        }
    }

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
