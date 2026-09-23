using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Infrastructure.ExternalData;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class UdpExternalDataInterfaceTests
{
    [Fact]
    public async Task Sends_payload_byte_for_byte_without_rebuilding_it()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = (IPEndPoint)receiver.Client.LocalEndPoint!;
        using var sender = new UdpExternalDataInterface(endpoint);
        var payload = Enumerable.Range(0, 40).Select(value => (byte)value).ToArray();

        await sender.SendAsync(payload, CancellationToken.None);
        var received = await receiver.ReceiveAsync();

        Assert.Equal(payload, received.Buffer);
    }
}
