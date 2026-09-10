using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Simulator.Communication;
using MineRailMonitor.Simulator.Models;
using MineRailMonitor.Simulator.Protocol;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidUdpTransportTests
{
    [Fact]
    public async Task ReceivesCompleteDatagramWithoutChangingContent()
    {
        var expected = CreateFrame();
        using var receiver = new RfidUdpTransport(IPAddress.Loopback, 0);
        var receivedTask = ReceiveOneAsync(receiver);
        var startTask = receiver.StartAsync(CancellationToken.None);
        using var simulator = new SimulatorUdpTransport(
            IPAddress.Loopback,
            0,
            IPAddress.Loopback,
            receiver.LocalEndPoint.Port);

        await simulator.SendAsync(expected, CancellationToken.None);
        var received = await receivedTask;

        Assert.True(received.IsValid);
        Assert.Equal(40, received.Data.Length);
        Assert.Equal(expected, received.Data);
        Assert.Equal(IPAddress.Loopback, received.RemoteEndPoint.Address);

        receiver.Stop();
        await startTask;
    }

    [Fact]
    public async Task InvalidDatagramsAreReportedWithoutStoppingReceiver()
    {
        var received = new List<RfidUdpDatagramEventArgs>();
        using var receiver = new RfidUdpTransport(IPAddress.Loopback, 0);
        receiver.DatagramReceived += (_, args) => received.Add(args);
        var startTask = receiver.StartAsync(CancellationToken.None);
        using var simulator = new SimulatorUdpTransport(
            IPAddress.Loopback,
            0,
            IPAddress.Loopback,
            receiver.LocalEndPoint.Port);

        await simulator.SendAsync(new byte[35], CancellationToken.None);
        await simulator.SendAsync(CreateFrame(firstByte: 0x00), CancellationToken.None);
        await simulator.SendAsync(CreateFrame(lastByte: 0x00), CancellationToken.None);
        await EventuallyAsync(() => received.Count == 3, TimeSpan.FromSeconds(3));

        Assert.False(received[0].IsValid);
        Assert.False(received[1].IsValid);
        Assert.False(received[2].IsValid);
        Assert.Equal("Length must be 40 bytes.", received[0].ValidationError);
        Assert.Equal("Invalid frame header.", received[1].ValidationError);
        Assert.Equal("Invalid frame tail.", received[2].ValidationError);

        receiver.Stop();
        await startTask;
    }

    [Fact]
    public async Task ReceivesAtLeastThreeConsecutiveDatagrams()
    {
        var expected = CreateFrame();
        var received = new List<RfidUdpDatagramEventArgs>();
        using var receiver = new RfidUdpTransport(IPAddress.Loopback, 0);
        receiver.DatagramReceived += (_, args) => received.Add(args);
        var startTask = receiver.StartAsync(CancellationToken.None);
        using var simulator = new SimulatorUdpTransport(
            IPAddress.Loopback,
            0,
            IPAddress.Loopback,
            receiver.LocalEndPoint.Port);

        for (var index = 0; index < 3; index++)
        {
            await simulator.SendAsync(expected, CancellationToken.None);
        }

        await EventuallyAsync(() => received.Count == 3, TimeSpan.FromSeconds(3));
        Assert.All(received, item => Assert.Equal(expected, item.Data));

        receiver.Stop();
        await startTask;
    }

    [Fact]
    public async Task StopReleasesBoundSocket()
    {
        using var receiver = new RfidUdpTransport(IPAddress.Loopback, 0);
        var port = receiver.LocalEndPoint.Port;
        var startTask = receiver.StartAsync(CancellationToken.None);

        receiver.Stop();
        await startTask;

        using var rebound = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        Assert.Equal(port, ((IPEndPoint)rebound.Client.LocalEndPoint!).Port);
    }

    private static async Task<RfidUdpDatagramEventArgs> ReceiveOneAsync(RfidUdpTransport receiver)
    {
        var completion = new TaskCompletionSource<RfidUdpDatagramEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.DatagramReceived += (_, args) => completion.TrySetResult(args);
        return await WithTimeoutAsync(completion.Task, TimeSpan.FromSeconds(3));
    }

    private static async Task EventuallyAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Expected UDP datagrams were not received in time.");
            }

            await Task.Delay(10);
        }
    }

    private static async Task<T> WithTimeoutAsync<T>(Task<T> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
        {
            throw new TimeoutException("Expected UDP datagram was not received in time.");
        }

        return await task;
    }

    private static byte[] CreateFrame(byte? firstByte = null, byte? lastByte = null)
    {
        var input = new SimulatorFrameInput
        {
            Address = 0x03,
            EmptySlotValue = 0,
            Slots = new ushort[14],
            CrcHigh = 0x12,
            CrcLow = 0x34
        };
        var frame = RfidResponseFrameBuilder.Build(input);
        frame[0] = firstByte ?? frame[0];
        frame[39] = lastByte ?? frame[39];
        return frame;
    }
}
