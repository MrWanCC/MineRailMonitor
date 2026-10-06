using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Simulator.Communication;
using MineRailMonitor.Simulator.Models;
using Xunit.Abstractions;

namespace MineRailMonitor.Core.Tests;

public sealed class SimulatorMultiStationRegressionTests
{
    private readonly ITestOutputHelper _output;

    public SimulatorMultiStationRegressionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Udp_response_can_arrive_before_packet_handled_count_update_completes()
    {
        using var responder = new SimulatorUdpResponder(IPAddress.Loopback, 0);
        using var cancellation = new CancellationTokenSource();
        using var callbackGate = new ManualResetEventSlim(false);
        var callbackStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCompleted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 3;
        long eventSequence = 0;
        long responseReceivedSequence = 0;
        long packetHandledCompletedSequence = 0;
        DateTimeOffset? callbackStartedAt = null;
        DateTimeOffset? callbackCompletedAt = null;
        DateTimeOffset? responseReceivedAt = null;
        var countAtResponse = -1;

        responder.PacketHandled += (_, _, _) =>
        {
            var requestSequence = Volatile.Read(ref requestCount) + 1;
            callbackStartedAt = DateTimeOffset.UtcNow;
            callbackStarted.TrySetResult(requestSequence == 4);
            callbackGate.Wait();
            var updatedCount = Interlocked.Increment(ref requestCount);
            callbackCompletedAt = DateTimeOffset.UtcNow;
            packetHandledCompletedSequence = Interlocked.Increment(ref eventSequence);
            callbackCompleted.TrySetResult(updatedCount);
        };

        var responderTask = responder.RunAsync(request => request, cancellation.Token);
        try
        {
            using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var responseTask = client.ReceiveAsync();
            var request = CreateRequest(0x01);
            await client.SendAsync(request, request.Length, responder.LocalEndPoint);

            var responseWait = await Task.WhenAny(responseTask, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.Same(responseTask, responseWait);
            var response = await responseTask;
            Assert.Equal(request, response.Buffer);
            responseReceivedAt = DateTimeOffset.UtcNow;
            responseReceivedSequence = Interlocked.Increment(ref eventSequence);
            countAtResponse = Volatile.Read(ref requestCount);

            var callbackWait = await Task.WhenAny(callbackStarted.Task, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.Same(callbackStarted.Task, callbackWait);
            Assert.True(await callbackStarted.Task);
            Assert.Equal(3, countAtResponse);
            Assert.False(callbackCompleted.Task.IsCompleted);
        }
        finally
        {
            callbackGate.Set();
            cancellation.Cancel();
            await responderTask;
        }

        var completedCount = await callbackCompleted.Task;
        var completedAt = callbackCompletedAt ?? throw new InvalidOperationException("PacketHandled completion time was not recorded.");
        var receivedAt = responseReceivedAt ?? throw new InvalidOperationException("Response receive time was not recorded.");
        Assert.Equal(4, completedCount);
        Assert.True(
            responseReceivedSequence < packetHandledCompletedSequence,
            $"ResponseReceivedAt={receivedAt:O} sequence={responseReceivedSequence}; PacketHandledCompletedAt={completedAt:O} sequence={packetHandledCompletedSequence}");
        _output.WriteLine($"ResponseReceived seq=4 at={receivedAt:O} RequestCount={countAtResponse}");
        _output.WriteLine($"PacketHandledStarted seq=4 at={callbackStartedAt:O}");
        _output.WriteLine($"PacketHandledCompleted seq=4 at={completedAt:O} RequestCount={completedCount}");
    }

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
            using var requestCountTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await WaitForRequestCountAsync(contexts[0], 4, requestCountTimeout.Token);
            await WaitForRequestCountAsync(contexts[1], 4, requestCountTimeout.Token);
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
        var crc = ModbusCrc16.Compute(frame, 0, 36);
        frame[36] = ModbusCrc16.LowByte(crc);
        frame[37] = ModbusCrc16.HighByte(crc);
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

    private static async Task WaitForRequestCountAsync(
        SimulatorStationContext context,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        if (context.RequestCount >= expectedCount)
        {
            return;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(SimulatorStationContext.RequestCount) &&
                context.RequestCount >= expectedCount)
            {
                completion.TrySetResult(true);
            }
        };

        context.PropertyChanged += handler;
        try
        {
            if (context.RequestCount >= expectedCount)
            {
                return;
            }

            using var timeoutRegistration = cancellationToken.Register(() =>
                completion.TrySetException(new TimeoutException(
                    $"等待模拟器请求计数达到 {expectedCount} 超时，当前计数为 {context.RequestCount}。")));
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            context.PropertyChanged -= handler;
        }
    }

    private static ushort ReadSlot(byte[] frame) => (ushort)(frame[8] | frame[9] << 8);

    private static int GetUnusedUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }
}
