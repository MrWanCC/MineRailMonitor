namespace MineRailMonitor.Core.ExternalAlarm;

/// <summary>
/// Sends one current External Alarm snapshot per configured station every 200 ms.
/// One scheduler belongs to one yard coordinator; station workers are isolated so
/// a failed send does not stop the other station workers.
/// </summary>
public sealed class ExternalAlarmStatusScheduler : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(200);

    private readonly object _syncRoot = new();
    private readonly ExternalAlarmStatusCache _cache;
    private readonly ExternalAlarmFrameCodec _codec;
    private readonly IExternalAlarmStatusSender _sender;
    private readonly IReadOnlyDictionary<string, byte> _stations;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _normalRetention;
    private readonly Func<DateTimeOffset> _clock;
    private CancellationTokenSource? _cancellationTokenSource;
    private IReadOnlyList<Task> _stationTasks = Array.Empty<Task>();
    private bool _disposed;

    public ExternalAlarmStatusScheduler(
        ExternalAlarmStatusCache cache,
        ExternalAlarmFrameCodec codec,
        IExternalAlarmStatusSender sender,
        IReadOnlyDictionary<string, byte> stations,
        TimeSpan? interval = null,
        TimeSpan? normalRetention = null,
        Func<DateTimeOffset>? clock = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        if (stations is null) throw new ArgumentNullException(nameof(stations));

        _stations = stations
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        _interval = interval ?? DefaultInterval;
        _normalRetention = normalRetention ?? ExternalAlarmStatusCache.DefaultNormalRetention;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        if (_interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        if (_normalRetention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(normalRetention));
        }

        foreach (var station in _stations)
        {
            _cache.EnsureStation(station.Key, station.Value, _clock());
        }
    }

    public event Action<ExternalAlarmStationSnapshot, Exception>? SendFailed;

    public event Action<ExternalAlarmStationSnapshot, byte[]>? Sent;

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _cancellationTokenSource is not null;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            if (_cancellationTokenSource is not null)
            {
                return Task.CompletedTask;
            }

            var schedulerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationTokenSource = schedulerCts;
            _stationTasks = _stations
                .Select(station => RunStationAsync(station.Key, schedulerCts.Token))
                .ToArray();
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellationTokenSource;
        IReadOnlyList<Task> stationTasks;
        lock (_syncRoot)
        {
            cancellationTokenSource = _cancellationTokenSource;
            stationTasks = _stationTasks;
            _cancellationTokenSource = null;
            _stationTasks = Array.Empty<Task>();
        }

        if (cancellationTokenSource is null)
        {
            return;
        }

        cancellationTokenSource.Cancel();
        try
        {
            await Task.WhenAll(stationTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
        finally
        {
            cancellationTokenSource.Dispose();
        }
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
    }

    private async Task RunStationAsync(
        string stationId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var cycleStartedAt = _clock();
            var snapshot = _cache.GetForSending(stationId, cycleStartedAt, _normalRetention);
            try
            {
                var payload = _codec.Encode(snapshot);
                await _sender.SendAsync(payload, cancellationToken).ConfigureAwait(false);
                Sent?.Invoke(snapshot, payload);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                SendFailed?.Invoke(snapshot, exception);
            }

            var elapsed = _clock() - cycleStartedAt;
            var delay = _interval - elapsed;
            if (delay <= TimeSpan.Zero)
            {
                continue;
            }

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ExternalAlarmStatusScheduler));
        }
    }
}
