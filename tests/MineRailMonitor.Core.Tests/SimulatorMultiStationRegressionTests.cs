using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Simulator.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class SimulatorMultiStationRegressionTests
{
    [Fact]
    public async Task Repeated_reads_and_slot_changes_remain_isolated_across_six_stations()
    {
        var contexts = Enumerable.Range(1, 6)
            .Select(address => CreateContext((byte)address, GetUnusedUdpPort()))
            .ToArray();
        contexts[0].SetSlots(CreateSlots(0x0101));
        contexts[1].SetSlots(CreateSlots(0x0202));

        try
        {
            foreach (var context in contexts)
            {
                await context.StartAsync();
            }

            for (var attempt = 0; attempt < 3; attempt++)
            {
                foreach (var context in contexts)
                {
                    var response = await SendAsync(context.Config.ListenPort, CreateRequest(context.Config.ProtocolAddress));
                    Assert.Equal(context.Config.ProtocolAddress, response[2]);
                }
            }

            contexts[0].SetSlots(CreateSlots(0x0A0A));
            var firstResponse = await SendAsync(contexts[0].Config.ListenPort, CreateRequest(0x01));
            var secondResponse = await SendAsync(contexts[1].Config.ListenPort, CreateRequest(0x02));
            Assert.Equal((ushort)0x0A0A, ReadSlot(firstResponse));
            Assert.Equal((ushort)0x0202, ReadSlot(secondResponse));
            Assert.Equal(4, contexts[0].RequestCount);
            Assert.Equal(4, contexts[1].RequestCount);
        }
        finally
        {
            foreach (var context in contexts)
            {
                context.Dispose();
            }
        }
    }

    [Fact]
    public async Task One_station_bind_failure_does_not_stop_the_other_five_listeners()
    {
        var occupiedPort = GetUnusedUdpPort();
        using var holder = new UdpClient(new IPEndPoint(IPAddress.Loopback, occupiedPort));
        var contexts = Enumerable.Range(1, 6)
            .Select(address => CreateContext((byte)address, address == 1 ? occupiedPort : GetUnusedUdpPort()))
            .ToArray();

        try
        {
            foreach (var context in contexts)
            {
                await context.StartAsync();
            }

            Assert.False(contexts[0].IsRunning);
            Assert.Contains("端口已被占用", contexts[0].ErrorMessage, StringComparison.Ordinal);
            Assert.All(contexts.Skip(1), context => Assert.True(context.IsRunning));

            var response = await SendAsync(contexts[5].Config.ListenPort, CreateRequest(0x06));
            Assert.Equal((byte)0x06, response[2]);
        }
        finally
        {
            foreach (var context in contexts)
            {
                context.Dispose();
            }
        }
    }

    [Fact]
    public async Task All_stations_release_ports_and_can_restart_in_the_same_process()
    {
        var contexts = Enumerable.Range(1, 6)
            .Select(address => CreateContext((byte)address, GetUnusedUdpPort()))
            .ToArray();

        try
        {
            foreach (var context in contexts)
            {
                await context.StartAsync();
            }

            foreach (var context in contexts)
            {
                context.Stop();
            }

            await Task.Delay(50);
            foreach (var context in contexts)
            {
                await context.StartAsync();
            }

            Assert.All(contexts, context => Assert.True(context.IsRunning));
            var response = await SendAsync(contexts[2].Config.ListenPort, CreateRequest(0x03));
            Assert.Equal((byte)0x03, response[2]);
        }
        finally
        {
            foreach (var context in contexts)
            {
                context.Dispose();
            }
        }
    }

    private static SimulatorStationContext CreateContext(byte address, int port) => new(new SimulatorStationConfig
    {
        StationName = $"REGRESSION-{address:X2}",
        ListenIp = IPAddress.Loopback.ToString(),
        ListenPort = port,
        ProtocolAddress = address,
        Enabled = true
    });

    private static ushort[] CreateSlots(ushort value)
    {
        var slots = new ushort[14];
        slots[0] = value;
        return slots;
    }

    private static byte[] CreateRequest(byte address)
    {
        var frame = new byte[40];
        frame[0] = 0xB0;
        frame[1] = 0xB0;
        frame[2] = address;
        frame[3] = 0x04;
        frame[38] = 0xAA;
        frame[39] = 0xAA;
        return frame;
    }

    private static async Task<byte[]> SendAsync(int port, byte[] request)
    {
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await client.SendAsync(request, request.Length, new IPEndPoint(IPAddress.Loopback, port));
        var receiveTask = client.ReceiveAsync();
        var completed = await Task.WhenAny(receiveTask, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(receiveTask, completed);
        return (await receiveTask).Buffer;
    }

    private static ushort ReadSlot(byte[] frame) => (ushort)(frame[8] | frame[9] << 8);

    private static int GetUnusedUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }
}
