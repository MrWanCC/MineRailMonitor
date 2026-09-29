using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Core.ExternalAlarm;

namespace MineRailMonitor.Core.Tests;

public sealed class ExternalAlarmUdpSenderTests
{
    [Fact]
    public async Task Sends_from_the_configured_local_udp_port()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var target = (IPEndPoint)receiver.Client.LocalEndPoint!;
        var sourcePort = GetUnusedPort();
        var constructor = typeof(ExternalAlarmUdpSender).GetConstructor(
            new[] { typeof(IPEndPoint), typeof(IPEndPoint) });

        Assert.NotNull(constructor);
        using var sender = (ExternalAlarmUdpSender)constructor!.Invoke(
            new object[]
            {
                target,
                new IPEndPoint(IPAddress.Loopback, sourcePort)
            });

        await sender.SendAsync(new byte[] { 0xB0, 0xB0 }, CancellationToken.None);
        var received = await receiver.ReceiveAsync();

        Assert.Equal(sourcePort, received.RemoteEndPoint.Port);
        Assert.Equal(new byte[] { 0xB0, 0xB0 }, received.Buffer);
    }

    private static int GetUnusedPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }
}
