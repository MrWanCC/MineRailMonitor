using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Core.ExternalAlarm;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class ExternalAlarmClearTests
{
    [Fact]
    public void Valid_clear_frame_is_parsed_with_station_address_command_and_raw_frame()
    {
        var codec = new ExternalAlarmFrameCodec();
        var frame = codec.EncodeClear(0x01);

        var parsed = codec.TryDecodeClear(frame, out var command);

        Assert.True(parsed);
        Assert.Equal((byte)0x01, command.StationAddress);
        Assert.Equal((byte)0x01, command.Command);
        Assert.Equal(frame, command.RawFrame);
    }

    [Fact]
    public void Invalid_crc_clear_frame_is_rejected()
    {
        var codec = new ExternalAlarmFrameCodec();
        var frame = codec.EncodeClear(0x01);
        frame[7] = 0x7F;

        Assert.False(codec.TryDecodeClear(frame, out _));
    }

    [Fact]
    public void Invalid_header_clear_frame_is_rejected()
    {
        var codec = new ExternalAlarmFrameCodec();
        var frame = codec.EncodeClear(0x01);
        frame[0] = 0xB1;

        Assert.False(codec.TryDecodeClear(frame, out _));
    }

    [Fact]
    public async Task Receiver_delivers_only_valid_clear_frames_on_its_independent_socket()
    {
        var codec = new ExternalAlarmFrameCodec();
        var port = GetUnusedPort();
        using var receiver = new ExternalAlarmUdpReceiver(
            new IPEndPoint(IPAddress.Loopback, port),
            codec);
        var received = new TaskCompletionSource<ExternalAlarmClearCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ClearReceived += (command, _) => received.TrySetResult(command);

        await receiver.StartAsync();
        try
        {
            using var sender = new UdpClient();
            await sender.SendAsync(codec.EncodeClear(0x01), 12, new IPEndPoint(IPAddress.Loopback, port));

            var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.Same(received.Task, completed);
            Assert.Equal((byte)0x01, (await received.Task).StationAddress);
        }
        finally
        {
            await receiver.StopAsync();
        }
    }

    [Fact]
    public async Task Same_station_address_isolated_by_yard_and_clear_enters_existing_recovery_flow()
    {
        var station560 = CreateStation("RFID-560-01", "560", 0x01, 63101);
        var station620 = CreateStation("RFID-620-01", "620", 0x01, 63102);
        var record560 = CreateAlarmRecord(station560, DateTimeOffset.UtcNow.AddMinutes(-2));
        var record620 = CreateAlarmRecord(station620, DateTimeOffset.UtcNow.AddMinutes(-1));
        var store = new InMemoryPassageRecordStore();
        store.Save(record560);
        store.Save(record620);

        var configuration560 = CreateCommunication("560");
        var configuration620 = CreateCommunication("620");
        using var manager = new YardCommunicationManager(
            new[] { configuration560, configuration620 },
            new[] { station560, station620 },
            CreateSettings(),
            store);

        foreach (var context in manager.Contexts.Values)
        {
            context.RestorePendingClear(store.GetPendingClear());
            context.RestoreUnacknowledgedAlarms(store.GetUnacknowledgedAlarms());
        }

        await manager.StartAllAsync();
        try
        {
            using var sender = new UdpClient();
            var clear = new ExternalAlarmFrameCodec().EncodeClear(0x01);
            await sender.SendAsync(clear, clear.Length, new IPEndPoint(IPAddress.Loopback, configuration560.ExternalAlarmListenPort));

            var context560 = manager.GetContext("560")!;
            var context620 = manager.GetContext("620")!;
            await WaitForAsync(() => !context560.HasUnacknowledgedAlarm(record560.PassageId));

            Assert.False(context560.HasUnacknowledgedAlarm(record560.PassageId));
            Assert.True(context620.HasUnacknowledgedAlarm(record620.PassageId));
            Assert.Equal(PassageLifecycleState.Clearing, context560.RuntimeStates.Single().LifecycleState);

            var state560 = context560.RuntimeStates.Single();
            context560.ProcessFrame(CreateEmptyFrame(station560, DateTimeOffset.UtcNow));
            Assert.Equal(PassageLifecycleState.WaitForEmpty, state560.LifecycleState);
            context560.ProcessFrame(CreateEmptyFrame(station560, DateTimeOffset.UtcNow.AddMilliseconds(200)));
            Assert.Equal(PassageLifecycleState.Idle, state560.LifecycleState);
            Assert.Equal(PassageLifecycleState.Clearing, context620.RuntimeStates.Single().LifecycleState);
        }
        finally
        {
            await manager.StopAllAsync();
        }
    }

    [Fact]
    public async Task Unknown_station_address_does_not_acknowledge_other_stations()
    {
        var station01 = CreateStation("RFID-560-01", "560", 0x01, 63111);
        var station02 = CreateStation("RFID-560-02", "560", 0x02, 63112);
        var record01 = CreateAlarmRecord(station01, DateTimeOffset.UtcNow.AddMinutes(-2));
        var record02 = CreateAlarmRecord(station02, DateTimeOffset.UtcNow.AddMinutes(-1));
        var store = new InMemoryPassageRecordStore();
        store.Save(record01);
        store.Save(record02);
        var configuration = CreateCommunication("560");
        using var manager = new YardCommunicationManager(
            new[] { configuration },
            new[] { station01, station02 },
            CreateSettings(),
            store);
        var context = manager.GetContext("560")!;
        context.RestorePendingClear(store.GetPendingClear());
        context.RestoreUnacknowledgedAlarms(store.GetUnacknowledgedAlarms());

        await manager.StartAllAsync();
        try
        {
            using var sender = new UdpClient();
            var clear = new ExternalAlarmFrameCodec().EncodeClear(0x7F);
            await sender.SendAsync(clear, clear.Length, new IPEndPoint(IPAddress.Loopback, configuration.ExternalAlarmListenPort));
            await Task.Delay(100);

            Assert.True(context.HasUnacknowledgedAlarm(record01.PassageId));
            Assert.True(context.HasUnacknowledgedAlarm(record02.PassageId));
        }
        finally
        {
            await manager.StopAllAsync();
        }
    }

    [Theory]
    [InlineData(PassageLifecycleState.Clearing)]
    [InlineData(PassageLifecycleState.WaitForEmpty)]
    public void Clearing_states_do_not_map_directly_to_external_normal(PassageLifecycleState state)
    {
        Assert.Equal(
            ExternalAlarmStatus.UncouplingAlarm,
            ExternalAlarmStatusMapper.FromLifecycleState(state));
    }

    private static PassageRecord CreateAlarmRecord(RfidStationConfig station, DateTimeOffset completedAt) =>
        new(
            Guid.NewGuid(),
            station.StationId,
            station.ProtocolAddress,
            0x0001,
            new ushort[] { 0x0001, 0x0011 },
            11,
            PassageOutcome.UncouplingAlarm,
            completedAt.AddSeconds(-1),
            completedAt,
            alarmMessage: "脱节报警");

    private static YardCommunicationConfig CreateCommunication(string yardId) => new()
    {
        YardId = yardId,
        ListenIp = IPAddress.Loopback.ToString(),
        ListenPort = GetUnusedPort(),
        ExternalAlarmListenPort = GetUnusedPort(),
        Enabled = true
    };

    private static RfidStationConfig CreateStation(string stationId, string yardId, byte address, int port) => new()
    {
        StationId = stationId,
        Name = stationId,
        YardId = yardId,
        IpAddress = IPAddress.Loopback.ToString(),
        Port = port,
        ProtocolAddress = address,
        Enabled = true
    };

    private static RfidSettings CreateSettings() => new()
    {
        PollIntervalMs = 1000,
        ExpectedVehicleCount = 11,
        InterVehicleTimeoutSeconds = 30
    };

    private static RfidStationFrame CreateEmptyFrame(RfidStationConfig station, DateTimeOffset receivedAt) => new()
    {
        StationAddress = station.ProtocolAddress,
        SourceEndpoint = new IPEndPoint(IPAddress.Loopback, station.Port),
        ReceivedAt = receivedAt,
        RawRfidSlots = new ushort[14],
        ValidRfids = Array.Empty<ushort>(),
        ActualNonZeroSlotCount = 0,
        ReportedCardCount = 0
    };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("等待外部报警清除命令处理超时。");
            }

            await Task.Delay(10);
        }
    }

    private static int GetUnusedPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }
}
