using MineRailMonitor.Core.ExternalAlarm;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class ExternalAlarmPhase3AcceptanceTests
{
    [Fact]
    public async Task Normal_flow_sends_00_02_01_00_with_counts_and_valid_crc()
    {
        var cache = new ExternalAlarmStatusCache();
        var sender = new RecordingSender();
        using var scheduler = CreateScheduler(cache, sender, normalRetention: TimeSpan.FromMilliseconds(100));

        await scheduler.StartAsync();
        try
        {
            await sender.WaitForPayloadAsync(IsStatus(ExternalAlarmStatus.NoVehicle));
            cache.Update("RFID-560-01", Snapshot(ExternalAlarmStatus.Recognizing, 5));
            await sender.WaitForPayloadAsync(IsStatus(ExternalAlarmStatus.Recognizing));
            cache.Update("RFID-560-01", Snapshot(ExternalAlarmStatus.Normal, 11));
            await sender.WaitForPayloadAsync(IsStatus(ExternalAlarmStatus.Normal));
            var afterNormal = sender.Count;
            var noVehicle = await sender.WaitForPayloadAsync(
                IsStatus(ExternalAlarmStatus.NoVehicle),
                TimeSpan.FromSeconds(2),
                afterNormal + 1);

            Assert.Equal(
                new[]
                {
                    (byte)ExternalAlarmStatus.NoVehicle,
                    (byte)ExternalAlarmStatus.Recognizing,
                    (byte)ExternalAlarmStatus.Normal,
                    (byte)ExternalAlarmStatus.NoVehicle
                },
                sender.GetStatusTransitions());
            Assert.Equal((byte)5, sender.LastPayloadFor(ExternalAlarmStatus.Recognizing)[7]);
            Assert.Equal((byte)11, sender.LastPayloadFor(ExternalAlarmStatus.Normal)[7]);
            Assert.Equal((byte)0, noVehicle[7]);
            Assert.All(sender.SnapshotPayloads(), AssertValidFrame);
        }
        finally
        {
            await scheduler.StopAsync();
        }
    }

    [Fact]
    public async Task Alarm_flow_sends_00_02_03_01_00_with_counts_and_valid_crc()
    {
        var cache = new ExternalAlarmStatusCache();
        var sender = new RecordingSender();
        using var scheduler = CreateScheduler(cache, sender, normalRetention: TimeSpan.FromMilliseconds(100));

        await scheduler.StartAsync();
        try
        {
            await sender.WaitForPayloadAsync(IsStatus(ExternalAlarmStatus.NoVehicle));
            cache.Update("RFID-560-01", Snapshot(ExternalAlarmStatus.Recognizing, 5));
            await sender.WaitForPayloadAsync(IsStatus(ExternalAlarmStatus.Recognizing));
            cache.Update("RFID-560-01", Snapshot(ExternalAlarmStatus.UncouplingAlarm, 5));
            await sender.WaitForPayloadAsync(IsStatus(ExternalAlarmStatus.UncouplingAlarm));
            cache.Update("RFID-560-01", Snapshot(ExternalAlarmStatus.Normal, 5));
            await sender.WaitForPayloadAsync(IsStatus(ExternalAlarmStatus.Normal));
            var afterNormal = sender.Count;
            await sender.WaitForPayloadAsync(
                IsStatus(ExternalAlarmStatus.NoVehicle),
                TimeSpan.FromSeconds(2),
                afterNormal + 1);

            Assert.Equal(
                new[]
                {
                    (byte)ExternalAlarmStatus.NoVehicle,
                    (byte)ExternalAlarmStatus.Recognizing,
                    (byte)ExternalAlarmStatus.UncouplingAlarm,
                    (byte)ExternalAlarmStatus.Normal,
                    (byte)ExternalAlarmStatus.NoVehicle
                },
                sender.GetStatusTransitions());
            Assert.All(sender.SnapshotPayloads(), AssertValidFrame);
        }
        finally
        {
            await scheduler.StopAsync();
        }
    }

    [Fact]
    public async Task Default_scheduler_interval_is_200ms_without_overlapping_station_sends()
    {
        var cache = new ExternalAlarmStatusCache();
        var sender = new RecordingSender();
        using var scheduler = new ExternalAlarmStatusScheduler(
            cache,
            new ExternalAlarmFrameCodec(),
            sender,
            new Dictionary<string, byte> { ["RFID-560-01"] = 0x01 });

        await scheduler.StartAsync();
        try
        {
            var timestamps = await sender.WaitForAtLeastTimestampsAsync(3, TimeSpan.FromSeconds(2));
            Assert.All(
                timestamps.Zip(timestamps.Skip(1), (first, second) => second - first),
                interval => Assert.InRange(interval, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(500)));
        }
        finally
        {
            await scheduler.StopAsync();
        }
    }

    [Fact]
    public void Duplicate_station_address_is_reported_within_one_yard_but_not_across_yards()
    {
        var sameYardStations = new[]
        {
            CreateStation("RFID-560-01", "560", 0x01, 63201),
            CreateStation("RFID-560-02", "560", 0x01, 63202)
        };
        using var sameYardManager = CreateManager(sameYardStations);

        Assert.Contains(
            sameYardManager.Diagnostics,
            diagnostic => diagnostic.Contains("重复") && diagnostic.Contains("560") && diagnostic.Contains("0x01"));

        var separateYardStations = new[]
        {
            CreateStation("RFID-560-01", "560", 0x01, 63203),
            CreateStation("RFID-620-01", "620", 0x01, 63204)
        };
        using var separateYardManager = CreateManager(separateYardStations);

        Assert.DoesNotContain(
            separateYardManager.Diagnostics,
            diagnostic => diagnostic.Contains("RFID-560-01") && diagnostic.Contains("RFID-620-01"));
    }

    [Fact]
    public void Status_change_tracker_accepts_only_status_transitions_per_yard_and_station()
    {
        var tracker = new ExternalAlarmStatusChangeTracker();

        Assert.True(tracker.TryRecord("560:01", ExternalAlarmStatus.NoVehicle, out var first));
        Assert.Null(first);
        Assert.False(tracker.TryRecord("560:01", ExternalAlarmStatus.NoVehicle, out _));
        Assert.True(tracker.TryRecord("560:01", ExternalAlarmStatus.Recognizing, out var previous));
        Assert.Equal(ExternalAlarmStatus.NoVehicle, previous);
        Assert.True(tracker.TryRecord("620:01", ExternalAlarmStatus.NoVehicle, out _));
    }

    private static ExternalAlarmStatusScheduler CreateScheduler(
        ExternalAlarmStatusCache cache,
        RecordingSender sender,
        TimeSpan normalRetention) =>
        new(
            cache,
            new ExternalAlarmFrameCodec(),
            sender,
            new Dictionary<string, byte> { ["RFID-560-01"] = 0x01 },
            interval: TimeSpan.FromMilliseconds(20),
            normalRetention: normalRetention);

    private static ExternalAlarmStationSnapshot Snapshot(
        ExternalAlarmStatus status,
        int vehicleCount) =>
        new(0x01, status, vehicleCount);

    private static Func<byte[], bool> IsStatus(ExternalAlarmStatus status) =>
        payload => payload[4] == (byte)status;

    private static void AssertValidFrame(byte[] payload)
    {
        Assert.Equal(12, payload.Length);
        Assert.Equal((byte)0xB0, payload[0]);
        Assert.Equal((byte)0xB0, payload[1]);
        Assert.Equal((byte)0x04, payload[3]);
        Assert.Equal((byte)0xAA, payload[10]);
        Assert.Equal((byte)0xAA, payload[11]);

        var expected = new ExternalAlarmModbusCrcCalculator().Compute(payload, 0, 8);
        Assert.Equal((byte)(expected & 0xFF), payload[8]);
        Assert.Equal((byte)(expected >> 8), payload[9]);
    }

    private static YardCommunicationManager CreateManager(IReadOnlyList<RfidStationConfig> stations) =>
        new(
            new[]
            {
                new YardCommunicationConfig
                {
                    YardId = "560",
                    ListenIp = "127.0.0.1",
                    ListenPort = 63210,
                    Enabled = true
                },
                new YardCommunicationConfig
                {
                    YardId = "620",
                    ListenIp = "127.0.0.1",
                    ListenPort = 63211,
                    Enabled = true
                }
            },
            stations,
            new RfidSettings(),
            new InMemoryPassageRecordStore());

    private static RfidStationConfig CreateStation(
        string stationId,
        string yardId,
        byte address,
        int port) =>
        new()
        {
            StationId = stationId,
            YardId = yardId,
            IpAddress = "127.0.0.1",
            Port = port,
            ProtocolAddress = address,
            Enabled = true
        };

    private sealed class RecordingSender : IExternalAlarmStatusSender
    {
        private readonly object _syncRoot = new();
        private readonly List<(byte[] Payload, DateTimeOffset SentAt)> _records = new();

        public Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                _records.Add(((byte[])payload.Clone(), DateTimeOffset.UtcNow));
            }

            return Task.CompletedTask;
        }

        public async Task<byte[]> WaitForPayloadAsync(
            Func<byte[], bool> predicate,
            TimeSpan timeout = default,
            int minimumCount = 0)
        {
            if (timeout == default)
            {
                timeout = TimeSpan.FromSeconds(2);
            }

            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                var payloads = SnapshotPayloads();
                var candidates = payloads.Skip(Math.Max(0, minimumCount - 1));
                var payload = candidates.LastOrDefault(predicate);
                if (payload is not null && payloads.Count >= minimumCount)
                {
                    return payload;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("等待 External Alarm 状态帧超时。");
                }

                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        public async Task<IReadOnlyList<DateTimeOffset>> WaitForAtLeastTimestampsAsync(
            int count,
            TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                var timestamps = SnapshotRecords().Select(record => record.SentAt).ToArray();
                if (timestamps.Length >= count)
                {
                    return timestamps.Take(count).ToArray();
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("等待 External Alarm 200ms 发送周期超时。");
                }

                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        public byte[] LastPayloadFor(ExternalAlarmStatus status) =>
            SnapshotPayloads().Last(IsStatus(status));

        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _records.Count;
                }
            }
        }

        public IReadOnlyList<byte[]> SnapshotPayloads()
        {
            lock (_syncRoot)
            {
                return _records.Select(record => (byte[])record.Payload.Clone()).ToArray();
            }
        }

        public IReadOnlyList<byte> GetStatusTransitions()
        {
            var transitions = new List<byte>();
            foreach (var status in SnapshotPayloads().Select(payload => payload[4]))
            {
                if (transitions.Count == 0 || transitions[transitions.Count - 1] != status)
                {
                    transitions.Add(status);
                }
            }

            return transitions;
        }

        private IReadOnlyList<(byte[] Payload, DateTimeOffset SentAt)> SnapshotRecords()
        {
            lock (_syncRoot)
            {
                return _records
                    .Select(record => ((byte[])record.Payload.Clone(), record.SentAt))
                    .ToArray();
            }
        }

    }
}
