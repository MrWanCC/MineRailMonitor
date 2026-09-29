using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.ExternalAlarm;

public sealed class ExternalAlarmStatusCoordinator : IDisposable
{
    private readonly ExternalAlarmFrameCodec _codec;
    private readonly IExternalAlarmStatusSender _sender;
    private readonly ExternalAlarmStatusCache _snapshotCache;
    private readonly object _syncRoot = new();
    private IReadOnlyDictionary<string, byte> _stations =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
    private ExternalAlarmStatusScheduler? _scheduler;
    private bool _disposed;

    public ExternalAlarmStatusCoordinator(
        ExternalAlarmFrameCodec codec,
        IExternalAlarmStatusSender sender,
        ExternalAlarmStatusCache? snapshotCache = null)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _snapshotCache = snapshotCache ?? new ExternalAlarmStatusCache();
    }

    public event Action<ExternalAlarmStationSnapshot, Exception>? SendFailed;

    public event Action<ExternalAlarmStationSnapshot, byte[]>? Sent;

    public IExternalAlarmStatusSender Sender => _sender;

    public ExternalAlarmStatusCache SnapshotCache => _snapshotCache;

    public ExternalAlarmStatusScheduler? Scheduler
    {
        get
        {
            lock (_syncRoot)
            {
                return _scheduler;
            }
        }
    }

    public void ConfigureStations(IEnumerable<RfidStationConfig> stations)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));

        var configuredStations = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var station in stations.Where(item => item is not null && item.Enabled))
        {
            if (string.IsNullOrWhiteSpace(station.StationId))
            {
                continue;
            }

            configuredStations[station.StationId.Trim()] = station.ProtocolAddress;
        }

        ExternalAlarmStatusScheduler? previousScheduler;
        var restart = false;
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            _stations = configuredStations;
            previousScheduler = _scheduler;
            _scheduler = null;
            restart = previousScheduler?.IsRunning == true;
        }

        if (previousScheduler is not null)
        {
            previousScheduler.SendFailed -= OnSchedulerSendFailed;
            previousScheduler.Sent -= OnSchedulerSent;
            previousScheduler.Dispose();
        }

        if (restart)
        {
            StartAsync().GetAwaiter().GetResult();
        }
    }

    public void UpdateSnapshot(
        string stationId,
        ExternalAlarmStationSnapshot snapshot,
        DateTimeOffset? updatedAt = null) =>
        _snapshotCache.Update(stationId, snapshot, updatedAt);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            if (_scheduler is not null)
            {
                return Task.CompletedTask;
            }

            var scheduler = new ExternalAlarmStatusScheduler(
                _snapshotCache,
                _codec,
                _sender,
                _stations);
            scheduler.SendFailed += OnSchedulerSendFailed;
            scheduler.Sent += OnSchedulerSent;
            _scheduler = scheduler;
            scheduler.StartAsync(cancellationToken);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        ExternalAlarmStatusScheduler? scheduler;
        lock (_syncRoot)
        {
            scheduler = _scheduler;
            _scheduler = null;
        }

        if (scheduler is null)
        {
            return;
        }

        scheduler.SendFailed -= OnSchedulerSendFailed;
        scheduler.Sent -= OnSchedulerSent;
        await scheduler.StopAsync().ConfigureAwait(false);
        scheduler.Dispose();
    }

    public Task PublishAsync(
        StationRuntimeState state,
        CancellationToken cancellationToken = default)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = ExternalAlarmStatusMapper.FromRuntimeState(state);
        UpdateSnapshot(state.StationId, snapshot, state.LastResponseAt);
        return Task.CompletedTask;
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

        StopAsync().GetAwaiter().GetResult();
        if (_sender is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void OnSchedulerSendFailed(
        ExternalAlarmStationSnapshot snapshot,
        Exception exception) =>
        SendFailed?.Invoke(snapshot, exception);

    private void OnSchedulerSent(
        ExternalAlarmStationSnapshot snapshot,
        byte[] payload) =>
        Sent?.Invoke(snapshot, payload);

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ExternalAlarmStatusCoordinator));
        }
    }
}
