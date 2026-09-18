using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Core.Recognition;

namespace MineRailMonitor.Core.Services;

/// <summary>
/// Owns all communication state for one yard without sharing a listener,
/// poller, or runtime coordinator with another yard.
/// </summary>
public sealed class YardCommunicationContext : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly IReadOnlyList<RfidStationConfig> _stations;
    private RfidSettings _settings;
    private readonly IPassageRecordStore _recordStore;
    private readonly IRfidTimeProvider _timeProvider;
    private RfidUdpTransport? _transport;
    private RfidStationPoller? _poller;
    private RfidRuntimeCoordinator? _runtimeCoordinator;
    private CancellationTokenSource? _pollerCts;
    private Task? _listenerTask;
    private Task? _pollerTask;
    private bool _isRunning;
    private bool _disposed;
    private long _clearCount;
    private long _errorCount;
    private long _requestCount;
    private long _responseCount;
    private DateTimeOffset? _lastRequestAt;
    private string? _lastCommand;
    private string? _lastError;

    public YardCommunicationContext(
        YardCommunicationConfig configuration,
        IEnumerable<RfidStationConfig> stations,
        RfidSettings settings,
        IPassageRecordStore recordStore,
        IRfidTimeProvider? timeProvider = null)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        if (stations is null) throw new ArgumentNullException(nameof(stations));
        _stations = stations.ToArray();
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        _timeProvider = timeProvider ?? new SystemRfidTimeProvider();
        try
        {
            _runtimeCoordinator = CreateRuntimeCoordinator();
            if (_runtimeCoordinator is not null)
            {
                _runtimeCoordinator.CommandSent += OnRuntimeCommandSent;
                _runtimeCoordinator.StationCommandSent += OnRuntimeStationCommandSent;
            }
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
    }

    public YardCommunicationConfig Configuration { get; }

    public string YardId => Configuration.YardId;

    public bool IsLegacySharedListener => Configuration.IsLegacySharedListener;

    public IReadOnlyList<RfidStationConfig> Stations => _stations;

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _isRunning;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastError;
            }
        }
    }

    public IPEndPoint? ListenerEndPoint => _transport?.LocalEndPoint;

    public RfidStationPoller? Poller => _poller;

    public RfidRuntimeCoordinator? RuntimeCoordinator => _runtimeCoordinator;

    public IReadOnlyCollection<StationRuntimeState> RuntimeStates =>
        _runtimeCoordinator?.States.Values.ToArray() ?? Array.Empty<StationRuntimeState>();

    public IReadOnlyCollection<RfidStationPollingStatus> PollingStatuses =>
        _poller?.EndpointStatuses.Values.ToArray() ?? Array.Empty<RfidStationPollingStatus>();

    public long RequestCount => Interlocked.Read(ref _requestCount) + PollingStatuses.Sum(status => status.RequestCount);

    public long ResponseCount => Interlocked.Read(ref _responseCount) + PollingStatuses.Sum(status => status.ResponseCount);

    public long ClearCount => Interlocked.Read(ref _clearCount);

    public long ErrorCount => Interlocked.Read(ref _errorCount);

    public string? LastCommand
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastCommand;
            }
        }
    }

    public DateTimeOffset? LastRequestAt
    {
        get
        {
            var current = PollingStatuses
                .Where(status => status.LastRequestAt.HasValue)
                .Select(status => status.LastRequestAt)
                .DefaultIfEmpty()
                .Max();
            var previous = _lastRequestAt;
            if (!current.HasValue)
            {
                return previous;
            }
            return previous.HasValue && previous.Value > current.Value ? previous : current;
        }
    }

    public event Action<YardCommunicationContext, RfidUdpDatagramEventArgs>? DatagramReceived;

    public event Action<YardCommunicationContext, RfidUdpDatagramSentEventArgs>? DatagramSent;

    public event Action<YardCommunicationContext, Exception>? ReceiveError;

    public event Action<YardCommunicationContext, byte, RfidPollCommand, DateTimeOffset>? CommandSent;

    public event Action<YardCommunicationContext, RfidStationConfig, RfidPollCommand, DateTimeOffset>? StationCommandSent;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        await StopAsync().ConfigureAwait(false);

        var validationErrors = Configuration.Validate();
        if (validationErrors.Count > 0)
        {
            SetError(string.Join(Environment.NewLine, validationErrors));
            return;
        }
        if (!Configuration.Enabled)
        {
            ClearError();
            return;
        }

        if (!Configuration.TryResolveEndpoint(out var endpoint))
        {
            SetError($"站场 {YardId} 的监听端点无效。");
            return;
        }

        try
        {
            var transport = new RfidUdpTransport(endpoint.Address, endpoint.Port);
            transport.DatagramReceived += OnTransportDatagramReceived;
            transport.DatagramSent += OnTransportDatagramSent;
            transport.ReceiveError += OnTransportReceiveError;

            lock (_syncRoot)
            {
                _transport = transport;
                _listenerTask = transport.StartAsync(CancellationToken.None);
                _isRunning = true;
                _lastError = null;
            }

            if (_stations.Any(station => station.Enabled))
            {
                var poller = new RfidStationPoller(
                    _stations,
                    _settings.PollIntervalMs,
                    transport,
                    _timeProvider,
                    _runtimeCoordinator,
                    _runtimeCoordinator?.OfflineTimeout);
                poller.CommandSent += OnPollerCommandSent;
                var pollerCts = new CancellationTokenSource();
                lock (_syncRoot)
                {
                    _poller = poller;
                    _pollerCts = pollerCts;
                    _pollerTask = RunPollerAsync(poller, pollerCts.Token);
                }
            }
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
            await StopAsync().ConfigureAwait(false);
        }
    }

    public async Task StopAsync()
    {
        Task? listenerTask;
        Task? pollerTask;
        CancellationTokenSource? pollerCts;
        RfidUdpTransport? transport;
        RfidStationPoller? poller;
        lock (_syncRoot)
        {
            _isRunning = false;
            listenerTask = _listenerTask;
            pollerTask = _pollerTask;
            pollerCts = _pollerCts;
            transport = _transport;
            poller = _poller;
            _listenerTask = null;
            _pollerTask = null;
            _pollerCts = null;
            _poller = null;
            _transport = null;
        }

        pollerCts?.Cancel();
        transport?.Stop();
        await IgnoreTaskFailureAsync(pollerTask).ConfigureAwait(false);
        await IgnoreTaskFailureAsync(listenerTask).ConfigureAwait(false);
        if (poller is not null)
        {
            Interlocked.Add(ref _requestCount, poller.EndpointStatuses.Values.Sum(status => status.RequestCount));
            Interlocked.Add(ref _responseCount, poller.EndpointStatuses.Values.Sum(status => status.ResponseCount));
            var lastRequestAt = poller.EndpointStatuses.Values
                .Where(status => status.LastRequestAt.HasValue)
                .Select(status => status.LastRequestAt)
                .DefaultIfEmpty()
                .Max();
            if (lastRequestAt.HasValue && (!_lastRequestAt.HasValue || lastRequestAt.Value > _lastRequestAt.Value))
            {
                _lastRequestAt = lastRequestAt;
            }
        }
        pollerCts?.Dispose();
        if (poller is not null)
        {
            poller.CommandSent -= OnPollerCommandSent;
        }
        if (transport is not null)
        {
            transport.DatagramReceived -= OnTransportDatagramReceived;
            transport.DatagramSent -= OnTransportDatagramSent;
            transport.ReceiveError -= OnTransportReceiveError;
        }
        transport?.Dispose();
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Evaluate(DateTimeOffset now)
    {
        _runtimeCoordinator?.Evaluate(now);
        if (_poller is null || _runtimeCoordinator is null)
        {
            return;
        }

        _poller.EvaluateTimeouts(now, _runtimeCoordinator.OfflineTimeout);
        _poller.SynchronizeOnlineStates(_runtimeCoordinator.EndpointStates);
    }

    public void UpdateSettings(RfidSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        var validationErrors = settings.Validate();
        if (validationErrors.Count > 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, validationErrors), nameof(settings));
        }

        lock (_syncRoot)
        {
            _settings = settings;
            _runtimeCoordinator?.UpdateDefaults(settings);
        }
    }

    public bool RecordResponse(IPEndPoint sourceEndpoint, byte protocolAddress, DateTimeOffset receivedAt) =>
        _poller?.RecordResponse(sourceEndpoint, protocolAddress, receivedAt) == true;

    public StationRecognitionSession? ProcessFrame(RfidStationFrame frame) =>
        _runtimeCoordinator?.ProcessFrame(frame);

    public void RestorePendingClear(IEnumerable<PassageRecord> records) =>
        _runtimeCoordinator?.RestorePendingClear(records);

    public async Task SendAsync(
        RfidStationConfig station,
        RfidPollCommand command,
        CancellationToken cancellationToken = default)
    {
        if (station is null) throw new ArgumentNullException(nameof(station));
        ThrowIfDisposed();
        if (!_stations.Any(item => string.Equals(item.StationId, station.StationId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"RFID基站不属于站场通信上下文：{YardId} / {station.StationId}。");
        }
        if (_transport is null || !IsRunning)
        {
            throw new InvalidOperationException($"站场通信上下文未运行：{YardId}。");
        }
        if (!station.TryResolveEndpoint(out var endpoint))
        {
            throw new InvalidOperationException($"RFID基站端点配置无效：{station.StationId}。");
        }

        await _transport.SendAsync(
            RfidRequestFrameBuilder.Build(station, command),
            endpoint,
            cancellationToken).ConfigureAwait(false);
        var sentAt = _timeProvider.UtcNow;
        _poller?.RecordSent(endpoint, station.ProtocolAddress, sentAt);
        PublishStationCommandSent(station, command, sentAt);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (_runtimeCoordinator is not null)
        {
            _runtimeCoordinator.CommandSent -= OnRuntimeCommandSent;
            _runtimeCoordinator.StationCommandSent -= OnRuntimeStationCommandSent;
        }
        StopAsync().GetAwaiter().GetResult();
    }

    private RfidRuntimeCoordinator? CreateRuntimeCoordinator() =>
        _stations.Any(station => station.Enabled)
            ? new RfidRuntimeCoordinator(_stations, _settings, _recordStore)
            : null;

    private async Task RunPollerAsync(RfidStationPoller poller, CancellationToken cancellationToken)
    {
        try
        {
            await poller.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
    }

    private void OnTransportDatagramReceived(object? sender, RfidUdpDatagramEventArgs args)
    {
        DatagramReceived?.Invoke(this, args);
    }

    private void OnTransportDatagramSent(object? sender, RfidUdpDatagramSentEventArgs args)
    {
        DatagramSent?.Invoke(this, args);
    }

    private void OnTransportReceiveError(Exception exception)
    {
        Interlocked.Increment(ref _errorCount);
        SetError(exception.Message);
        ReceiveError?.Invoke(this, exception);
    }

    private void OnRuntimeCommandSent(byte stationAddress, RfidPollCommand command, DateTimeOffset sentAt)
    {
        if (command == RfidPollCommand.Clear)
        {
            Interlocked.Increment(ref _clearCount);
        }
        PublishCommandSent(stationAddress, command, sentAt);
    }

    private void OnPollerCommandSent(RfidStationConfig station, RfidPollCommand command, DateTimeOffset sentAt)
    {
        // Clear is already published by the runtime coordinator after its
        // lifecycle transition. Publish automatic reads here so every actual
        // wire command reaches diagnostics exactly once.
        if (command == RfidPollCommand.Read)
        {
            PublishStationCommandSent(station, command, sentAt);
        }
    }

    private void OnRuntimeStationCommandSent(
        RfidStationConfig station,
        RfidPollCommand command,
        DateTimeOffset sentAt) =>
        PublishStationCommandSent(station, command, sentAt);

    private void PublishCommandSent(byte stationAddress, RfidPollCommand command, DateTimeOffset sentAt)
    {
        lock (_syncRoot)
        {
            _lastCommand = command.ToString();
        }
        CommandSent?.Invoke(this, stationAddress, command, sentAt);
    }

    private void PublishStationCommandSent(
        RfidStationConfig station,
        RfidPollCommand command,
        DateTimeOffset sentAt)
    {
        lock (_syncRoot)
        {
            _lastCommand = command.ToString();
        }
        StationCommandSent?.Invoke(this, station, command, sentAt);
    }

    private void SetError(string message)
    {
        Interlocked.Increment(ref _errorCount);
        lock (_syncRoot)
        {
            _lastError = message;
        }
    }

    private void ClearError()
    {
        lock (_syncRoot)
        {
            _lastError = null;
        }
    }

    private static async Task IgnoreTaskFailureAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(YardCommunicationContext));
            }
        }
    }
}
