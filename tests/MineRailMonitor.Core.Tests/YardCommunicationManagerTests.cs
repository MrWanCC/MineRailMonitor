using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class YardCommunicationManagerTests
{
    [Fact]
    public async Task Starts_two_yard_contexts_with_independent_listeners_and_station_sets()
    {
        var station560 = CreateStation("RFID-01", "560", 0x01, 63101);
        var station620 = CreateStation("RFID-02", "620", 0x02, 63102);
        using var manager = new YardCommunicationManager(
            new[] { CreateCommunication("560"), CreateCommunication("620") },
            new[] { station560, station620 },
            CreateSettings(),
            new InMemoryPassageRecordStore());

        await manager.StartAllAsync();

        try
        {
            var context560 = Assert.IsType<YardCommunicationContext>(manager.GetContext("560"));
            var context620 = Assert.IsType<YardCommunicationContext>(manager.GetContext("620"));
            Assert.True(context560.IsRunning);
            Assert.True(context620.IsRunning);
            Assert.Equal(new[] { "RFID-01" }, context560.Stations.Select(item => item.StationId));
            Assert.Equal(new[] { "RFID-02" }, context620.Stations.Select(item => item.StationId));
            Assert.NotEqual(context560.ListenerEndPoint!.Port, context620.ListenerEndPoint!.Port);
        }
        finally
        {
            await manager.StopAllAsync();
        }
    }

    [Fact]
    public void Rejects_duplicate_listener_endpoints_when_constructing_manager()
    {
        var first = CreateCommunication("560");
        var second = CreateCommunication("620", first.ListenPort);

        var exception = Assert.Throws<ArgumentException>(() => new YardCommunicationManager(
            new[] { first, second },
            Array.Empty<RfidStationConfig>(),
            CreateSettings(),
            new InMemoryPassageRecordStore()));

        Assert.Contains("监听端点重复", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stopping_and_restarting_one_yard_does_not_change_the_other_yard()
    {
        var station560 = CreateStation("RFID-01", "560", 0x01, 63111);
        var station620 = CreateStation("RFID-02", "620", 0x02, 63112);
        using var manager = CreateManager(station560, station620);

        await manager.StartAllAsync();
        await manager.StopYardAsync("560");

        var context620 = manager.GetContext("620")!;
        Assert.False(manager.GetContext("560")!.IsRunning);
        Assert.True(context620.IsRunning);

        await manager.RestartYardAsync("560");

        Assert.True(manager.GetContext("560")!.IsRunning);
        Assert.True(context620.IsRunning);
        await manager.StopAllAsync();
    }

    [Fact]
    public async Task Applying_yard_communication_changes_restarts_only_the_changed_yard()
    {
        var station560 = CreateStation("RFID-01", "560", 0x01, 63116);
        var station620 = CreateStation("RFID-02", "620", 0x02, 63117);
        var configuration560 = CreateCommunication("560");
        var configuration620 = CreateCommunication("620");
        using var manager = new YardCommunicationManager(
            new[] { configuration560, configuration620 },
            new[] { station560, station620 },
            CreateSettings(),
            new InMemoryPassageRecordStore());

        await manager.StartAllAsync();
        var original560 = manager.GetContext("560")!;
        var original620 = manager.GetContext("620")!;

        var changed560 = CreateCommunication("560", GetUnusedPort());
        await manager.ApplyConfigurationsAsync(new[] { changed560, configuration620 });

        Assert.NotSame(original560, manager.GetContext("560"));
        Assert.Same(original620, manager.GetContext("620"));
        Assert.True(manager.GetContext("560")!.IsRunning);
        Assert.True(manager.GetContext("620")!.IsRunning);
        await manager.StopAllAsync();
    }

    [Fact]
    public async Task Rejects_duplicate_enabled_listener_endpoints_before_replacing_contexts()
    {
        var station560 = CreateStation("RFID-01", "560", 0x01, 63118);
        var station620 = CreateStation("RFID-02", "620", 0x02, 63119);
        var configuration560 = CreateCommunication("560");
        var configuration620 = CreateCommunication("620");
        using var manager = new YardCommunicationManager(
            new[] { configuration560, configuration620 },
            new[] { station560, station620 },
            CreateSettings(),
            new InMemoryPassageRecordStore());

        var original560 = manager.GetContext("560");
        var original620 = manager.GetContext("620");
        var duplicate620 = CreateCommunication("620", configuration560.ListenPort);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.ApplyConfigurationsAsync(new[] { configuration560, duplicate620 }));

        Assert.Same(original560, manager.GetContext("560"));
        Assert.Same(original620, manager.GetContext("620"));
    }

    [Fact]
    public void Map_unassigned_station_is_not_silently_added_to_communication_context()
    {
        var station = CreateStation("RFID-01", string.Empty, 0x01, 63121);
        using var manager = new YardCommunicationManager(
            new[] { CreateCommunication("560") },
            new[] { station },
            CreateSettings(),
            new InMemoryPassageRecordStore());

        Assert.Empty(manager.GetContext("560")!.Stations);
        Assert.Contains(manager.Diagnostics, item => item.Contains("未分配通信站场"));
    }

    [Fact]
    public void Assigned_station_without_a_map_point_still_enters_its_communication_context()
    {
        var station = CreateStation("RFID-01", "560", 0x01, 63125);
        using var manager = new YardCommunicationManager(
            new[] { CreateCommunication("560") },
            new[] { station },
            CreateSettings(),
            new InMemoryPassageRecordStore());

        Assert.Contains(station, manager.GetContext("560")!.Stations);
        Assert.Empty(manager.Diagnostics);
    }

    [Fact]
    public void Same_protocol_address_is_allowed_when_yard_endpoints_are_different()
    {
        var station560 = CreateStation("RFID-01", "560", 0x01, 63131);
        var station620 = CreateStation("RFID-02", "620", 0x01, 63132);

        using var manager = CreateManager(station560, station620);

        Assert.Equal(new[] { "RFID-01" }, manager.GetContext("560")!.Stations.Select(item => item.StationId));
        Assert.Equal(new[] { "RFID-02" }, manager.GetContext("620")!.Stations.Select(item => item.StationId));
        Assert.Empty(manager.Diagnostics);
    }

    [Fact]
    public async Task Context_publishes_each_manual_command_with_its_real_type_once()
    {
        var station = CreateStation("RFID-01", "560", 0x01, 63135);
        station.Enabled = false;
        using var context = new YardCommunicationContext(
            CreateCommunication("560"),
            new[] { station },
            CreateSettings(),
            new InMemoryPassageRecordStore());
        var commands = new List<RfidPollCommand>();
        context.StationCommandSent += (_, sentStation, command, _) =>
        {
            Assert.Same(station, sentStation);
            commands.Add(command);
        };

        await context.StartAsync();
        try
        {
            await context.SendAsync(station, RfidPollCommand.Read);
            await context.SendAsync(station, RfidPollCommand.Clear);
        }
        finally
        {
            await context.StopAsync();
        }

        Assert.Equal(new[] { RfidPollCommand.Read, RfidPollCommand.Clear }, commands);
    }

    [Fact]
    public async Task Manager_publishes_real_datagram_sent_with_the_owning_context()
    {
        var station = CreateStation("RFID-01", "560", 0x01, 63136);
        station.Enabled = false;
        using var manager = new YardCommunicationManager(
            new[] { CreateCommunication("560") },
            new[] { station },
            CreateSettings(),
            new InMemoryPassageRecordStore());
        var sent = new TaskCompletionSource<(YardCommunicationContext Context, RfidUdpDatagramSentEventArgs Args)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        manager.DatagramSent += (context, args) => sent.TrySetResult((context, args));

        await manager.StartAllAsync();
        try
        {
            await manager.GetContext("560")!.SendAsync(station, RfidPollCommand.Clear);
            var completed = await Task.WhenAny(sent.Task, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.Same(sent.Task, completed);
            var result = await sent.Task;

            Assert.Equal("560", result.Context.YardId);
            Assert.Equal(station.Port, result.Args.DestinationEndPoint.Port);
            Assert.Equal(0x01, result.Args.Data[2]);
            Assert.Equal(0x01, result.Args.Data[4]);
        }
        finally
        {
            await manager.StopAllAsync();
        }
    }

    [Fact]
    public async Task A_port_conflict_is_isolated_to_the_conflicting_yard()
    {
        var occupiedPort = GetUnusedPort();
        using var occupiedSocket = new UdpClient(new IPEndPoint(IPAddress.Loopback, occupiedPort));
        var station560 = CreateStation("RFID-01", "560", 0x01, 63141);
        var station620 = CreateStation("RFID-02", "620", 0x02, 63142);
        var configuration560 = CreateCommunication("560", occupiedPort);
        var configuration620 = CreateCommunication("620");
        using var manager = new YardCommunicationManager(
            new[] { configuration560, configuration620 },
            new[] { station560, station620 },
            CreateSettings(),
            new InMemoryPassageRecordStore());

        await manager.StartAllAsync();

        Assert.False(manager.GetContext("560")!.IsRunning);
        Assert.NotNull(manager.GetContext("560")!.LastError);
        Assert.True(manager.GetContext("620")!.IsRunning);
        await manager.StopAllAsync();
    }

    [Fact]
    public void Runtime_state_in_one_yard_does_not_change_when_another_yard_processes_a_frame()
    {
        var station560 = CreateStation("RFID-01", "560", 0x01, 63151);
        var station620 = CreateStation("RFID-02", "620", 0x02, 63152);
        using var manager = CreateManager(station560, station620);
        var at = DateTimeOffset.UtcNow;

        manager.GetContext("560")!.ProcessFrame(CreateFrame(station560, at, 0x0001));
        var context620 = manager.GetContext("620")!;
        Assert.Equal(0, context620.RuntimeStates.Single().DetectedVehicleCount);

        context620.ProcessFrame(CreateFrame(station620, at, 0x0002));

        Assert.Equal(1, manager.GetContext("560")!.RuntimeStates.Single().DetectedVehicleCount);
        Assert.Equal(1, context620.RuntimeStates.Single().DetectedVehicleCount);
    }

    [Fact]
    public void Clear_command_in_one_yard_does_not_change_the_other_yard_runtime()
    {
        var station560 = CreateStation("RFID-01", "560", 0x01, 63161);
        var station620 = CreateStation("RFID-02", "620", 0x02, 63162);
        using var manager = CreateManager(station560, station620);
        var at = DateTimeOffset.UtcNow;
        var context560 = manager.GetContext("560")!;
        var context620 = manager.GetContext("620")!;
        context560.ProcessFrame(CreateFrame(station560, at, Enumerable.Range(1, 11).Select(value => (ushort)value).ToArray()));
        context620.ProcessFrame(CreateFrame(station620, at, 0x0201));

        var state620 = context620.RuntimeStates.Single();
        context560.RuntimeCoordinator!.MarkCommandSent(station560, RfidPollCommand.Clear, at.AddMilliseconds(1));

        Assert.Equal(PassageLifecycleState.Recognizing, state620.LifecycleState);
        Assert.Equal(1, state620.DetectedVehicleCount);
        Assert.False(state620.PendingClear);
    }

    [Fact]
    public void Legacy_shared_listener_is_explicitly_diagnosed_and_marked()
    {
        var station = CreateStation("RFID-01", string.Empty, 0x01, 63171);
        using var manager = YardCommunicationManager.CreateLegacyShared(
            new[] { station },
            CreateSettings(),
            new InMemoryPassageRecordStore(),
            IPAddress.Loopback,
            GetUnusedPort());

        Assert.True(manager.GetContext(YardCommunicationManager.LegacySharedListenerId)!.IsLegacySharedListener);
        Assert.Contains(manager.Diagnostics, item => item.Contains("LegacySharedListener"));
    }

    private static YardCommunicationManager CreateManager(
        RfidStationConfig station560,
        RfidStationConfig station620) => new(
        new[] { CreateCommunication("560"), CreateCommunication("620") },
        new[] { station560, station620 },
        CreateSettings(),
        new InMemoryPassageRecordStore());

    private static YardCommunicationConfig CreateCommunication(string yardId, int? port = null) => new()
    {
        YardId = yardId,
        ListenIp = IPAddress.Loopback.ToString(),
        ListenPort = port ?? GetUnusedPort(),
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

    private static RfidStationFrame CreateFrame(
        RfidStationConfig station,
        DateTimeOffset receivedAt,
        ushort rfid)
        => CreateFrame(station, receivedAt, new[] { rfid });

    private static RfidStationFrame CreateFrame(
        RfidStationConfig station,
        DateTimeOffset receivedAt,
        IReadOnlyList<ushort> rfids)
    {
        var slots = new ushort[14];
        for (var index = 0; index < rfids.Count && index < slots.Length; index++)
        {
            slots[index] = rfids[index];
        }
        return new RfidStationFrame
        {
            StationAddress = station.ProtocolAddress,
            SourceEndpoint = new IPEndPoint(IPAddress.Loopback, station.Port),
            ReceivedAt = receivedAt,
            RawRfidSlots = slots,
            ValidRfids = rfids.ToArray(),
            ActualNonZeroSlotCount = rfids.Count,
            ReportedCardCount = (byte)rfids.Count
        };
    }

    private static int GetUnusedPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }
}
