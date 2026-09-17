using System.Net;
using MineRailMonitor.Core.Communication;
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
    private readonly IPassageRecordStore _recordStore;
    private readonly IRfidTimeProvider? _timeProvider;
    private RfidSettings _settings;
    private readonly IReadOnlyList<string> _diagnostics;
    private bool _isStarted;
    private bool _disposed;

    public YardCommunicationManager(
        IEnumerable<YardCommunicationConfig> configurations,
        IEnumerable<RfidStationConfig> stations,
        RfidSettings settings,
        IPassageRecordStore recordStore,
        IRfidTimeProvider? timeProvider = null)
    {
        if (configurations is null) throw new ArgumentNullException(nameof(configurations));
        if (stations is null) throw new ArgumentNullException(nameof(stations));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        if (recordStore is null) throw new ArgumentNullException(nameof(recordStore));

        _settings = settings;
        _recordStore = recordStore;
        _timeProvider = timeProvider;
        _stations = stations.Where(item => item is not null).ToArray();

        var diagnostics = new List<string>();
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

    public event Action<YardCommunicationContext, Exception>? ReceiveError;

    public event Action<YardCommunicationContext, byte, RfidPollCommand, DateTimeOffset>? CommandSent;

    public YardCommunicationContext? GetContext(string yardId)
    {
        if (string.IsNullOrWhiteSpace(yardId))
        {
            return null;
        }

        return _contexts.TryGetValue(yardId.Trim(), out var context) ? context : null;
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
    }

    private YardCommunicationContext CreateContext(YardCommunicationConfig configuration)
    {
        var contextStations = configuration.IsLegacySharedListener
            ? _stations
            : _stations.Where(station => string.Equals(
                station.YardId?.Trim(),
                configuration.YardId?.Trim(),
                StringComparison.OrdinalIgnoreCase)).ToArray();
        return new YardCommunicationContext(
            configuration,
            contextStations,
            _settings,
            _recordStore,
            _timeProvider);
    }

    private async Task ReplaceContextAsync(
        string yardId,
        YardCommunicationConfig? configuration,
        CancellationToken cancellationToken)
    {
        var current = _contexts[yardId];
        DetachContext(current);
        current.Dispose();
        _contexts.Remove(yardId);

        if (configuration is null)
        {
            return;
        }

        var replacement = CreateContext(configuration);
        _contexts[configuration.YardId.Trim()] = replacement;
        AttachContext(replacement);
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
        left.Enabled == right.Enabled &&
        left.IsLegacySharedListener == right.IsLegacySharedListener;

    private static void ValidateCandidateEndpointConflicts(
        IEnumerable<YardCommunicationConfig> configurations)
    {
        var seenEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configuration in configurations.Where(item => item.Enabled))
        {
            if (!configuration.TryResolveEndpoint(out var endpoint))
            {
                continue;
            }

            var endpointKey = $"{endpoint.Address}|{endpoint.Port}";
            if (!seenEndpoints.Add(endpointKey))
            {
                throw new ArgumentException(
                    $"启用的站场监听端点重复：{endpoint}，监听端点重复。",
                    nameof(configurations));
            }
        }
    }

    private void AttachContext(YardCommunicationContext context)
    {
        context.DatagramReceived += OnContextDatagramReceived;
        context.ReceiveError += OnContextReceiveError;
        context.CommandSent += OnContextCommandSent;
    }

    private void DetachContext(YardCommunicationContext context)
    {
        context.DatagramReceived -= OnContextDatagramReceived;
        context.ReceiveError -= OnContextReceiveError;
        context.CommandSent -= OnContextCommandSent;
    }

    public static YardCommunicationManager CreateLegacyShared(
        IEnumerable<RfidStationConfig> stations,
        RfidSettings settings,
        IPassageRecordStore recordStore,
        IPAddress listenAddress,
        int listenPort,
        IRfidTimeProvider? timeProvider = null)
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
            timeProvider);
    }

    private YardCommunicationContext GetRequiredContext(string yardId)
    {
        var context = GetContext(yardId);
        return context ?? throw new KeyNotFoundException($"未找到站场通信上下文：{yardId}。");
    }

    private void OnContextDatagramReceived(YardCommunicationContext context, RfidUdpDatagramEventArgs args) =>
        DatagramReceived?.Invoke(context, args);

    private void OnContextReceiveError(YardCommunicationContext context, Exception exception) =>
        ReceiveError?.Invoke(context, exception);

    private void OnContextCommandSent(
        YardCommunicationContext context,
        byte stationAddress,
        RfidPollCommand command,
        DateTimeOffset sentAt) =>
        CommandSent?.Invoke(context, stationAddress, command, sentAt);

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
