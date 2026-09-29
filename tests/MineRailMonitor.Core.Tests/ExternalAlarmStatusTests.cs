using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Core.ExternalAlarm;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Simulator.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class ExternalAlarmStatusTests
{
    [Fact]
    public async Task Manager_creates_independent_status_coordinators_for_yards_and_stations()
    {
        var station560Address01 = CreateStation("RFID-560-01", "560", 0x01, 62101);
        var station560Address02 = CreateStation("RFID-560-02", "560", 0x02, 62102);
        var station620Address01 = CreateStation("RFID-620-01", "620", 0x01, 62103);
        var sender560 = new RecordingStatusSender();
        var sender620 = new RecordingStatusSender();
        var coordinator560 = CreateCoordinator(sender560);
        var coordinator620 = CreateCoordinator(sender620);
        using var manager = new YardCommunicationManager(
            new[] { CreateCommunication("560"), CreateCommunication("620") },
            new[] { station560Address01, station560Address02, station620Address01 },
            CreateSettings(),
            new InMemoryPassageRecordStore(),
            externalAlarmStatusCoordinators: new Dictionary<string, ExternalAlarmStatusCoordinator>(StringComparer.OrdinalIgnoreCase)
            {
                ["560"] = coordinator560,
                ["620"] = coordinator620
            });

        await coordinator560.StartAsync();
        await coordinator620.StartAsync();

        manager.GetContext("560")!.ProcessFrame(CreateFrame(station560Address01, 5));
        manager.GetContext("560")!.ProcessFrame(CreateFrame(station560Address02, 6));
        manager.GetContext("620")!.ProcessFrame(CreateFrame(station620Address01, 7));

        var payloads560 = await sender560.WaitForCountAsync(2);
        var payloads620 = await sender620.WaitForCountAsync(1);

        Assert.Equal(new byte[] { 0x01, 0x02 }, payloads560.Select(payload => payload[2]).ToArray());
        Assert.Equal(new byte[] { 0x01 }, payloads620.Select(payload => payload[2]).ToArray());
        Assert.True(sender560.SendCount >= 2);
        Assert.True(sender620.SendCount >= 1);
    }

    [Fact]
    public async Task Manager_status_send_failure_is_reported_without_changing_rfid_health()
    {
        var station = CreateStation("RFID-560-01", "560", 0x01, 62104);
        var sender = new FailingStatusSender();
        var coordinator = CreateCoordinator(sender);
        using var manager = new YardCommunicationManager(
            new[] { CreateCommunication("560") },
            new[] { station },
            CreateSettings(),
            new InMemoryPassageRecordStore(),
            externalAlarmStatusCoordinators: new Dictionary<string, ExternalAlarmStatusCoordinator>
            {
                ["560"] = coordinator
            });
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ExternalAlarmStatusSendFailed += (_, _, exception) => failure.TrySetResult(exception);

        var context = manager.GetContext("560")!;
        var beforeErrorCount = context.ErrorCount;
        context.ProcessFrame(CreateFrame(station, 5));
        await coordinator.StartAsync();

        var completed = await Task.WhenAny(failure.Task, Task.Delay(TimeSpan.FromSeconds(3)));

        Assert.Same(failure.Task, completed);
        Assert.Contains("external status unavailable", (await failure.Task).Message, StringComparison.Ordinal);
        Assert.Null(context.LastError);
        Assert.Equal(beforeErrorCount, context.ErrorCount);
        Assert.Single(context.RuntimeStates);
    }

    [Fact]
    public async Task Manager_rebinds_status_coordinator_when_external_target_changes()
    {
        var station = CreateStation("RFID-560-01", "560", 0x01, 62105);
        var sender1 = new RecordingStatusSender();
        var sender2 = new RecordingStatusSender();
        var coordinator1 = CreateCoordinator(sender1);
        var coordinator2 = CreateCoordinator(sender2);
        using var manager = new YardCommunicationManager(
            new[] { CreateCommunication("560") },
            new[] { station },
            CreateSettings(),
            new InMemoryPassageRecordStore(),
            externalAlarmStatusCoordinators: new Dictionary<string, ExternalAlarmStatusCoordinator>
            {
                ["560"] = coordinator1
            });

        var context = manager.GetContext("560")!;
        context.ProcessFrame(CreateFrame(station, 5));
        await coordinator1.StartAsync();
        await sender1.WaitForCountAsync(1);

        manager.ApplyExternalAlarmStatusCoordinators(
            new Dictionary<string, ExternalAlarmStatusCoordinator>
            {
                ["560"] = coordinator2
            });
        context.ProcessFrame(CreateFrame(station, 6));
        await coordinator2.StartAsync();

        await sender2.WaitForCountAsync(1);
        Assert.Equal(1, sender1.SendCount);
        Assert.Equal(1, sender2.SendCount);
    }

    [Fact]
    public void Modbus_crc_calculator_matches_confirmed_external_frame_bytes()
    {
        var calculator = new ExternalAlarmModbusCrcCalculator();
        var framePrefix = new byte[] { 0xB0, 0xB0, 0x01, 0x04, 0x02, 0x00, 0x00, 0x05 };

        var crc = calculator.Compute(framePrefix, 0, framePrefix.Length);

        Assert.Equal(0x1ECB, crc);
    }

    [Fact]
    public void Default_codec_writes_confirmed_modbus_crc_low_then_high()
    {
        var codec = new ExternalAlarmFrameCodec();
        var snapshot = new ExternalAlarmStationSnapshot(
            stationAddress: 0x01,
            status: ExternalAlarmStatus.Recognizing,
            accumulatedVehicleCount: 5);

        var payload = codec.Encode(snapshot);

        Assert.Equal(
            new byte[] { 0xB0, 0xB0, 0x01, 0x04, 0x02, 0x00, 0x00, 0x05, 0xCB, 0x1E, 0xAA, 0xAA },
            payload);
    }

    [Fact]
    public void No_vehicle_snapshot_encodes_status_00()
    {
        var codec = new ExternalAlarmFrameCodec(new FixedCrcCalculator(0x1234));
        var snapshot = new ExternalAlarmStationSnapshot(
            stationAddress: 0x01,
            status: ExternalAlarmStatus.NoVehicle,
            accumulatedVehicleCount: 0);

        var payload = codec.Encode(snapshot);

        Assert.Equal((byte)0x00, payload[4]);
        Assert.Equal((byte)0x00, payload[7]);
    }

    [Fact]
    public void Status_cache_keeps_the_latest_snapshot_per_station()
    {
        var cache = new ExternalAlarmStatusCache();

        cache.Update(
            "RFID-560-01",
            new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.NoVehicle, 0));
        cache.Update(
            "RFID-560-01",
            new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.Recognizing, 2));

        Assert.True(cache.TryGet("RFID-560-01", out var snapshot));
        Assert.Equal(ExternalAlarmStatus.Recognizing, snapshot.Status);
        Assert.Equal(2, snapshot.AccumulatedVehicleCount);
        Assert.Single(cache.GetAll());
    }

    [Fact]
    public void Codec_writes_the_confirmed_twelve_byte_layout()
    {
        var codec = new ExternalAlarmFrameCodec(new FixedCrcCalculator(0x1234));
        var snapshot = new ExternalAlarmStationSnapshot(
            stationAddress: 0x01,
            status: ExternalAlarmStatus.Recognizing,
            accumulatedVehicleCount: 5);

        var payload = codec.Encode(snapshot);

        Assert.Equal(
            new byte[] { 0xB0, 0xB0, 0x01, 0x04, 0x02, 0x00, 0x00, 0x05, 0x34, 0x12, 0xAA, 0xAA },
            payload);
    }

    [Theory]
    [InlineData(PassageLifecycleState.Idle, ExternalAlarmStatus.NoVehicle)]
    [InlineData(PassageLifecycleState.Recognizing, ExternalAlarmStatus.Recognizing)]
    [InlineData(PassageLifecycleState.Completed, ExternalAlarmStatus.Normal)]
    [InlineData(PassageLifecycleState.Alarm, ExternalAlarmStatus.UncouplingAlarm)]
    public void Runtime_lifecycle_maps_to_external_status(
        PassageLifecycleState lifecycleState,
        ExternalAlarmStatus expected)
    {
        Assert.Equal(expected, ExternalAlarmStatusMapper.FromLifecycleState(lifecycleState));
    }

    [Fact]
    public async Task External_sender_failure_does_not_change_rfid_context_health()
    {
        var sender = new FailingSender();
        var publisher = new ExternalAlarmStatusCoordinator(
            new ExternalAlarmFrameCodec(new FixedCrcCalculator(0x1234)),
            sender);
        var station = new RfidStationConfig
        {
            StationId = "RFID-01",
            Name = "RFID-01",
            IpAddress = "127.0.0.1",
            Port = 62001,
            ProtocolAddress = 0x01,
            Enabled = true
        };
        var configuration = new YardCommunicationConfig
        {
            YardId = "560",
            ListenIp = "127.0.0.1",
            ListenPort = 62000,
            Enabled = true
        };

        using var context = new YardCommunicationContext(
            configuration,
            new[] { station },
            new RfidSettings { PollIntervalMs = 200, ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            new InMemoryPassageRecordStore(),
            externalAlarmStatusCoordinator: publisher);
        var beforeErrorCount = context.ErrorCount;

        context.ProcessFrame(new RfidStationFrame
        {
            StationAddress = 0x01,
            Mode = 0x04,
            SourceEndpoint = new IPEndPoint(IPAddress.Loopback, 62001),
            ReceivedAt = DateTimeOffset.UtcNow,
            RawRfidSlots = new ushort[14]
        });
        await publisher.StartAsync();

        var completed = await Task.WhenAny(sender.Failure.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(sender.Failure.Task, completed);
        Assert.Null(context.LastError);
        Assert.Equal(beforeErrorCount, context.ErrorCount);
    }

    [Fact]
    public async Task Simulator_closed_loop_publishes_normal_recognizing_alarm_clear_and_normal()
    {
        var simulator = new SimulatorStationContext(new SimulatorStationConfig
        {
            StationName = "SIM-560",
            ListenIp = IPAddress.Loopback.ToString(),
            ListenPort = GetUnusedPort(),
            ProtocolAddress = 0x01,
            Enabled = true,
            EmptySlotValue = 0
        });
        var statusSender = new RecordingStatusSender();
        var configuration = new YardCommunicationConfig
        {
            YardId = "560",
            ListenIp = IPAddress.Loopback.ToString(),
            ListenPort = GetUnusedPort(),
            ExternalAlarmListenPort = GetUnusedPort(),
            Enabled = true
        };
        var station = CreateStation("RFID-560-01", "560", 0x01, simulator.Config.ListenPort);
        var coordinator = CreateCoordinator(statusSender);
        using var manager = new YardCommunicationManager(
            new[] { configuration },
            new[] { station },
            CreateSettings(pollIntervalMs: 20, interVehicleTimeoutSeconds: 1),
            new InMemoryPassageRecordStore(),
            externalAlarmStatusCoordinators: new Dictionary<string, ExternalAlarmStatusCoordinator>
            {
                ["560"] = coordinator
            });
        var parser = new RfidFrameParser(new RfidFrameParserOptions { EmptyRfidValue = 0 });
        manager.DatagramReceived += (context, args) =>
        {
            if (!args.IsValid ||
                !context.RecordResponse(args.RemoteEndPoint, args.Data[2], args.ReceivedAt) ||
                !parser.TryParse(args.Data, args.RemoteEndPoint, args.ReceivedAt, out var frame) ||
                frame is null)
            {
                return;
            }

            context.ProcessFrame(frame);
        };

        await simulator.StartAsync();
        await manager.StartAllAsync();
        try
        {
            var context = manager.GetContext("560")!;
            await statusSender.WaitForStatusAsync((byte)ExternalAlarmStatus.NoVehicle);

            simulator.SetSlots(CreateSlots(5));
            await statusSender.WaitForStatusAsync(
                (byte)ExternalAlarmStatus.Recognizing,
                payload => payload[7] == 5);

            simulator.Stop();
            manager.Evaluate(DateTimeOffset.UtcNow.AddSeconds(2));
            Assert.True(coordinator.SnapshotCache.TryGet("RFID-560-01", out var alarmSnapshot));
            Assert.Equal(ExternalAlarmStatus.UncouplingAlarm, alarmSnapshot.Status);
            await statusSender.WaitForStatusAsync((byte)ExternalAlarmStatus.UncouplingAlarm);

            var sendCountBeforeClear = statusSender.SendCount;
            using var clearSender = new UdpClient();
            var clearFrame = new ExternalAlarmFrameCodec().EncodeClear(0x01);
            Assert.Equal((byte)0x0B, clearFrame[8]);
            Assert.Equal((byte)0x59, clearFrame[9]);
            await clearSender.SendAsync(
                clearFrame,
                clearFrame.Length,
                new IPEndPoint(IPAddress.Loopback, configuration.ExternalAlarmListenPort));
            await WaitForAsync(() =>
                context.RuntimeStates.Single().LifecycleState is
                    PassageLifecycleState.Clearing or PassageLifecycleState.WaitForEmpty);

            simulator.ClearSlots();
            await simulator.StartAsync();
            await WaitForAsync(() => context.RuntimeStates.Single().LifecycleState == PassageLifecycleState.Idle);
            await statusSender.WaitForNewStatusAsync(
                (byte)ExternalAlarmStatus.Normal,
                sendCountBeforeClear);
        }
        finally
        {
            await manager.StopAllAsync();
            simulator.Dispose();
        }
    }

    private sealed class FixedCrcCalculator : IExternalAlarmCrcCalculator
    {
        private readonly ushort _value;

        public FixedCrcCalculator(ushort value) => _value = value;

        public ushort Compute(byte[] buffer, int offset, int length) => _value;
    }

    private sealed class FailingSender : IExternalAlarmStatusSender
    {
        public TaskCompletionSource<Exception> Failure { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            var exception = new InvalidOperationException("external endpoint unavailable");
            Failure.TrySetResult(exception);
            return Task.FromException(exception);
        }
    }

    private static ExternalAlarmStatusCoordinator CreateCoordinator(IExternalAlarmStatusSender sender) =>
        new(new ExternalAlarmFrameCodec(), sender);

    private static YardCommunicationConfig CreateCommunication(string yardId) => new()
    {
        YardId = yardId,
        ListenIp = IPAddress.Loopback.ToString(),
        ListenPort = yardId == "560" ? 62201 : 62202,
        Enabled = true
    };

    private static RfidStationConfig CreateStation(
        string stationId,
        string yardId,
        byte address,
        int port) => new()
    {
        StationId = stationId,
        Name = stationId,
        YardId = yardId,
        IpAddress = IPAddress.Loopback.ToString(),
        Port = port,
        ProtocolAddress = address,
        Enabled = true
    };

    private static RfidSettings CreateSettings(
        int pollIntervalMs = 200,
        int interVehicleTimeoutSeconds = 30) => new()
    {
        ExpectedVehicleCount = 11,
        InterVehicleTimeoutSeconds = interVehicleTimeoutSeconds,
        PollIntervalMs = pollIntervalMs
    };

    private static ushort[] CreateSlots(int count)
    {
        var slots = new ushort[14];
        for (var index = 0; index < count; index++)
        {
            slots[index] = (ushort)(index + 1);
        }

        return slots;
    }

    private static RfidStationFrame CreateFrame(RfidStationConfig station, byte count) => new()
    {
        StationAddress = station.ProtocolAddress,
        Mode = 0x04,
        SourceEndpoint = new IPEndPoint(IPAddress.Loopback, station.Port),
        ReceivedAt = DateTimeOffset.UtcNow,
        RawRfidSlots = new ushort[14],
        ReportedCardCount = count
    };

    private sealed class RecordingStatusSender : IExternalAlarmStatusSender
    {
        private readonly object _syncRoot = new();
        private readonly List<byte[]> _payloads = new();
        private readonly TaskCompletionSource<IReadOnlyList<byte[]>> _completed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SendCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _payloads.Count;
                }
            }
        }

        public Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                _payloads.Add((byte[])payload.Clone());
            }
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<byte[]>> WaitForCountAsync(int count)
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(3));
            while (SendCount < count)
            {
                if (await Task.WhenAny(Task.Delay(10), timeout).ConfigureAwait(false) == timeout)
                {
                    throw new TimeoutException($"Expected {count} external status sends, got {SendCount}.");
                }
            }

            lock (_syncRoot)
            {
                return _payloads.Select(payload => (byte[])payload.Clone()).ToArray();
            }
        }

        public Task<byte[]> WaitForStatusAsync(byte status, Func<byte[], bool>? predicate = null) =>
            WaitForStatusCoreAsync(status, predicate, minimumIndex: 0);

        public Task<byte[]> WaitForNewStatusAsync(byte status, int previousSendCount) =>
            WaitForStatusCoreAsync(status, predicate: null, previousSendCount);

        private async Task<byte[]> WaitForStatusCoreAsync(
            byte status,
            Func<byte[], bool>? predicate,
            int minimumIndex)
        {
            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < timeout)
            {
                lock (_syncRoot)
                {
                    var match = _payloads
                        .Skip(minimumIndex)
                        .LastOrDefault(payload =>
                            payload.Length > 7 &&
                            payload[4] == status &&
                            (predicate is null || predicate(payload)));
                    if (match is not null)
                    {
                        return (byte[])match.Clone();
                    }
                }

                await Task.Delay(10).ConfigureAwait(false);
            }

            throw new TimeoutException($"Expected external alarm status 0x{status:X2}; got {SendCount} sends.");
        }
    }

    private sealed class FailingStatusSender : IExternalAlarmStatusSender
    {
        public Task SendAsync(byte[] payload, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("external status unavailable"));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("等待 ExternalAlarm Simulator 闭环状态超时。");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private static int GetUnusedPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }
}
