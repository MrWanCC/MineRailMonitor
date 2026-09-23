using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Simulator.Communication;
using MineRailMonitor.Simulator.Models;
using MineRailMonitor.Simulator.Protocol;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidUdpTransportTests
{
    [Fact]
    public async Task UdpTransport_sent_event_is_raised_after_successful_send()
    {
        var expected = CreateFrame();
        using var receiver = new RfidUdpTransport(IPAddress.Loopback, 0);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var sentCompletion = new TaskCompletionSource<RfidUdpDatagramSentEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.DatagramSent += (_, args) => sentCompletion.TrySetResult(args);

        await receiver.SendAsync(expected, (IPEndPoint)client.Client.LocalEndPoint!, CancellationToken.None);
        expected[0] = 0x00;
        var received = await ReceiveWithTimeoutAsync(client, TimeSpan.FromSeconds(3));
        var sent = await WithTimeoutAsync(sentCompletion.Task, TimeSpan.FromSeconds(3));

        Assert.Equal(CreateFrame(), received.Buffer);
        Assert.Equal(CreateFrame(), sent.Data);
        Assert.Equal(client.Client.LocalEndPoint, sent.DestinationEndPoint);
        Assert.Equal(receiver.LocalEndPoint, sent.LocalEndPoint);
        Assert.NotEqual(default, sent.SentAt);
    }

    [Fact]
    public async Task UdpTransport_datagram_sent_observer_failure_does_not_fail_successful_send()
    {
        using var receiver = new RfidUdpTransport(IPAddress.Loopback, 0);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        receiver.DatagramSent += (_, _) => throw new InvalidOperationException("observer failure");
        var request = CreateFrame();

        var exception = await Record.ExceptionAsync(() => receiver.SendAsync(
            request,
            (IPEndPoint)client.Client.LocalEndPoint!,
            CancellationToken.None));

        Assert.Null(exception);
        Assert.Equal(request, (await ReceiveWithTimeoutAsync(client, TimeSpan.FromSeconds(3))).Buffer);
    }

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
    public async Task Listener_survives_unavailable_station_and_receives_from_live_station()
    {
        var received = new List<RfidUdpDatagramEventArgs>();
        using var receiver = new RfidUdpTransport(IPAddress.Loopback, 0);
        using var pollerCancellation = new CancellationTokenSource();
        using var responderCancellation = new CancellationTokenSource();
        using var responder = new SimulatorUdpResponder(IPAddress.Loopback, 0);
        var unavailableEndpoint = new IPEndPoint(IPAddress.Loopback, GetUnusedLoopbackPort());
        var liveStation = CreateStation(0x02, responder.LocalEndPoint);
        var unavailableStation = CreateStation(0x01, unavailableEndpoint);
        var poller = new RfidStationPoller(
            new[] { unavailableStation, liveStation },
            25,
            receiver,
            new SystemRfidTimeProvider());

        receiver.DatagramReceived += (_, args) =>
        {
            received.Add(args);
            if (args.IsValid && args.Data.Length >= 3)
            {
                poller.RecordResponse(args.RemoteEndPoint, args.Data[2], args.ReceivedAt);
            }
        };

        var receiveTask = receiver.StartAsync(CancellationToken.None);
        var responderTask = responder.RunAsync(_ => CreateFrame(address: 0x02), responderCancellation.Token);
        var pollerTask = poller.RunAsync(pollerCancellation.Token);

        try
        {
            await EventuallyAsync(
                () => received.Any(item => item.IsValid && item.RemoteEndPoint.Port == responder.LocalEndPoint.Port),
                TimeSpan.FromSeconds(3));

            Assert.Contains(poller.EndpointStatuses.Values, status => status.RequestCount > 0);
            Assert.True(poller.EndpointStatuses.Values.Single(status => status.StationAddress == 0x02).ResponseCount > 0);
        }
        finally
        {
            pollerCancellation.Cancel();
            responderCancellation.Cancel();
            receiver.Stop();
            try
            {
                await pollerTask;
            }
            catch (OperationCanceledException) when (pollerCancellation.IsCancellationRequested)
            {
            }
            await receiveTask;
            await responderTask;
        }
    }

    [Fact]
    public async Task Six_station_polling_keeps_live_station_responsive_when_other_endpoints_are_unavailable()
    {
        var received = new ConcurrentQueue<RfidUdpDatagramEventArgs>();
        var receiveErrors = new ConcurrentQueue<Exception>();
        using var receiver = new RfidUdpTransport(IPAddress.Loopback, 0);
        using var pollerCancellation = new CancellationTokenSource();
        using var responderCancellation = new CancellationTokenSource();
        var livePort = GetUnusedLoopbackPort();
        using var responder = new SimulatorUdpResponder(IPAddress.Loopback, livePort);
        var stations = Enumerable.Range(1, 6)
            .Select(address => CreateStation(
                (byte)address,
                new IPEndPoint(IPAddress.Loopback, address == 1 ? livePort : 10000 + address)))
            .ToArray();
        var poller = new RfidStationPoller(
            stations,
            25,
            receiver,
            new SystemRfidTimeProvider());

        receiver.DatagramReceived += (_, args) =>
        {
            received.Enqueue(args);
            if (args.IsValid && args.Data.Length >= 3)
            {
                poller.RecordResponse(args.RemoteEndPoint, args.Data[2], args.ReceivedAt);
            }
        };
        receiver.ReceiveError += receiveErrors.Enqueue;

        var receiveTask = receiver.StartAsync(CancellationToken.None);
        var responderTask = responder.RunAsync(_ => CreateFrame(address: 0x01), responderCancellation.Token);
        var pollerTask = poller.RunAsync(pollerCancellation.Token);

        try
        {
            var liveStatus = poller.StationStatuses[0x01];
            await EventuallyAsync(
                () => liveStatus.RequestCount >= 3 && liveStatus.ResponseCount >= 3,
                TimeSpan.FromSeconds(5));

            var requestCount = liveStatus.RequestCount;
            var responseCount = liveStatus.ResponseCount;
            await EventuallyAsync(
                () => liveStatus.RequestCount >= requestCount + 2 && liveStatus.ResponseCount >= responseCount + 2,
                TimeSpan.FromSeconds(5));

            Assert.NotNull(liveStatus.LastRequestAt);
            Assert.NotNull(liveStatus.LastResponseAt);
            Assert.All(
                stations.Skip(1),
                station => Assert.True(poller.StationStatuses[station.ProtocolAddress].RequestCount > 0));
            Assert.All(
                stations.Skip(1),
                station => Assert.Equal(0, poller.StationStatuses[station.ProtocolAddress].ResponseCount));
            Assert.Contains(
                received,
                item => item.IsValid && item.RemoteEndPoint.Port == responder.LocalEndPoint.Port);
            Assert.Empty(receiveErrors);
        }
        finally
        {
            pollerCancellation.Cancel();
            responderCancellation.Cancel();
            receiver.Stop();
            try
            {
                await pollerTask;
            }
            catch (OperationCanceledException) when (pollerCancellation.IsCancellationRequested)
            {
            }
            await receiveTask;
            await responderTask;
        }
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

    private static async Task<UdpReceiveResult> ReceiveWithTimeoutAsync(UdpClient client, TimeSpan timeout)
    {
        var receiveTask = client.ReceiveAsync();
        var completed = await Task.WhenAny(receiveTask, Task.Delay(timeout));
        if (completed != receiveTask)
        {
            throw new TimeoutException("Expected UDP datagram was not received in time.");
        }

        return await receiveTask;
    }

    private static int GetUnusedLoopbackPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private static RfidStationConfig CreateStation(byte address, IPEndPoint endpoint) => new()
    {
        Address = address,
        Enabled = true,
        DestinationEndpoint = endpoint,
        CommandBytes = new byte[4],
        RequestPayload = new byte[28]
    };

    private static byte[] CreateFrame(byte? firstByte = null, byte? lastByte = null, byte address = 0x03)
    {
        var input = new SimulatorFrameInput
        {
            Address = address,
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
