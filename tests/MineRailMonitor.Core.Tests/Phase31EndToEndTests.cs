using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Simulator.Communication;
using MineRailMonitor.Simulator.Models;
using MineRailMonitor.Simulator.Protocol;

namespace MineRailMonitor.Core.Tests;

public sealed class Phase31EndToEndTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task Poller_simulator_parser_and_runtime_complete_clear_and_wait_for_empty()
    {
        var store = new InMemoryPassageRecordStore();
        var runtime = new RfidRuntimeCoordinator(
            new[] { (byte)0x01 },
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            store,
            new RfidRuntimePolicy(TimeSpan.FromMinutes(2)));
        var station = new RfidStationConfig
        {
            Address = 0x01,
            DestinationEndpoint = new IPEndPoint(IPAddress.Loopback, 62001),
            CommandBytes = new byte[4],
            RequestPayload = new byte[28]
        };
        var simulatorStation = new SimulatorStation
        {
            Address = 0x01,
            Slots = new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019, 0x001A, 0, 0, 0 }
        };
        var responder = new RfidSimulatorResponder(new[] { simulatorStation }, emptySlotValue: 0);
        var parser = new RfidFrameParser();
        using var cancellation = new CancellationTokenSource();
        var sender = new LoopbackSender(
            cancellation,
            stopAfter: 4,
            responder,
            parser,
            frame => runtime.ProcessFrame(frame));
        var poller = new RfidStationPoller(
            new[] { station },
            200,
            sender,
            new ControllableTimeProvider(Start),
            runtime);

        await poller.RunAsync(cancellation.Token);

        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x00 }, sender.Requests.Select(request => request[4]).ToArray());
        Assert.Single(store.Records);
        Assert.Equal(PassageOutcome.Completed, store.Records[0].Outcome);
        Assert.Equal(PassageLifecycleState.Idle, runtime.States[0x01].LifecycleState);
        Assert.Equal(1, runtime.States[0x01].ClearAttempts);
        Assert.Equal((ushort)0x0001, store.Records[0].HeadRfid);
    }

    [Fact]
    public async Task Udp_loopback_carries_read_clear_and_empty_confirmations_to_idle()
    {
        var store = new InMemoryPassageRecordStore();
        var runtime = new RfidRuntimeCoordinator(
            new[] { (byte)0x01 },
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            store,
            new RfidRuntimePolicy(TimeSpan.FromSeconds(10)));
        var simulatorStation = new SimulatorStation
        {
            Address = 0x01,
            Slots = new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019, 0x001A, 0, 0, 0 }
        };
        var responderLogic = new RfidSimulatorResponder(new[] { simulatorStation }, emptySlotValue: 0);
        using var responder = new SimulatorUdpResponder(IPAddress.Loopback, 0);
        using var receiver = new RfidUdpTransport(IPAddress.Loopback, 0);
        using var responderCancellation = new CancellationTokenSource();
        using var pollerCancellation = new CancellationTokenSource();
        var parser = new RfidFrameParser();
        var idleReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.DatagramReceived += (_, datagram) =>
        {
            if (!datagram.IsValid || !parser.TryParse(datagram.Data, datagram.RemoteEndPoint, datagram.ReceivedAt, out var frame) || frame is null)
            {
                return;
            }

            runtime.ProcessFrame(frame);
            if (runtime.States[0x01].LifecycleState == PassageLifecycleState.Idle && store.Records.Count == 1)
            {
                idleReached.TrySetResult(true);
            }
        };

        var responderTask = responder.RunAsync(responderLogic.CreateResponse, responderCancellation.Token);
        var receiverTask = receiver.StartAsync(CancellationToken.None);
        var station = new RfidStationConfig
        {
            Address = 0x01,
            DestinationEndpoint = responder.LocalEndPoint,
            CommandBytes = new byte[4],
            RequestPayload = new byte[28]
        };
        var poller = new RfidStationPoller(
            new[] { station },
            200,
            receiver,
            new SystemRfidTimeProvider(),
            runtime);
        var pollerTask = poller.RunAsync(pollerCancellation.Token);

        try
        {
            await WithTimeoutAsync(idleReached.Task, TimeSpan.FromSeconds(5));
            Assert.Single(store.Records);
            Assert.Equal(PassageLifecycleState.Idle, runtime.States[0x01].LifecycleState);
        }
        finally
        {
            pollerCancellation.Cancel();
            responderCancellation.Cancel();
            receiver.Stop();
            try { await pollerTask; } catch (OperationCanceledException) { }
            try { await receiverTask; } catch (OperationCanceledException) { }
            try { await responderTask; } catch (OperationCanceledException) { }
        }
    }

    private static async Task WithTimeoutAsync(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
        {
            throw new TimeoutException("UDP loopback lifecycle did not reach Idle in time.");
        }

        await task;
    }

    private sealed class LoopbackSender : IRfidRequestSender
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly int _stopAfter;
        private readonly RfidSimulatorResponder _responder;
        private readonly RfidFrameParser _parser;
        private readonly Action<RfidStationFrame> _frameSink;

        public LoopbackSender(
            CancellationTokenSource cancellation,
            int stopAfter,
            RfidSimulatorResponder responder,
            RfidFrameParser parser,
            Action<RfidStationFrame> frameSink)
        {
            _cancellation = cancellation;
            _stopAfter = stopAfter;
            _responder = responder;
            _parser = parser;
            _frameSink = frameSink;
        }

        public List<byte[]> Requests { get; } = new();

        public Task SendAsync(byte[] request, IPEndPoint destination, CancellationToken cancellationToken)
        {
            Requests.Add(request.ToArray());
            var response = _responder.CreateResponse(request);
            if (response is not null && _parser.TryParse(response, destination, Start, out var frame) && frame is not null)
            {
                _frameSink(frame);
            }

            if (Requests.Count == _stopAfter)
            {
                _cancellation.Cancel();
            }
            return Task.CompletedTask;
        }
    }

    private sealed class ControllableTimeProvider : IRfidTimeProvider
    {
        public ControllableTimeProvider(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UtcNow += delay;
            return Task.CompletedTask;
        }
    }
}
