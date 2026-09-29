using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Core.ExternalAlarm;

namespace MineRailMonitor.Core.Tests;

public sealed class ExternalAlarmUdpChannelTests
{
    [Fact]
    public async Task Send_uses_the_same_fixed_local_port_as_receive_listener()
    {
        var localPort = GetUnusedPort();
        var targetPort = GetUnusedPort();
        var localEndpoint = new IPEndPoint(IPAddress.Loopback, localPort);
        var targetEndpoint = new IPEndPoint(IPAddress.Loopback, targetPort);
        using var target = new UdpClient(targetEndpoint);
        using var channel = new ExternalAlarmUdpChannel(localEndpoint, targetEndpoint);

        await channel.StartAsync();
        try
        {
            var payload = new byte[] { 0xB0, 0xB0, 0x01, 0x04, 0x01, 0x00, 0x00, 0x00, 0x0B, 0x59, 0xAA, 0xAA };
            await channel.SendAsync(payload, CancellationToken.None);

            var received = await ReceiveAsync(target);

            Assert.Equal(payload, received.Buffer);
            Assert.Equal(localPort, received.RemoteEndPoint.Port);
        }
        finally
        {
            await channel.StopAsync();
        }
    }

    [Fact]
    public async Task Receive_clear_command_on_the_same_socket_used_for_send()
    {
        var localPort = GetUnusedPort();
        var targetPort = GetUnusedPort();
        var localEndpoint = new IPEndPoint(IPAddress.Loopback, localPort);
        var targetEndpoint = new IPEndPoint(IPAddress.Loopback, targetPort);
        using var channel = new ExternalAlarmUdpChannel(localEndpoint, targetEndpoint);
        var received = new TaskCompletionSource<ExternalAlarmClearCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ClearReceived += (command, _) => received.TrySetResult(command);
        await channel.StartAsync();

        try
        {
            using var sender = new UdpClient();
            var clear = new ExternalAlarmFrameCodec().EncodeClear(0x01);
            await sender.SendAsync(clear, clear.Length, localEndpoint);

            var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.Same(received.Task, completed);
            Assert.Equal((byte)0x01, (await received.Task).StationAddress);
        }
        finally
        {
            await channel.StopAsync();
        }
    }

    private static async Task<UdpReceiveResult> ReceiveAsync(UdpClient client)
    {
        var receiveTask = client.ReceiveAsync();
        var completed = await Task.WhenAny(receiveTask, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(receiveTask, completed);
        return await receiveTask;
    }

    private static int GetUnusedPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }
}
