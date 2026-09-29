using MineRailMonitor.Core.ExternalAlarm;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class ExternalAlarmStatusSchedulerTests
{
    [Fact]
    public async Task Sends_no_vehicle_snapshot_for_each_station_every_200ms_without_rfid()
    {
        var cache = new ExternalAlarmStatusCache();
        var sender = new RecordingSender();
        using var scheduler = new ExternalAlarmStatusScheduler(
            cache,
            new ExternalAlarmFrameCodec(),
            sender,
            new Dictionary<string, byte>
            {
                ["RFID-560-01"] = 0x01,
                ["RFID-560-02"] = 0x02
            });

        await scheduler.StartAsync();
        try
        {
            var payloads = await sender.WaitForAtLeastAsync(6, TimeSpan.FromSeconds(2));

            Assert.True(payloads.Count(payload => payload[2] == 0x01) >= 3);
            Assert.True(payloads.Count(payload => payload[2] == 0x02) >= 3);
            Assert.All(payloads, payload => Assert.Equal((byte)ExternalAlarmStatus.NoVehicle, payload[4]));
        }
        finally
        {
            await scheduler.StopAsync();
        }
    }

    [Fact]
    public async Task Separate_yard_schedulers_keep_same_station_address_isolated()
    {
        var cache560 = new ExternalAlarmStatusCache();
        var cache620 = new ExternalAlarmStatusCache();
        cache560.Update(
            "RFID-560-01",
            new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.Recognizing, 5));
        cache620.Update(
            "RFID-620-01",
            new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.NoVehicle, 0));
        var sender560 = new RecordingSender();
        var sender620 = new RecordingSender();
        using var scheduler560 = CreateScheduler(cache560, sender560, "RFID-560-01");
        using var scheduler620 = CreateScheduler(cache620, sender620, "RFID-620-01");

        await scheduler560.StartAsync();
        await scheduler620.StartAsync();
        try
        {
            var payload560 = await sender560.WaitForPayloadAsync(
                payload => payload[4] == (byte)ExternalAlarmStatus.Recognizing);
            var payload620 = await sender620.WaitForPayloadAsync(
                payload => payload[4] == (byte)ExternalAlarmStatus.NoVehicle);

            Assert.Equal((byte)0x01, payload560[2]);
            Assert.Equal((byte)0x01, payload620[2]);
            Assert.Equal((byte)5, payload560[7]);
            Assert.Equal((byte)0, payload620[7]);
        }
        finally
        {
            await scheduler560.StopAsync();
            await scheduler620.StopAsync();
        }
    }

    [Fact]
    public async Task Scheduler_sends_new_snapshot_after_rfid_updates_cache()
    {
        var cache = new ExternalAlarmStatusCache();
        var sender = new RecordingSender();
        using var scheduler = CreateScheduler(cache, sender, "RFID-560-01");

        await scheduler.StartAsync();
        try
        {
            cache.Update(
                "RFID-560-01",
                new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.UncouplingAlarm, 5));

            var payload = await sender.WaitForPayloadAsync(
                payload => payload[4] == (byte)ExternalAlarmStatus.UncouplingAlarm);

            Assert.Equal((byte)5, payload[7]);
        }
        finally
        {
            await scheduler.StopAsync();
        }
    }

    [Fact]
    public void Normal_snapshot_ignores_empty_updates_during_retention()
    {
        var cache = new ExternalAlarmStatusCache();
        var completedAt = DateTimeOffset.UtcNow;
        cache.Update(
            "RFID-560-01",
            new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.Normal, 11),
            completedAt);

        cache.Update(
            "RFID-560-01",
            new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.NoVehicle, 0),
            completedAt.AddSeconds(30));

        Assert.True(cache.TryGet("RFID-560-01", out var retained));
        Assert.Equal(ExternalAlarmStatus.Normal, retained.Status);

        cache.Update(
            "RFID-560-01",
            new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.NoVehicle, 0),
            completedAt.AddMinutes(2));

        Assert.True(cache.TryGet("RFID-560-01", out var expired));
        Assert.Equal(ExternalAlarmStatus.NoVehicle, expired.Status);
    }

    [Fact]
    public async Task Normal_status_expires_to_no_vehicle_after_two_minutes_of_retention()
    {
        var cache = new ExternalAlarmStatusCache();
        cache.Update(
            "RFID-560-01",
            new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.Normal, 11));
        var sender = new RecordingSender();
        using var scheduler = new ExternalAlarmStatusScheduler(
            cache,
            new ExternalAlarmFrameCodec(),
            sender,
            new Dictionary<string, byte> { ["RFID-560-01"] = 0x01 },
            normalRetention: TimeSpan.FromMilliseconds(100));

        await scheduler.StartAsync();
        try
        {
            var payload = await sender.WaitForPayloadAsync(
                payload => payload[4] == (byte)ExternalAlarmStatus.NoVehicle,
                TimeSpan.FromSeconds(2));

            Assert.Equal((byte)0x00, payload[4]);
            Assert.Equal((byte)0x00, payload[7]);
            Assert.True(cache.TryGet("RFID-560-01", out var snapshot));
            Assert.Equal(ExternalAlarmStatus.NoVehicle, snapshot.Status);
        }
        finally
        {
            await scheduler.StopAsync();
        }
    }

    [Fact]
    public async Task Alarm_status_is_repeated_and_is_not_expired_by_normal_retention()
    {
        var cache = new ExternalAlarmStatusCache();
        cache.Update(
            "RFID-560-01",
            new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.UncouplingAlarm, 5));
        var sender = new RecordingSender();
        using var scheduler = new ExternalAlarmStatusScheduler(
            cache,
            new ExternalAlarmFrameCodec(),
            sender,
            new Dictionary<string, byte> { ["RFID-560-01"] = 0x01 },
            normalRetention: TimeSpan.FromMilliseconds(100));

        await scheduler.StartAsync();
        try
        {
            var payloads = await sender.WaitForAtLeastAsync(3, TimeSpan.FromSeconds(2));

            Assert.All(payloads, payload =>
                Assert.Equal((byte)ExternalAlarmStatus.UncouplingAlarm, payload[4]));
        }
        finally
        {
            await scheduler.StopAsync();
        }
    }

    [Fact]
    public async Task Send_failure_is_reported_and_scheduler_continues_for_rfid_independence()
    {
        var cache = new ExternalAlarmStatusCache();
        var sender = new FailingSender();
        using var scheduler = CreateScheduler(cache, sender, "RFID-560-01");
        var failures = 0;
        scheduler.SendFailed += (_, _) => Interlocked.Increment(ref failures);

        await scheduler.StartAsync();
        try
        {
            await WaitForAsync(() => Volatile.Read(ref failures) >= 2, TimeSpan.FromSeconds(2));
            Assert.True(scheduler.IsRunning);
        }
        finally
        {
            await scheduler.StopAsync();
        }
    }

    private static ExternalAlarmStatusScheduler CreateScheduler(
        ExternalAlarmStatusCache cache,
        IExternalAlarmStatusSender sender,
        string stationId) =>
        new(
            cache,
            new ExternalAlarmFrameCodec(),
            sender,
            new Dictionary<string, byte> { [stationId] = 0x01 });

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("等待 External Alarm Scheduler 测试条件超时。");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private sealed class RecordingSender : IExternalAlarmStatusSender
    {
        private readonly object _syncRoot = new();
        private readonly List<byte[]> _payloads = new();

        public Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                _payloads.Add((byte[])payload.Clone());
            }

            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<byte[]>> WaitForAtLeastAsync(int count, TimeSpan timeout)
        {
            await WaitForAsync(() => Count >= count, timeout).ConfigureAwait(false);
            lock (_syncRoot)
            {
                return _payloads.Select(payload => (byte[])payload.Clone()).ToArray();
            }
        }

        public async Task<byte[]> WaitForPayloadAsync(
            Func<byte[], bool> predicate,
            TimeSpan timeout = default)
        {
            if (timeout == default)
            {
                timeout = TimeSpan.FromSeconds(2);
            }

            await WaitForAsync(() => Snapshot().Any(predicate), timeout).ConfigureAwait(false);
            return Snapshot().Last(predicate);
        }

        private int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _payloads.Count;
                }
            }
        }

        private IReadOnlyList<byte[]> Snapshot()
        {
            lock (_syncRoot)
            {
                return _payloads.Select(payload => (byte[])payload.Clone()).ToArray();
            }
        }
    }

    private sealed class FailingSender : IExternalAlarmStatusSender
    {
        public Task SendAsync(byte[] payload, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("external endpoint unavailable"));
    }
}
