using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.ExternalAlarm;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

/// <summary>
/// Runs one independent communication context per configured yard.
/// </summary>
public sealed class YardCommunicationManager : IDisposable
{
    public const string LegacySharedListenerId = "LegacySharedListener";

    private readonly Dictionary<string, YardCommunicationContext> _contexts;
    private readonly IReadOnlyList<RfidStationConfig> _stations;
    private IReadOnlyList<VehicleConfig> _vehicleConfigurations;
    private readonly IPassageRecordStore _recordStore;
    private readonly IPassageEventLogger? _passageEventLogger;
    private readonly IRfidTimeProvider? _timeProvider;
    private IReadOnlyDictionary<string, IExternalDataInterface> _alarmForwardSenders;
    private IReadOnlyDictionary<string, ExternalAlarmStatusCoordinator> _externalAlarmStatusCoordinators;
    private RfidSettings _settings;
    private readonly IReadOnlyList<string> _diagnostics;
    private bool _isStarted;
    private bool _disposed;

    public YardCommunicationManager(
        IEnumerable<YardCommunicationConfig> configurations,
        IEnumerable<RfidStationConfig> stations,
        RfidSettings settings,
        IPassageRecordStore recordStore,
        IRfidTimeProvider? timeProvider = null,
        IReadOnlyDictionary<string, IExternalDataInterface>? alarmForwardSenders = null,
        IReadOnlyDictionary<string, ExternalAlarmStatusCoordinator>? externalAlarmStatusCoordinators = null,
        IEnumerable<VehicleConfig>? vehicleConfigurations = null,
        IPassageEventLogger? passageEventLogger = null)
    {
        if (configurations is null) throw new ArgumentNullException(nameof(configurations));
        if (stations is null) throw new ArgumentNullException(nameof(stations));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        if (recordStore is null) throw new ArgumentNullException(nameof(recordStore));

        _settings = settings;
        _recordStore = recordStore;
        _passageEventLogger = passageEventLogger;
        _timeProvider = timeProvider;
        _alarmForwardSenders = CopyAlarmForwardSenders(alarmForwardSenders);
        _externalAlarmStatusCoordinators = CopyExternalAlarmStatusCoordinators(externalAlarmStatusCoordinators);
        _stations = stations.Where(item => item is not null).ToArray();
        _vehicleConfigurations = (vehicleConfigurations ?? Array.Empty<VehicleConfig>()).ToArray();

        var diagnostics = new List<string>();
        AddDuplicateStationAddressDiagnostics(_stations, diagnostics);
        var configurationList = configurations.Where(item => item is not null).ToArray();
        ValidateCandidateEndpointConflicts(configurationList);
        var contextEntries = new Dictionary<string, YardCommunicationContext>(StringComparer.OrdinalIgnoreCase);
        var legacyConfiguration = configurationList.FirstOrDefault(item => item.IsLegacySharedListener);
        if (legacyConfiguration is not null)
        {
            diagnostics.Add("项目未配置 YardCommunications，当前使用 LegacySharedListener 兼容模式（非独立通信模式）。");
        }

        foreach (var configuration in configurationList)
        {
            var yardId = configuration.YardId?.Trim() ?? string.Empty;
            if (yardId.Length == 0)
            {
                diagnostics.Add("站场通信接口缺少 YardId，未创建通信上下文。");
                continue;
            }
            if (contextEntries.ContainsKey(yardId))
            {
                diagnostics.Add($"站场通信接口所属站场重复：{yardId}。");
                continue;
            }

            var context = CreateContext(configuration);
            contextEntries.Add(yardId, context);
        }

        if (legacyConfiguration is null)
        {
            foreach (var station in _stations)
            {
                var stationId = station.StationId?.Trim() ?? string.Empty;
                var yardId = station.YardId?.Trim() ?? string.Empty;
                if (yardId.Length == 0)
                {
                    diagnostics.Add($"RFID 基站 {stationId} 未分配通信站场，不进入任何 YardCommunicationContext。");
                }
                else if (!contextEntries.ContainsKey(yardId))
                {
                    diagnostics.Add($"RFID 基站 {stationId} 的通信归属站场 {yardId} 没有对应通信接口配置。");
                }
            }
        }

        _contexts = contextEntries;
        foreach (var context in _contexts.Values)
        {
            AttachContext(context);
        }

        _diagnostics = diagnostics;
    }

    public IReadOnlyDictionary<string, YardCommunicationContext> Contexts => _contexts;

    public IReadOnlyList<string> Diagnostics => _diagnostics;

    public event Action<YardCommunicationContext, RfidUdpDatagramEventArgs>? DatagramReceived;

    public event Action<YardCommunicationContext, RfidUdpDatagramSentEventArgs>? DatagramSent;

    public event Action<YardCommunicationContext, Exception>? ReceiveError;

    public event Action<YardCommunicationContext, byte, RfidPollCommand, DateTimeOffset>? CommandSent;

    public event Action<YardCommunicationContext, RfidStationConfig, RfidPollCommand, DateTimeOffset>? StationCommandSent;

    public event Action<YardCommunicationContext, AlarmForwardRequest, Exception>? AlarmForwardFailed;

    public event Action<YardCommunicationContext, ExternalAlarmStationSnapshot, Exception>? ExternalAlarmStatusSendFailed;

    public event Action<YardCommunicationContext, ExternalAlarmStationSnapshot, byte[]>? ExternalAlarmStatusSent;

    public event Action<YardCommunicationContext, ExternalAlarmStationSnapshot>? ExternalAlarmStatusUpdated;

    public event Action<YardCommunicationContext, byte[], IPEndPoint, bool, DateTimeOffset>? ExternalAlarmFrameReceived;

    public event Action<YardCommunicationContext, ExternalAlarmClearCommand, IPEndPoint, bool>? ExternalAlarmClearProcessed;

    public event Action<YardCommunicationContext, string, IPEndPoint>? ExternalAlarmFrameRejected;

    public event Action<YardCommunicationContext, Exception>? ExternalAlarmReceiveError;

    public YardCommunicationContext? GetContext(string yardId)
    {
        if (string.IsNullOrWhiteSpace(yardId))
        {
            return null;
        }

        return _contexts.TryGetValue(yardId.Trim(), out var context) ? context : null;
    }

    public bool TryGetExternalAlarmSnapshot(
        string yardId,
        string stationId,
        out ExternalAlarmStationSnapshot snapshot,
        out DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(yardId) || string.IsNullOrWhiteSpace(stationId) ||
            !_externalAlarmStatusCoordinators.TryGetValue(yardId.Trim(), out var coordinator))
        {
            snapshot = null!;
            updatedAt = default;
            return false;
        }

        return coordinator.SnapshotCache.TryGet(stationId, out snapshot, out updatedAt);
    }

    public async Task StartAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await Task.WhenAll(_contexts.Values.Select(context => context.StartAsync(cancellationToken)))
            .ConfigureAwait(false);
        _isStarted = true;
    }

    public async Task StopAllAsync()
    {
        await Task.WhenAll(_contexts.Values.Select(context => context.StopAsync()))
            .ConfigureAwait(false);
        _isStarted = false;
    }

    public async Task RestartAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await Task.WhenAll(_contexts.Values.Select(context => context.RestartAsync(cancellationToken)))
            .ConfigureAwait(false);
        _isStarted = true;
    }

    public async Task ApplyConfigurationsAsync(
        IEnumerable<YardCommunicationConfig> configurations,
        CancellationToken cancellationToken = default)
    {
        if (configurations is null) throw new ArgumentNullException(nameof(configurations));
        ThrowIfDisposed();

        var candidateByYard = new Dictionary<string, YardCommunicationConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var configuration in configurations.Where(item => item is not null))
        {
            var yardId = configuration.YardId?.Trim() ?? string.Empty;
            if (yardId.Length == 0)
            {
                throw new ArgumentException("通信接口所属站场不能为空。", nameof(configurations));
            }

            var validationErrors = configuration.Validate();
            if (validationErrors.Count > 0)
            {
                throw new ArgumentException(
                    string.Join(Environment.NewLine, validationErrors),
                    nameof(configurations));
            }

            if (candidateByYard.ContainsKey(yardId))
            {
                throw new ArgumentException($"站场通信接口所属站场重复：{yardId}。", nameof(configurations));
            }
            candidateByYard.Add(yardId, configuration);
        }

        ValidateCandidateEndpointConflicts(candidateByYard.Values);

        foreach (var current in _contexts.ToArray())
        {
            if (!candidateByYard.TryGetValue(current.Key, out var candidate) ||
                !AreConfigurationsEqual(current.Value.Configuration, candidate))
            {
                await ReplaceContextAsync(current.Key, candidate, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var candidate in candidateByYard)
        {
            if (_contexts.ContainsKey(candidate.Key))
            {
                continue;
            }

            var context = CreateContext(candidate.Value);
            _contexts.Add(candidate.Key, context);
            AttachContext(context);
            context.RestorePendingClear(_recordStore.GetPendingClear());
            context.RestoreUnacknowledgedAlarms(_recordStore.GetUnacknowledgedAlarms());
            if (_isStarted)
            {
                await context.StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void UpdateSettings(RfidSettings settings)
    {
        ThrowIfDisposed();
        foreach (var context in _contexts.Values)
        {
            context.UpdateSettings(settings);
        }
    }

    public void UpdateVehicleConfigurations(IEnumerable<VehicleConfig> vehicleConfigurations)
    {
        if (vehicleConfigurations is null) throw new ArgumentNullException(nameof(vehicleConfigurations));
        ThrowIfDisposed();

        var configurations = vehicleConfigurations.ToArray();
        _vehicleConfigurations = configurations;
        foreach (var context in _contexts.Values)
        {
            context.UpdateVehicleConfigurations(configurations);
        }
    }

    public void ApplyAlarmForwardSenders(
        IReadOnlyDictionary<string, IExternalDataInterface> alarmForwardSenders)
    {
        if (alarmForwardSenders is null) throw new ArgumentNullException(nameof(alarmForwardSenders));
        ThrowIfDisposed();
        var previousSenders = _alarmForwardSenders;
        _alarmForwardSenders = CopyAlarmForwardSenders(alarmForwardSenders);
        foreach (var context in _contexts.Values)
        {
            var sender = context.IsLegacySharedListener
                ? null
                : _alarmForwardSenders.TryGetValue(context.YardId, out var configuredSender)
                    ? configuredSender
                    : null;
            context.UpdateAlarmForwardSender(sender);
        }
        DisposeReplacedSenders(previousSenders, _alarmForwardSenders);
    }

    public void ApplyExternalAlarmStatusCoordinators(
        IReadOnlyDictionary<string, ExternalAlarmStatusCoordinator> externalAlarmStatusCoordinators)
    {
        if (externalAlarmStatusCoordinators is null)
        {
            throw new ArgumentNullException(nameof(externalAlarmStatusCoordinators));
        }

        ThrowIfDisposed();
        var previousCoordinators = _externalAlarmStatusCoordinators;
        var previousStates = previousCoordinators.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ExportState(pair.Key),
            StringComparer.OrdinalIgnoreCase);
        _externalAlarmStatusCoordinators = CopyExternalAlarmStatusCoordinators(externalAlarmStatusCoordinators);
        foreach (var context in _contexts.Values)
        {
            var coordinator = context.IsLegacySharedListener
                ? null
                : _externalAlarmStatusCoordinators.TryGetValue(context.YardId, out var configuredCoordinator)
                    ? configuredCoordinator
                    : null;

            var isReplacement = previousCoordinators.TryGetValue(context.YardId, out var previousCoordinator) &&
                !ReferenceEquals(previousCoordinator, coordinator);
            if (isReplacement && coordinator is not null &&
                previousStates.TryGetValue(context.YardId, out var states))
            {
                coordinator.ConfigureStations(context.Stations);
                coordinator.RestoreState(states);
            }

            context.UpdateExternalAlarmStatusCoordinator(coordinator);
            context.UpdateExternalAlarmUdpChannel(coordinator?.Sender as ExternalAlarmUdpChannel);
        }
        DisposeReplacedCoordinators(previousCoordinators, _externalAlarmStatusCoordinators);
    }

    public Task StartYardAsync(string yardId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return GetRequiredContext(yardId).StartAsync(cancellationToken);
    }

    public Task StopYardAsync(string yardId) => GetRequiredContext(yardId).StopAsync();

    public Task RestartYardAsync(string yardId, CancellationToken cancellationToken = default) =>
        GetRequiredContext(yardId).RestartAsync(cancellationToken);

    public void Evaluate(DateTimeOffset now)
    {
        foreach (var context in _contexts.Values)
        {
            context.Evaluate(now);
        }
    }

    public int RetryPendingPassages() => _contexts.Values.Sum(context => context.RetryPendingPassages());

    public IReadOnlyList<YardCommunicationSnapshot> GetSnapshot() =>
        _contexts.Values
            .Select(context => new YardCommunicationSnapshot(context))
            .ToArray();

    public IReadOnlyList<StationRuntimeState> GetRuntimeStates() =>
        _contexts.Values.SelectMany(context => context.RuntimeStates).ToArray();

    public IReadOnlyList<RfidStationPollingStatus> GetPollingStatuses() =>
        _contexts.Values.SelectMany(context => context.PollingStatuses).ToArray();

    public YardCommunicationContext? FindContextForStation(RfidStationConfig station)
    {
        if (station is null)
        {
            return null;
        }

        var ownedYardId = station.YardId?.Trim();
        if (ownedYardId is not null && ownedYardId.Length > 0)
        {
            return GetContext(ownedYardId);
        }

        return _contexts.Values.SingleOrDefault(context => context.IsLegacySharedListener);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAllAsync().GetAwaiter().GetResult();
        foreach (var context in _contexts.Values.ToArray())
        {
            DetachContext(context);
            context.Dispose();
        }
        _contexts.Clear();
        DisposeSenders(_alarmForwardSenders.Values);
        DisposeReplacedCoordinators(
            _externalAlarmStatusCoordinators,
            new Dictionary<string, ExternalAlarmStatusCoordinator>());
    }

    private YardCommunicationContext CreateContext(YardCommunicationConfig configuration)
    {
        var contextStations = configuration.IsLegacySharedListener
            ? _stations
            : _stations.Where(station => string.Equals(
                station.YardId?.Trim(),
                configuration.YardId?.Trim(),
                StringComparison.OrdinalIgnoreCase)).ToArray();

        ExternalAlarmUdpReceiver? externalAlarmUdpReceiver = null;
        ExternalAlarmUdpChannel? externalAlarmUdpChannel = null;
        ExternalAlarmStatusCoordinator? externalAlarmStatusCoordinator = null;
        if (!configuration.IsLegacySharedListener)
        {
            _externalAlarmStatusCoordinators.TryGetValue(
                configuration.YardId.Trim(),
                out externalAlarmStatusCoordinator);
            externalAlarmUdpChannel = externalAlarmStatusCoordinator?.Sender as ExternalAlarmUdpChannel;
            if (externalAlarmUdpChannel is null &&
                configuration.Enabled &&
                configuration.TryResolveExternalAlarmEndpoint(out var externalAlarmEndpoint))
            {
                externalAlarmUdpReceiver = new ExternalAlarmUdpReceiver(externalAlarmEndpoint);
            }
        }

        return new YardCommunicationContext(
            configuration,
            contextStations,
            _settings,
            _recordStore,
            _timeProvider,
            configuration.IsLegacySharedListener
                ? null
                : _alarmForwardSenders.TryGetValue(configuration.YardId.Trim(), out var sender)
                    ? sender
                    : null,
            externalAlarmStatusCoordinator,
            externalAlarmUdpReceiver,
            externalAlarmUdpChannel,
            _vehicleConfigurations,
            _passageEventLogger);
    }

    private async Task ReplaceContextAsync(
        string yardId,
        YardCommunicationConfig? configuration,
        CancellationToken cancellationToken)
    {
        var current = _contexts[yardId];
        DetachBusinessEvents(current);
        await current.StopAsync().ConfigureAwait(false);
        DetachDatagramEvents(current);
        current.Dispose();
        _contexts.Remove(yardId);

        if (configuration is null)
        {
            return;
        }

        var pendingClearRecords = _recordStore.GetPendingClear();
        var unacknowledgedAlarms = _recordStore.GetUnacknowledgedAlarms();
        var replacement = CreateContext(configuration);
        _contexts[configuration.YardId.Trim()] = replacement;
        AttachContext(replacement);
        replacement.RestorePendingClear(pendingClearRecords);
        replacement.RestoreUnacknowledgedAlarms(unacknowledgedAlarms);
        if (_isStarted)
        {
            await replacement.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool AreConfigurationsEqual(
        YardCommunicationConfig left,
        YardCommunicationConfig right) =>
        string.Equals(left.YardId?.Trim(), right.YardId?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ListenIp?.Trim(), right.ListenIp?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        left.ListenPort == right.ListenPort &&
        string.Equals(left.ExternalAlarmListenIp?.Trim(), right.ExternalAlarmListenIp?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        left.ExternalAlarmListenPort == right.ExternalAlarmListenPort &&
        left.Enabled == right.Enabled &&
        left.IsLegacySharedListener == right.IsLegacySharedListener;

    private static void ValidateCandidateEndpointConflicts(
        IEnumerable<YardCommunicationConfig> configurations)
    {
        var seenEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configuration in configurations.Where(item => item.Enabled))
        {
            if (configuration.TryResolveEndpoint(out var endpoint))
            {
                var endpointKey = $"{endpoint.Address}|{endpoint.Port}";
                if (!seenEndpoints.Add(endpointKey))
                {
                    throw new ArgumentException(
                        $"启用的站场监听端点重复：{endpoint}，监听端点重复。",
                        nameof(configurations));
                }
            }

            if (configuration.TryResolveExternalAlarmEndpoint(out var externalEndpoint))
            {
                var endpointKey = $"{externalEndpoint.Address}|{externalEndpoint.Port}";
                if (!seenEndpoints.Add(endpointKey))
                {
                    throw new ArgumentException(
                        $"启用的外部报警监听端点重复：{externalEndpoint}，监听端点重复。",
                        nameof(configurations));
                }
            }
        }
    }

    private static void AddDuplicateStationAddressDiagnostics(
        IEnumerable<RfidStationConfig> stations,
        ICollection<string> diagnostics)
    {
        var duplicateGroups = stations
            .Where(station => station.Enabled && !string.IsNullOrWhiteSpace(station.YardId))
            .GroupBy(
                station => new
                {
                    YardId = station.YardId!.Trim().ToUpperInvariant(),
                    station.ProtocolAddress
                })
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            var stationIds = string.Join(", ", group.Select(station => station.StationId));
            var yardId = group.First().YardId!.Trim();
            diagnostics.Add(
                $"站场 {yardId} 存在重复 RFID 基站地址 0x{group.Key.ProtocolAddress:X2}：{stationIds}。" +
                "同一站场的外部报警清除命令无法唯一定位。 ");
        }
    }

    private void AttachContext(YardCommunicationContext context)
    {
        context.DatagramReceived += OnContextDatagramReceived;
        context.DatagramSent += OnContextDatagramSent;
        context.ReceiveError += OnContextReceiveError;
        context.CommandSent += OnContextCommandSent;
        context.StationCommandSent += OnContextStationCommandSent;
        context.AlarmForwardFailed += OnContextAlarmForwardFailed;
        context.ExternalAlarmStatusSendFailed += OnContextExternalAlarmStatusSendFailed;
        context.ExternalAlarmStatusSent += OnContextExternalAlarmStatusSent;
        context.ExternalAlarmStatusUpdated += OnContextExternalAlarmStatusUpdated;
        context.ExternalAlarmFrameReceived += OnContextExternalAlarmFrameReceived;
        context.ExternalAlarmClearProcessed += OnContextExternalAlarmClearProcessed;
        context.ExternalAlarmFrameRejected += OnContextExternalAlarmFrameRejected;
        context.ExternalAlarmReceiveError += OnContextExternalAlarmReceiveError;
    }

    private void DetachContext(YardCommunicationContext context)
    {
        DetachBusinessEvents(context);
        DetachDatagramEvents(context);
    }

    private void DetachBusinessEvents(YardCommunicationContext context)
    {
        context.ReceiveError -= OnContextReceiveError;
        context.CommandSent -= OnContextCommandSent;
        context.StationCommandSent -= OnContextStationCommandSent;
        context.AlarmForwardFailed -= OnContextAlarmForwardFailed;
        context.ExternalAlarmStatusSendFailed -= OnContextExternalAlarmStatusSendFailed;
        context.ExternalAlarmStatusSent -= OnContextExternalAlarmStatusSent;
        context.ExternalAlarmStatusUpdated -= OnContextExternalAlarmStatusUpdated;
        context.ExternalAlarmFrameReceived -= OnContextExternalAlarmFrameReceived;
        context.ExternalAlarmClearProcessed -= OnContextExternalAlarmClearProcessed;
        context.ExternalAlarmFrameRejected -= OnContextExternalAlarmFrameRejected;
        context.ExternalAlarmReceiveError -= OnContextExternalAlarmReceiveError;
    }

    private void DetachDatagramEvents(YardCommunicationContext context)
    {
        context.DatagramReceived -= OnContextDatagramReceived;
        context.DatagramSent -= OnContextDatagramSent;
    }

    public static YardCommunicationManager CreateLegacyShared(
        IEnumerable<RfidStationConfig> stations,
        RfidSettings settings,
        IPassageRecordStore recordStore,
        IPAddress listenAddress,
        int listenPort,
        IRfidTimeProvider? timeProvider = null,
        IReadOnlyDictionary<string, ExternalAlarmStatusCoordinator>? externalAlarmStatusCoordinators = null,
        IEnumerable<VehicleConfig>? vehicleConfigurations = null,
        IPassageEventLogger? passageEventLogger = null)
    {
        return new YardCommunicationManager(
            new[]
            {
                new YardCommunicationConfig
                {
                    YardId = LegacySharedListenerId,
                    ListenIp = listenAddress.ToString(),
                    ListenPort = listenPort,
                    Enabled = true,
                    IsLegacySharedListener = true
                }
            },
            stations,
            settings,
            recordStore,
            timeProvider,
            externalAlarmStatusCoordinators: externalAlarmStatusCoordinators,
            vehicleConfigurations: vehicleConfigurations,
            passageEventLogger: passageEventLogger);
    }

    private YardCommunicationContext GetRequiredContext(string yardId)
    {
        var context = GetContext(yardId);
        return context ?? throw new KeyNotFoundException($"未找到站场通信上下文：{yardId}。");
    }

    private void OnContextDatagramReceived(YardCommunicationContext context, RfidUdpDatagramEventArgs args) =>
        DatagramReceived?.Invoke(context, args);

    private void OnContextDatagramSent(YardCommunicationContext context, RfidUdpDatagramSentEventArgs args) =>
        DatagramSent?.Invoke(context, args);

    private void OnContextReceiveError(YardCommunicationContext context, Exception exception) =>
        ReceiveError?.Invoke(context, exception);

    private void OnContextCommandSent(
        YardCommunicationContext context,
        byte stationAddress,
        RfidPollCommand command,
        DateTimeOffset sentAt) =>
        CommandSent?.Invoke(context, stationAddress, command, sentAt);

    private void OnContextStationCommandSent(
        YardCommunicationContext context,
        RfidStationConfig station,
        RfidPollCommand command,
        DateTimeOffset sentAt) =>
        StationCommandSent?.Invoke(context, station, command, sentAt);

    private void OnContextAlarmForwardFailed(
        YardCommunicationContext context,
        AlarmForwardRequest request,
        Exception exception) =>
        AlarmForwardFailed?.Invoke(context, request, exception);

    private void OnContextExternalAlarmStatusSendFailed(
        YardCommunicationContext context,
        ExternalAlarmStationSnapshot snapshot,
        Exception exception) =>
        ExternalAlarmStatusSendFailed?.Invoke(context, snapshot, exception);

    private void OnContextExternalAlarmStatusSent(
        YardCommunicationContext context,
        ExternalAlarmStationSnapshot snapshot,
        byte[] payload) =>
        ExternalAlarmStatusSent?.Invoke(context, snapshot, payload);

    private void OnContextExternalAlarmStatusUpdated(
        YardCommunicationContext context,
        ExternalAlarmStationSnapshot snapshot) =>
        ExternalAlarmStatusUpdated?.Invoke(context, snapshot);

    private void OnContextExternalAlarmFrameReceived(
        YardCommunicationContext context,
        byte[] payload,
        IPEndPoint sourceEndpoint,
        bool isValid,
        DateTimeOffset receivedAt) =>
        ExternalAlarmFrameReceived?.Invoke(context, payload, sourceEndpoint, isValid, receivedAt);

    private void OnContextExternalAlarmClearProcessed(
        YardCommunicationContext context,
        ExternalAlarmClearCommand command,
        IPEndPoint sourceEndpoint,
        bool processed) =>
        ExternalAlarmClearProcessed?.Invoke(context, command, sourceEndpoint, processed);

    private void OnContextExternalAlarmFrameRejected(
        YardCommunicationContext context,
        string reason,
        IPEndPoint sourceEndpoint) =>
        ExternalAlarmFrameRejected?.Invoke(context, reason, sourceEndpoint);

    private void OnContextExternalAlarmReceiveError(
        YardCommunicationContext context,
        Exception exception) =>
        ExternalAlarmReceiveError?.Invoke(context, exception);

    private static IReadOnlyDictionary<string, IExternalDataInterface> CopyAlarmForwardSenders(
        IReadOnlyDictionary<string, IExternalDataInterface>? senders)
    {
        var result = new Dictionary<string, IExternalDataInterface>(StringComparer.OrdinalIgnoreCase);
        if (senders is null)
        {
            return result;
        }

        foreach (var pair in senders)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
            {
                continue;
            }

            result[pair.Key.Trim()] = pair.Value;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, ExternalAlarmStatusCoordinator> CopyExternalAlarmStatusCoordinators(
        IReadOnlyDictionary<string, ExternalAlarmStatusCoordinator>? coordinators)
    {
        var result = new Dictionary<string, ExternalAlarmStatusCoordinator>(StringComparer.OrdinalIgnoreCase);
        if (coordinators is null)
        {
            return result;
        }

        foreach (var pair in coordinators)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
            {
                continue;
            }

            result[pair.Key.Trim()] = pair.Value;
        }

        return result;
    }

    private static void DisposeReplacedSenders(
        IReadOnlyDictionary<string, IExternalDataInterface> previous,
        IReadOnlyDictionary<string, IExternalDataInterface> current)
    {
        DisposeSenders(previous.Values.Where(previousSender =>
            !current.Values.Any(currentSender => ReferenceEquals(previousSender, currentSender))));
    }

    private static void DisposeSenders(IEnumerable<IExternalDataInterface> senders)
    {
        var disposed = new List<IExternalDataInterface>();
        foreach (var sender in senders)
        {
            if (sender is IDisposable disposable &&
                !disposed.Any(item => ReferenceEquals(item, sender)))
            {
                disposable.Dispose();
                disposed.Add(sender);
            }
        }
    }

    private static void DisposeReplacedCoordinators(
        IReadOnlyDictionary<string, ExternalAlarmStatusCoordinator> previous,
        IReadOnlyDictionary<string, ExternalAlarmStatusCoordinator> current)
    {
        var disposed = new List<ExternalAlarmStatusCoordinator>();
        foreach (var previousCoordinator in previous.Values)
        {
            if (current.Values.Any(currentCoordinator => ReferenceEquals(previousCoordinator, currentCoordinator)) ||
                disposed.Any(item => ReferenceEquals(item, previousCoordinator)))
            {
                continue;
            }

            previousCoordinator.Dispose();
            disposed.Add(previousCoordinator);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(YardCommunicationManager));
        }
    }
}

public sealed class YardCommunicationSnapshot
{
    internal YardCommunicationSnapshot(YardCommunicationContext context)
    {
        YardId = context.YardId;
        ListenEndpoint = context.ListenerEndPoint ??
            (context.Configuration.TryResolveEndpoint(out var configuredEndpoint) ? configuredEndpoint : null);
        IsRunning = context.IsRunning;
        IsLegacySharedListener = context.IsLegacySharedListener;
        StationIds = context.Stations.Select(station => station.StationId).ToArray();
        RequestCount = context.RequestCount;
        ResponseCount = context.ResponseCount;
        ClearCount = context.ClearCount;
        ErrorCount = context.ErrorCount;
        LastCommand = context.LastCommand;
        LastError = context.LastError;
    }

    public string YardId { get; }

    public IPEndPoint? ListenEndpoint { get; }

    public bool IsRunning { get; }

    public bool IsLegacySharedListener { get; }

    public IReadOnlyList<string> StationIds { get; }

    public long RequestCount { get; }

    public long ResponseCount { get; }

    public long ClearCount { get; }

    public long ErrorCount { get; }

    public string? LastCommand { get; }

    public string? LastError { get; }
}
