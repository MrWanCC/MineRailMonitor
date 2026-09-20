using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Infrastructure.Logging;
using MineRailMonitor.Infrastructure.Persistence;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class DatabaseMaintenanceCoordinatorTests
{
    [Fact]
    public async Task Existing_formal_backup_for_today_skips_backup_after_process_restart()
    {
        using var directory = new TemporaryDirectory();
        var today = CreateLocalNow(1, 0, 0);
        var candidatePath = directory.CreateFormal(today.Date, "010000");
        var backup = new RecordingBackupService(new[] { new SqliteBackupCandidate(candidatePath, today) });
        using var coordinator = CreateCoordinator(directory, backup, today);

        var result = await coordinator.RunStartupCatchUpAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(candidatePath, result.FinalPath);
        Assert.Equal(0, backup.CreateCalls);
    }

    [Fact]
    public async Task Failed_backup_does_not_run_destructive_retention()
    {
        using var directory = new TemporaryDirectory();
        var localNow = CreateLocalNow(1, 0, 0);
        var oldFormal = directory.CreateFormal(localNow.Date.AddDays(-14), "010000");
        var backup = new RecordingBackupService(
            Array.Empty<SqliteBackupCandidate>(),
            new SqliteBackupResult(false, null, "failed"));
        using var coordinator = CreateCoordinator(directory, backup, localNow);

        var result = await coordinator.RunStartupCatchUpAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(oldFormal));
    }

    [Fact]
    public async Task Scheduled_tick_cleans_stale_tmp_even_when_today_backup_already_exists()
    {
        using var directory = new TemporaryDirectory();
        var localNow = CreateLocalNow(1, 0, 0);
        var stale = directory.CreateTmp(localNow.Date, "010000");
        File.SetLastWriteTimeUtc(stale, localNow.UtcDateTime.AddHours(-25));
        var candidatePath = directory.CreateFormal(localNow.Date, "000000");
        var delay = new ManualAsyncDelay();
        var backup = new RecordingBackupService(new[]
        {
            new SqliteBackupCandidate(candidatePath, localNow),
        });
        using var coordinator = CreateCoordinator(directory, backup, localNow, delay);

        await coordinator.StartAsync(CancellationToken.None);
        await delay.WaitUntilCapturedAsync();
        delay.ReleaseNext();
        await backup.WaitUntilScanCapturedAsync();

        Assert.False(File.Exists(stale));
        Assert.Equal(0, backup.CreateCalls);
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task Scheduled_tick_cleans_stale_tmp_even_when_backup_fails()
    {
        using var directory = new TemporaryDirectory();
        var localNow = CreateLocalNow(1, 0, 0);
        var stale = directory.CreateTmp(localNow.Date, "010000");
        File.SetLastWriteTimeUtc(stale, localNow.UtcDateTime.AddHours(-25));
        var delay = new ManualAsyncDelay();
        var backup = new RecordingBackupService(
            Array.Empty<SqliteBackupCandidate>(),
            new SqliteBackupResult(false, null, "failed"));
        using var coordinator = CreateCoordinator(directory, backup, localNow, delay);

        await coordinator.StartAsync(CancellationToken.None);
        await delay.WaitUntilCapturedAsync();
        delay.ReleaseNext();
        await backup.WaitUntilCreateCapturedAsync();

        Assert.False(File.Exists(stale));
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task StartAsync_does_not_run_an_immediate_backup()
    {
        using var directory = new TemporaryDirectory();
        var localNow = CreateLocalNow(1, 0, 0);
        var delay = new ManualAsyncDelay();
        var backup = new RecordingBackupService(Array.Empty<SqliteBackupCandidate>());
        using var coordinator = CreateCoordinator(directory, backup, localNow, delay);

        await coordinator.StartAsync(CancellationToken.None);

        await delay.WaitUntilCapturedAsync();
        Assert.Equal(0, backup.CreateCalls);
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task StartAsync_is_idempotent_and_starts_one_scheduler_loop()
    {
        using var directory = new TemporaryDirectory();
        var localNow = CreateLocalNow(1, 0, 0);
        var delay = new ManualAsyncDelay();
        using var coordinator = CreateCoordinator(
            directory,
            new RecordingBackupService(Array.Empty<SqliteBackupCandidate>()),
            localNow,
            delay);

        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartAsync(CancellationToken.None);

        await delay.WaitUntilCapturedAsync();
        Assert.Single(delay.CapturedDelays);
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task Scheduler_waits_until_next_local_0200()
    {
        using var directory = new TemporaryDirectory();
        var localNow = CreateLocalNow(1, 0, 0);
        var delay = new ManualAsyncDelay();
        using var coordinator = CreateCoordinator(
            directory,
            new RecordingBackupService(Array.Empty<SqliteBackupCandidate>()),
            localNow,
            delay);

        await coordinator.StartAsync(CancellationToken.None);

        await delay.WaitUntilCapturedAsync();
        Assert.Equal(TimeSpan.FromHours(1), delay.CapturedDelays[0]);
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task Scheduler_after_0200_waits_until_the_next_day()
    {
        using var directory = new TemporaryDirectory();
        var localNow = CreateLocalNow(2, 0, 1);
        var delay = new ManualAsyncDelay();
        using var coordinator = CreateCoordinator(
            directory,
            new RecordingBackupService(Array.Empty<SqliteBackupCandidate>()),
            localNow,
            delay);

        await coordinator.StartAsync(CancellationToken.None);

        await delay.WaitUntilCapturedAsync();
        Assert.Equal(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59), delay.CapturedDelays[0]);
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task Scheduler_does_not_require_caller_synchronization_context_for_backup_execution()
    {
        using var directory = new TemporaryDirectory();
        var localNow = CreateLocalNow(1, 0, 0);
        var delay = new ManualAsyncDelay();
        var backup = new RecordingBackupService(Array.Empty<SqliteBackupCandidate>());
        using var coordinator = CreateCoordinator(directory, backup, localNow, delay);
        var callerContext = new RecordingSynchronizationContext();
        var previousContext = SynchronizationContext.Current;

        SynchronizationContext.SetSynchronizationContext(callerContext);
        try
        {
            await coordinator.StartAsync(CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        await delay.WaitUntilCapturedAsync();
        delay.ReleaseNext();
        await backup.WaitUntilCreateCapturedAsync();

        Assert.NotSame(callerContext, backup.LastSynchronizationContext);
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task StopAsync_waits_for_in_flight_backup_before_returning()
    {
        using var directory = new TemporaryDirectory();
        var localNow = CreateLocalNow(1, 0, 0);
        var delay = new ManualAsyncDelay();
        var backup = new BlockingBackupService();
        using var coordinator = CreateCoordinator(directory, backup, localNow, delay);

        await coordinator.StartAsync(CancellationToken.None);
        await delay.WaitUntilCapturedAsync();
        delay.ReleaseNext();
        await backup.WaitUntilFirstCreateAsync();

        var stopTask = coordinator.StopAsync();
        Assert.False(stopTask.IsCompleted);

        backup.ReleaseCurrent();
        await stopTask;
    }

    [Fact]
    public async Task StopAsync_is_safe_when_scheduler_is_waiting()
    {
        using var directory = new TemporaryDirectory();
        var localNow = CreateLocalNow(1, 0, 0);
        var delay = new ManualAsyncDelay();
        using var coordinator = CreateCoordinator(
            directory,
            new RecordingBackupService(Array.Empty<SqliteBackupCandidate>()),
            localNow,
            delay);

        await coordinator.StartAsync(CancellationToken.None);
        await delay.WaitUntilCapturedAsync();

        await coordinator.StopAsync();
    }

    [Fact]
    public async Task Startup_and_scheduled_backup_share_one_serial_gate()
    {
        using var directory = new TemporaryDirectory();
        var localNow = CreateLocalNow(1, 0, 0);
        var delay = new ManualAsyncDelay();
        var backup = new BlockingBackupService();
        using var coordinator = CreateCoordinator(directory, backup, localNow, delay);

        await coordinator.StartAsync(CancellationToken.None);
        await delay.WaitUntilCapturedAsync();
        delay.ReleaseNext();
        await backup.WaitUntilFirstCreateAsync();

        var startupTask = coordinator.RunStartupCatchUpAsync(CancellationToken.None);
        Assert.False(startupTask.IsCompleted);

        backup.ReleaseCurrent();
        await backup.WaitUntilSecondCreateAsync();
        Assert.Equal(1, backup.MaxConcurrency);

        backup.ReleaseCurrent();
        await startupTask;
        await coordinator.StopAsync();
    }

    private static DatabaseMaintenanceCoordinator CreateCoordinator(
        TemporaryDirectory directory,
        ISqliteBackupService backup,
        DateTimeOffset localNow,
        IAsyncDelay? delay = null)
    {
        var time = new FixedTimeProvider(localNow);
        return new DatabaseMaintenanceCoordinator(
            Path.Combine(directory.Path, "MineRailMonitor.db"),
            directory.BackupRoot,
            backup,
            new SqliteRetentionService(14, new TestLogger()),
            time,
            delay ?? new ManualAsyncDelay(),
            new TestLogger());
    }

    private static DateTimeOffset CreateLocalNow(int hour, int minute, int second)
    {
        var date = new DateTime(2026, 9, 20, hour, minute, second, DateTimeKind.Unspecified);
        return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
    }

    private sealed class FixedTimeProvider : IRfidTimeProvider
    {
        public FixedTimeProvider(DateTimeOffset localNow) => UtcNow = localNow.ToUniversalTime();

        public DateTimeOffset UtcNow { get; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ManualAsyncDelay : IAsyncDelay
    {
        private readonly object _syncRoot = new();
        private readonly Queue<TaskCompletionSource<object?>> _pending = new();
        private readonly List<TimeSpan> _capturedDelays = new();
        private readonly Queue<TaskCompletionSource<TimeSpan>> _waiters = new();

        public IReadOnlyList<TimeSpan> CapturedDelays
        {
            get
            {
                lock (_syncRoot)
                {
                    return _capturedDelays.ToArray();
                }
            }
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_syncRoot)
            {
                _capturedDelays.Add(delay);
                _pending.Enqueue(completion);
                if (_waiters.Count > 0)
                {
                    _waiters.Dequeue().TrySetResult(delay);
                }
            }

            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return completion.Task;
        }

        public Task<TimeSpan> WaitUntilCapturedAsync()
        {
            lock (_syncRoot)
            {
                if (_capturedDelays.Count > 0)
                {
                    return Task.FromResult(_capturedDelays[_capturedDelays.Count - 1]);
                }

                var waiter = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Enqueue(waiter);
                return waiter.Task;
            }
        }

        public void ReleaseNext()
        {
            TaskCompletionSource<object?> completion;
            lock (_syncRoot)
            {
                completion = _pending.Dequeue();
            }

            completion.TrySetResult(null);
        }
    }

    private sealed class RecordingBackupService : ISqliteBackupService
    {
        private readonly IReadOnlyList<SqliteBackupCandidate> _candidates;
        private readonly SqliteBackupResult _result;
        private readonly object _syncRoot = new();
        private readonly TaskCompletionSource<object?> _createCaptured =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingBackupService(
            IReadOnlyList<SqliteBackupCandidate> candidates,
            SqliteBackupResult? result = null)
        {
            _candidates = candidates;
            _result = result ?? new SqliteBackupResult(true, "created.db", null);
        }

        public int CreateCalls { get; private set; }
        public SynchronizationContext? LastSynchronizationContext { get; private set; }

        public Task<SqliteBackupResult> CreateValidatedBackupAsync(
            string productionDatabasePath,
            string backupRootDirectory,
            DateTimeOffset localNow,
            CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                CreateCalls++;
                LastSynchronizationContext = SynchronizationContext.Current;
            }

            _createCaptured.TrySetResult(null);
            return Task.FromResult(_result);
        }

        public Task WaitUntilCreateCapturedAsync() => _createCaptured.Task;

        public Task WaitUntilScanCapturedAsync() => _scanCaptured.Task;

        private readonly TaskCompletionSource<object?> _scanCaptured =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<SqliteBackupCandidate> ScanCandidates(string backupRootDirectory)
        {
            _scanCaptured.TrySetResult(null);
            return _candidates;
        }
    }

    private sealed class BlockingBackupService : ISqliteBackupService
    {
        private readonly object _syncRoot = new();
        private readonly TaskCompletionSource<object?> _firstStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> _secondStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<object?> _currentRelease =
            NewCompletionSource();
        private int _active;
        private int _calls;
        private int _maxConcurrency;

        public int MaxConcurrency
        {
            get
            {
                lock (_syncRoot)
                {
                    return _maxConcurrency;
                }
            }
        }

        public IReadOnlyList<SqliteBackupCandidate> ScanCandidates(string backupRootDirectory) =>
            Array.Empty<SqliteBackupCandidate>();

        public async Task<SqliteBackupResult> CreateValidatedBackupAsync(
            string productionDatabasePath,
            string backupRootDirectory,
            DateTimeOffset localNow,
            CancellationToken cancellationToken)
        {
            Task releaseTask;
            lock (_syncRoot)
            {
                _active++;
                _maxConcurrency = Math.Max(_maxConcurrency, _active);
                _calls++;
                releaseTask = _currentRelease.Task;
                if (_calls == 1)
                {
                    _firstStarted.TrySetResult(null);
                }
                else if (_calls == 2)
                {
                    _secondStarted.TrySetResult(null);
                }
            }

            try
            {
                await releaseTask.ConfigureAwait(false);
                return new SqliteBackupResult(true, "created.db", null);
            }
            finally
            {
                lock (_syncRoot)
                {
                    _active--;
                }
            }
        }

        public Task WaitUntilFirstCreateAsync() => _firstStarted.Task;
        public Task WaitUntilSecondCreateAsync() => _secondStarted.Task;

        public void ReleaseCurrent()
        {
            lock (_syncRoot)
            {
                _currentRelease.TrySetResult(null);
                _currentRelease = NewCompletionSource();
            }
        }

        private static TaskCompletionSource<object?> NewCompletionSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
    }

    private sealed class TestLogger : ILogger
    {
        public void Information(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MineRailMonitor",
                Guid.NewGuid().ToString("N"));
            BackupRoot = System.IO.Path.Combine(Path, "Backups", "SQLite");
            Directory.CreateDirectory(BackupRoot);
        }

        public string Path { get; }
        public string BackupRoot { get; }

        public string CreateFormal(DateTime date, string time) =>
            CreateFile($"MineRailMonitor_{date:yyyyMMdd}_{time}.db", date);

        public string CreateTmp(DateTime date, string time) =>
            CreateFile($"MineRailMonitor_{date:yyyyMMdd}_{time}.tmp.db", date);

        private string CreateFile(string fileName, DateTime date)
        {
            var directory = System.IO.Path.Combine(BackupRoot, date.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, fileName);
            File.WriteAllText(path, "test");
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
