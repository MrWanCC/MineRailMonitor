using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Simulator.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class SimulatorStationContextTests
{
    [Fact]
    public async Task Six_contexts_bind_and_respond_independently()
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
                var response = await SendAsync(context.Config.ListenPort, CreateRequest(context.Config.ProtocolAddress));

                Assert.Equal(40, response.Length);
                Assert.Equal(context.Config.ProtocolAddress, response[2]);
            }

            Assert.All(contexts, context => Assert.True(context.IsRunning));
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
    public async Task Clear_and_stop_affect_only_the_selected_context()
    {
        var first = CreateContext(0x01, GetUnusedUdpPort());
        var second = CreateContext(0x02, GetUnusedUdpPort());
        first.SetSlots(CreateSlots(0x0001));
        second.SetSlots(CreateSlots(0x0002));

        try
        {
            await first.StartAsync();
            await second.StartAsync();

            Assert.Equal((ushort)0x0001, ReadSlot(await SendAsync(first.Config.ListenPort, CreateRequest(0x01)), 0));
            Assert.Equal((ushort)0x0002, ReadSlot(await SendAsync(second.Config.ListenPort, CreateRequest(0x02)), 0));

            await SendAsync(second.Config.ListenPort, CreateRequest(0x02, clear: true));

            Assert.Equal((ushort)0x0001, ReadSlot(await SendAsync(first.Config.ListenPort, CreateRequest(0x01)), 0));
            Assert.Equal((ushort)0x0000, ReadSlot(await SendAsync(second.Config.ListenPort, CreateRequest(0x02)), 0));

            second.Stop();
            Assert.Equal((ushort)0x0001, ReadSlot(await SendAsync(first.Config.ListenPort, CreateRequest(0x01)), 0));
            Assert.False(second.IsRunning);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    public async Task Same_protocol_address_on_different_ports_is_isolated()
    {
        var first = CreateContext(0x01, GetUnusedUdpPort());
        var second = CreateContext(0x01, GetUnusedUdpPort());
        first.SetSlots(CreateSlots(0x0011));
        second.SetSlots(CreateSlots(0x0022));

        try
        {
            await first.StartAsync();
            await second.StartAsync();

            var firstResponse = await SendAsync(first.Config.ListenPort, CreateRequest(0x01));
            var secondResponse = await SendAsync(second.Config.ListenPort, CreateRequest(0x01));

            Assert.Equal((ushort)0x0011, ReadSlot(firstResponse, 0));
            Assert.Equal((ushort)0x0022, ReadSlot(secondResponse, 0));
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    public void Scenario_players_are_independent_and_preserve_duplicates()
    {
        var first = CreateContext(0x01, GetUnusedUdpPort());
        var second = CreateContext(0x02, GetUnusedUdpPort());

        try
        {
            first.LoadScenario("正常11节", new ushort[] { 0x0001, 0x0011 });
            second.LoadScenario("重复RFID", new ushort[] { 0x0002, 0x0021, 0x0021 });

            Assert.True(first.StepScenario());
            Assert.Equal(1, first.Playback.CurrentIndex);
            Assert.Equal(0, second.Playback.CurrentIndex);

            Assert.True(second.StepScenario());
            Assert.True(second.StepScenario());
            Assert.True(second.StepScenario());
            Assert.Equal(new ushort[] { 0x0002, 0x0021, 0x0021 }, second.Playback.Slots.Take(3));
            Assert.Equal(3, second.Playback.CurrentIndex);

            second.ResetScenario();
            Assert.Equal(0, second.Playback.CurrentIndex);
            Assert.All(second.Playback.Slots, value => Assert.Equal((ushort)0, value));
            Assert.Equal(1, first.Playback.CurrentIndex);
            Assert.Equal((ushort)0x0001, first.Playback.CurrentRfid);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    public async Task Occupied_port_stops_only_that_context_and_reports_a_clear_error()
    {
        var occupiedPort = GetUnusedUdpPort();
        using var holder = new UdpClient(new IPEndPoint(IPAddress.Loopback, occupiedPort));
        var blocked = CreateContext(0x01, occupiedPort);
        var healthy = CreateContext(0x02, GetUnusedUdpPort());

        try
        {
            var exception = await Record.ExceptionAsync(() => blocked.StartAsync());

            Assert.Null(exception);
            Assert.False(blocked.IsRunning);
            Assert.Contains("端口已被占用", blocked.ErrorMessage, StringComparison.Ordinal);

            await healthy.StartAsync();
            var response = await SendAsync(healthy.Config.ListenPort, CreateRequest(healthy.Config.ProtocolAddress));
            Assert.Equal(healthy.Config.ProtocolAddress, response[2]);
            Assert.True(healthy.IsRunning);
        }
        finally
        {
            blocked.Dispose();
            healthy.Dispose();
        }
    }

    [Fact]
    public async Task Applying_configuration_rebinds_only_the_running_context()
    {
        var first = CreateContext(0x01, GetUnusedUdpPort());
        var second = CreateContext(0x02, GetUnusedUdpPort());
        var oldPort = first.Config.ListenPort;
        var replacementPort = GetUnusedUdpPort();

        try
        {
            await first.StartAsync();
            await second.StartAsync();
            var updated = first.Config.Clone();
            updated.ListenPort = replacementPort;
            updated.ProtocolAddress = 0x0A;

            Assert.True(first.TryApplyConfiguration(updated, out var error));
            Assert.Empty(error);
            Assert.True(first.IsRunning);
            Assert.Equal(replacementPort, first.Config.ListenPort);
            Assert.Equal((byte)0x0A, first.Config.ProtocolAddress);
            Assert.False(IsUdpPortBound(oldPort));

            var firstResponse = await SendAsync(replacementPort, CreateRequest(0x0A));
            var secondResponse = await SendAsync(second.Config.ListenPort, CreateRequest(0x02));
            Assert.Equal((byte)0x0A, firstResponse[2]);
            Assert.Equal((byte)0x02, secondResponse[2]);
            Assert.True(second.IsRunning);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    public async Task Applying_an_occupied_port_stops_only_the_current_context()
    {
        var first = CreateContext(0x01, GetUnusedUdpPort());
        var second = CreateContext(0x02, GetUnusedUdpPort());
        var occupiedPort = GetUnusedUdpPort();
        using var holder = new UdpClient(new IPEndPoint(IPAddress.Loopback, occupiedPort));

        try
        {
            await first.StartAsync();
            await second.StartAsync();
            var updated = first.Config.Clone();
            updated.ListenPort = occupiedPort;

            Assert.False(first.TryApplyConfiguration(updated, out var error));
            Assert.Contains("端口已被占用", error, StringComparison.Ordinal);
            Assert.False(first.IsRunning);

            var secondResponse = await SendAsync(second.Config.ListenPort, CreateRequest(0x02));
            Assert.Equal((byte)0x02, secondResponse[2]);
            Assert.True(second.IsRunning);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    private static SimulatorStationContext CreateContext(byte address, int port) => new(new SimulatorStationConfig
    {
        StationName = $"TEST-{address:X2}",
        ListenIp = IPAddress.Loopback.ToString(),
        ListenPort = port,
        ProtocolAddress = address,
        Enabled = true,
        EmptySlotValue = 0,
        CrcHigh = 0,
        CrcLow = 0
    });

    private static ushort[] CreateSlots(ushort first)
    {
        var slots = new ushort[14];
        slots[0] = first;
        return slots;
    }

    private static byte[] CreateRequest(byte address, bool clear = false)
    {
        var request = new byte[40];
        request[0] = 0xB0;
        request[1] = 0xB0;
        request[2] = address;
        request[3] = 0x04;
        request[4] = clear ? (byte)0x01 : (byte)0x00;
        request[38] = 0xAA;
        request[39] = 0xAA;
        return request;
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

    private static ushort ReadSlot(byte[] frame, int index)
    {
        var offset = 8 + index * 2;
        return (ushort)(frame[offset] | frame[offset + 1] << 8);
    }

    private static int GetUnusedUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    private static bool IsUdpPortBound(int port)
    {
        try
        {
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }
}
