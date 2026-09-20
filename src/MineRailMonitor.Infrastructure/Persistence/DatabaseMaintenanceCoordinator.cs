using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Infrastructure.Logging;

namespace MineRailMonitor.Infrastructure.Persistence;

public sealed class DatabaseMaintenanceCoordinator : IDisposable
{
    private readonly string _productionDatabasePath;
    private readonly string _backupRootDirectory;
    private readonly ISqliteBackupService _backupService;
    private readonly SqliteRetentionService _retentionService;
    private readonly IRfidTimeProvider _timeProvider;
    private readonly IAsyncDelay _delay;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _serialGate = new(1, 1);
    private readonly object _lifecycleSyncRoot = new();

    private CancellationTokenSource? _schedulerCancellation;
    private Task? _schedulerTask;
    private Task? _stopTask;
    private bool _disposed;

    public DatabaseMaintenanceCoordinator(
        string productionDatabasePath,
        string backupRootDirectory,
        ISqliteBackupService backupService,
        SqliteRetentionService retentionService,
        IRfidTimeProvider timeProvider,
        IAsyncDelay delay,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(productionDatabasePath))
        {
            throw new ArgumentException("生产数据库路径不能为空。", nameof(productionDatabasePath));
        }

        if (string.IsNullOrWhiteSpace(backupRootDirectory))
        {
            throw new ArgumentException("备份根目录不能为空。", nameof(backupRootDirectory));
        }

        _productionDatabasePath = productionDatabasePath;
        _backupRootDirectory = backupRootDirectory;
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _retentionService = retentionService ?? throw new ArgumentNullException(nameof(retentionService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SqliteBackupResult> RunStartupCatchUpAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _serialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var localNow = GetLocalNow();
            _retentionService.CleanupStaleTemporaryFiles(_backupRootDirectory, localNow);
            return await RunBackupIfNeededAsync(localNow, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _serialGate.Release();
        }
    }

    public Task StartAsync(CancellationToken applicationStopping)
    {
        lock (_lifecycleSyncRoot)
        {
            ThrowIfDisposed();
            if (_schedulerTask != null)
            {
                return Task.CompletedTask;
            }

            _schedulerCancellation = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
            var cancellationToken = _schedulerCancellation.Token;
            _schedulerTask = Task.Run(() => SchedulerLoopAsync(cancellationToken));
            return Task.CompletedTask;
        }
    }

    public Task StopAsync()
    {
        lock (_lifecycleSyncRoot)
        {
            if (_stopTask != null)
            {
                return _stopTask;
            }

            _stopTask = StopCoreAsync(_schedulerCancellation, _schedulerTask);
            return _stopTask;
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        lock (_lifecycleSyncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _serialGate.Dispose();
    }

    private async Task SchedulerLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var localNow = GetLocalNow();
                var nextRun = GetNextTwoAm(localNow);
                var wait = nextRun - localNow;
                await _delay.DelayAsync(wait, cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await RunScheduledTickAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.Error("Scheduled SQLite backup failed.", exception);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error("SQLite maintenance scheduler stopped unexpectedly.", exception);
        }
    }

    private async Task RunScheduledTickAsync(CancellationToken cancellationToken)
    {
        await _serialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var localNow = GetLocalNow();
            _retentionService.CleanupStaleTemporaryFiles(_backupRootDirectory, localNow);
            await RunBackupIfNeededAsync(localNow, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _serialGate.Release();
        }
    }

    private async Task<SqliteBackupResult> RunBackupIfNeededAsync(
        DateTimeOffset localNow,
        CancellationToken cancellationToken)
    {
        var candidates = _backupService.ScanCandidates(_backupRootDirectory);
        var today = candidates.FirstOrDefault(candidate => candidate.LocalTimestamp.Date == localNow.Date);
        if (today != null)
        {
            return new SqliteBackupResult(true, today.Path, null);
        }

        var result = await _backupService.CreateValidatedBackupAsync(
                _productionDatabasePath,
                _backupRootDirectory,
                localNow,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            _retentionService.Apply(_backupRootDirectory, localNow);
        }

        return result;
    }

    private async Task StopCoreAsync(
        CancellationTokenSource? schedulerCancellation,
        Task? schedulerTask)
    {
        schedulerCancellation?.Cancel();
        if (schedulerTask != null)
        {
            await schedulerTask.ConfigureAwait(false);
        }

        await _serialGate.WaitAsync().ConfigureAwait(false);
        _serialGate.Release();
        schedulerCancellation?.Dispose();
    }

    private DateTimeOffset GetLocalNow() => _timeProvider.UtcNow.ToLocalTime();

    private static DateTimeOffset GetNextTwoAm(DateTimeOffset localNow)
    {
        var next = new DateTimeOffset(
            localNow.Year,
            localNow.Month,
            localNow.Day,
            2,
            0,
            0,
            localNow.Offset);
        return localNow < next ? next : next.AddDays(1);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DatabaseMaintenanceCoordinator));
        }
    }
}
