using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Simulator.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class YardCommunicationClosedLoopTests
{
    private static readonly object PortAllocationLock = new();
    private static readonly HashSet<int> AllocatedPorts = new();

    [Fact]
    public async Task Two_yards_complete_independent_udp_poll_runtime_clear_and_restart_loop()
    {
        var simulator560 = CreateSimulator("SIM-560", 0x01, GetUnusedUdpPort());
        var simulator620 = CreateSimulator("SIM-620", 0x01, GetUnusedUdpPort());
        simulator560.SetSlots(CreateCompleteTrainSlots());
        simulator620.SetSlots(CreateStableSlot(0x0201));

        var station560 = CreateStation("RFID-560-01", "560", 0x01, simulator560.Config.ListenPort);
        var station620 = CreateStation("RFID-620-01", "620", 0x01, simulator620.Config.ListenPort);
        var recordStore = new InMemoryPassageRecordStore();
        using var manager = new YardCommunicationManager(
            new[]
            {
                CreateCommunication("560"),
                CreateCommunication("620")
            },
            new[] { station560, station620 },
            CreateSettings(),
            recordStore);

        var parser = new RfidFrameParser(new RfidFrameParserOptions { EmptyRfidValue = 0 });
        manager.DatagramReceived += (context, args) => ProcessDatagram(context, args, parser);

        try
        {
            await simulator560.StartAsync();
            await simulator620.StartAsync();
            await manager.StartAllAsync();

            var context560 = Assert.IsType<YardCommunicationContext>(manager.GetContext("560"));
            var context620 = Assert.IsType<YardCommunicationContext>(manager.GetContext("620"));
            Assert.NotEqual(context560.ListenerEndPoint!.Port, context620.ListenerEndPoint!.Port);
            Assert.Equal(new[] { "RFID-560-01" }, context560.Stations.Select(item => item.StationId));
            Assert.Equal(new[] { "RFID-620-01" }, context620.Stations.Select(item => item.StationId));

            await WaitUntilAsync(() => simulator560.ResponseCount > 0 && simulator620.ResponseCount > 0);
            await WaitUntilAsync(() => context560.ResponseCount > 0 && context620.ResponseCount > 0);
            Assert.True(context560.ResponseCount > 0);
            Assert.True(context620.ResponseCount > 0);
            Assert.Equal(0x01, context560.PollingStatuses.Single().StationAddress);
            Assert.Equal(0x01, context620.PollingStatuses.Single().StationAddress);
            Assert.True(context560.PollingStatuses.Single().SentCount > 0);
            Assert.True(context620.PollingStatuses.Single().SentCount > 0);
            Assert.True(context560.PollingStatuses.Single().ReceivedCount > 0);
            Assert.True(context620.PollingStatuses.Single().ReceivedCount > 0);
            Assert.True(context560.PollingStatuses.Single().IsOnline);
            Assert.True(context620.PollingStatuses.Single().IsOnline);
            Assert.Equal(0, context560.PollingStatuses.Single().TimeoutCount);
            Assert.Equal(0, context620.PollingStatuses.Single().TimeoutCount);

            await WaitUntilAsync(() => simulator560.ClearCount > 0);
            var runtime620 = context620.RuntimeStates.Single();
            var runtime620Snapshot = (runtime620.LifecycleState, runtime620.DetectedVehicleCount, runtime620.PendingClear);
            Assert.Equal(0, simulator620.ClearCount);
            Assert.Equal((ushort)0x0201, simulator620.SnapshotSlots()[0]);
            Assert.Equal(runtime620Snapshot.LifecycleState, runtime620.LifecycleState);
            Assert.Equal(runtime620Snapshot.DetectedVehicleCount, runtime620.DetectedVehicleCount);
            Assert.Equal(runtime620Snapshot.PendingClear, runtime620.PendingClear);

            var responsesBeforeStop = simulator620.ResponseCount;
            await manager.StopYardAsync("560");
            Assert.False(context560.IsRunning);
            Assert.True(context620.IsRunning);
            await WaitUntilAsync(() => simulator620.ResponseCount >= responsesBeforeStop + 2);
            Assert.Equal(0, simulator620.ClearCount);

            var responsesBeforeRestart = simulator560.ResponseCount;
            await manager.RestartYardAsync("560");
            Assert.True(context560.IsRunning);
            Assert.True(context620.IsRunning);
            await WaitUntilAsync(() => simulator560.ResponseCount > responsesBeforeRestart);
            Assert.Equal(0, simulator620.ClearCount);
            Assert.True(simulator620.ResponseCount >= responsesBeforeStop + 2);
        }
        finally
        {
            await manager.StopAllAsync();
            simulator560.Dispose();
            simulator620.Dispose();
        }
    }

    private static void ProcessDatagram(
        YardCommunicationContext context,
        MineRailMonitor.Core.Communication.RfidUdpDatagramEventArgs args,
        RfidFrameParser parser)
    {
        if (!args.IsValid || args.Data.Length < 4 || args.Data[3] != 0x04 || args.Data.Length < 3 ||
            !context.RecordResponse(args.RemoteEndPoint, args.Data[2], args.ReceivedAt) ||
            !parser.TryParse(args.Data, args.RemoteEndPoint, args.ReceivedAt, out var frame) || frame is null)
        {
            return;
        }

        context.ProcessFrame(frame);
    }

    private static SimulatorStationContext CreateSimulator(string name, byte address, int port) =>
        new(new SimulatorStationConfig
        {
            StationName = name,
            ListenIp = IPAddress.Loopback.ToString(),
            ListenPort = port,
            ProtocolAddress = address,
            Enabled = true,
            EmptySlotValue = 0,
            CrcHigh = 0,
            CrcLow = 0
        });

    private static RfidStationConfig CreateStation(string id, string yardId, byte address, int port) => new()
    {
        StationId = id,
        Name = id,
        YardId = yardId,
        IpAddress = IPAddress.Loopback.ToString(),
        Port = port,
        ProtocolAddress = address,
        Enabled = true,
        Mode = 0x04,
        CommandBytes = new byte[4],
        RequestPayload = new byte[28]
    };

    private static YardCommunicationConfig CreateCommunication(string yardId) => new()
    {
        YardId = yardId,
        ListenIp = IPAddress.Loopback.ToString(),
        ListenPort = GetUnusedUdpPort(),
        Enabled = true
    };

    private static RfidSettings CreateSettings() => new()
    {
        PollIntervalMs = 20,
        ExpectedVehicleCount = 11,
        InterVehicleTimeoutSeconds = 30,
        EmptyRfidValue = 0
    };

    private static ushort[] CreateCompleteTrainSlots()
    {
        var slots = new ushort[14];
        for (var index = 0; index < 11; index++)
        {
            slots[index] = (ushort)(index + 1);
        }

        return slots;
    }

    private static ushort[] CreateStableSlot(ushort value)
    {
        var slots = new ushort[14];
        slots[0] = value;
        return slots;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
        while (!condition())
        {
            if (Stopwatch.GetTimestamp() >= deadline)
            {
                Assert.True(condition(), "等待 560/620 闭环状态超时。");
            }

            await Task.Delay(10);
        }
    }

    private static int GetUnusedUdpPort()
    {
        while (true)
        {
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var port = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
            lock (PortAllocationLock)
            {
                if (AllocatedPorts.Add(port))
                {
                    return port;
                }
            }
        }
    }
}
