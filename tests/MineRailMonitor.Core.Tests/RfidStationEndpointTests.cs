using System.Net;
using System.Net.Sockets;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidStationEndpointTests
{
    [Fact]
    public void Request_builder_uses_protocol_address_as_byte_two()
    {
        var station = CreateStation("RFID-03", "3号站", 0x33, 62303);

        var frame = RfidRequestFrameBuilder.Build(station);

        Assert.Equal(0x33, frame[2]);
    }

    [Fact]
    public void Canonical_endpoint_fields_resolve_to_the_configured_ip_and_port()
    {
        var station = CreateStation("RFID-03", "3号站", 0x33, 62303);

        Assert.True(station.TryResolveEndpoint(out var endpoint));
        Assert.Equal(IPAddress.Loopback, endpoint.Address);
        Assert.Equal(62303, endpoint.Port);
    }

    [Fact]
    public async Task Six_stations_send_read_and_clear_to_their_own_endpoints()
    {
        var stations = CreateSixStations();
        using var cancellation = new CancellationTokenSource();
        var sender = new RecordingSender(cancellation, stopAfter: 12);
        var poller = new RfidStationPoller(
            stations,
            1,
            sender,
            new ImmediateTimeProvider(),
            new SingleClearProvider(stations[2].ProtocolAddress));

        await poller.RunAsync(cancellation.Token);

        Assert.All(stations, station => Assert.Contains(sender.Sends, item =>
            item.Endpoint.Port == station.Port && item.Request[2] == station.ProtocolAddress));
        Assert.Contains(sender.Sends, item =>
            item.Endpoint.Port == stations[2].Port && item.Request[4] == 0x01);
        Assert.DoesNotContain(sender.Sends, item =>
            item.Request[4] == 0x01 && item.Endpoint.Port != stations[2].Port);
    }

    [Fact]
    public async Task Six_loopback_endpoints_receive_real_40_byte_requests_without_crossing()
    {
        var listeners = Enumerable.Range(0, 6)
            .Select(_ => new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            .ToArray();
        using var cancellation = new CancellationTokenSource();
        using var sender = new UdpRequestSender(cancellation, stopAfter: 12);
        var stations = listeners.Select((listener, index) => CreateStation(
            $"RFID-{index + 1:00}",
            $"站{index + 1}",
            (byte)(0x31 + index),
            ((IPEndPoint)listener.Client.LocalEndPoint!).Port)).ToArray();
        var poller = new RfidStationPoller(
            stations,
            1,
            sender,
            new ImmediateTimeProvider(),
            new SingleClearProvider(stations[2].ProtocolAddress));
        var receiveTasks = listeners.Select(listener => ReceiveRequestsAsync(listener, expectedCount: 2)).ToArray();
        var pollerTask = poller.RunAsync(cancellation.Token);

        try
        {
            var receiveAllTask = Task.WhenAll(receiveTasks);
            var completed = await Task.WhenAny(receiveAllTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(receiveAllTask, completed);
            var requests = await receiveAllTask;
            Assert.All(requests, stationRequests => Assert.All(stationRequests, request => Assert.Equal(40, request.Length)));
            Assert.Contains(requests[2], request => request[4] == 0x01);
            Assert.All(requests.Where((_, index) => index != 2), stationRequests =>
                Assert.All(stationRequests, request => Assert.Equal(0x00, request[4])));
        }
        finally
        {
            cancellation.Cancel();
            foreach (var listener in listeners)
            {
                listener.Dispose();
            }

            try
            {
                await pollerTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [Fact]
    public void Response_requires_source_ip_source_port_and_protocol_address()
    {
        var stations = CreateSixStations();
        var poller = new RfidStationPoller(stations, 200, new RecordingSender(), new ImmediateTimeProvider());
        var target = stations[2];
        var now = DateTimeOffset.UtcNow;

        Assert.True(poller.RecordResponse(new IPEndPoint(IPAddress.Loopback, target.Port), target.ProtocolAddress, now));
        Assert.False(poller.RecordResponse(new IPEndPoint(IPAddress.Loopback, target.Port + 1), target.ProtocolAddress, now));
        Assert.False(poller.RecordResponse(new IPEndPoint(IPAddress.Loopback, target.Port), (byte)(target.ProtocolAddress + 1), now));
        Assert.False(poller.RecordResponse(new IPEndPoint(IPAddress.Parse("127.0.0.2"), target.Port), target.ProtocolAddress, now));
        Assert.Equal(1, poller.StationStatuses[target.ProtocolAddress].ResponseCount);
    }

    [Fact]
    public void Poller_allows_same_protocol_address_when_endpoints_are_different()
    {
        var stations = new[]
        {
            CreateStation("RFID-01", "一号站", 0x41, 62301),
            CreateStation("RFID-02", "二号站", 0x41, 62302)
        };

        var poller = new RfidStationPoller(stations, 200, new RecordingSender(), new ImmediateTimeProvider());

        Assert.Equal(2, poller.StationStatuses.Values.Count());
    }

    [Fact]
    public void Runtime_coordinator_allows_same_protocol_address_when_endpoints_are_different()
    {
        var stations = new[]
        {
            CreateStation("RFID-01", "一号站", 0x41, 62301),
            CreateStation("RFID-02", "二号站", 0x41, 62302)
        };

        var coordinator = new RfidRuntimeCoordinator(
            stations,
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            new InMemoryPassageRecordStore());

        Assert.Equal(2, coordinator.States.Values.Count());
    }

    [Fact]
    public void Runtime_routes_same_protocol_responses_by_source_endpoint()
    {
        var stations = new[]
        {
            CreateStation("RFID-01", "一号站", 0x41, 62301),
            CreateStation("RFID-02", "二号站", 0x41, 62302)
        };
        var coordinator = new RfidRuntimeCoordinator(
            stations,
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            new InMemoryPassageRecordStore());
        var now = DateTimeOffset.UtcNow;

        coordinator.ProcessFrame(CreateFrame(stations[0], now, 0x0001));
        coordinator.ProcessFrame(CreateFrame(stations[1], now, 0x0002));

        var states = coordinator.States.Values.ToArray();
        Assert.Equal(2, states.Length);
        Assert.Equal(1, states.Single(state => state.StationId == "RFID-01").DetectedVehicleCount);
        Assert.Equal(1, states.Single(state => state.StationId == "RFID-02").DetectedVehicleCount);
        Assert.Equal((ushort?)0x0001, states.Single(state => state.StationId == "RFID-01").CurrentHeadRfid);
        Assert.Equal((ushort?)0x0002, states.Single(state => state.StationId == "RFID-02").CurrentHeadRfid);
    }

    [Fact]
    public void One_offline_station_does_not_mark_the_other_five_offline()
    {
        var stations = CreateSixStations();
        var now = new DateTimeOffset(2026, 9, 10, 1, 0, 0, TimeSpan.Zero);
        var coordinator = new RfidRuntimeCoordinator(
            stations,
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            new InMemoryPassageRecordStore());

        foreach (var station in stations.Take(5))
        {
            coordinator.ProcessFrame(new RfidStationFrame
            {
                StationAddress = station.ProtocolAddress,
                ReceivedAt = now,
                RawRfidSlots = new ushort[14],
                ValidRfids = Array.Empty<ushort>()
            });
        }

        coordinator.Evaluate(now.AddSeconds(1));

        Assert.All(stations.Take(5), station =>
            Assert.Equal(StationCommunicationState.Online, coordinator.States[station.ProtocolAddress].CommunicationState));
        Assert.Equal(StationCommunicationState.Offline, coordinator.States[stations[5].ProtocolAddress].CommunicationState);
    }

    [Fact]
    public void Runtime_state_uses_configured_station_identity_but_preserves_protocol_address()
    {
        var station = CreateStation("station-west", "西侧读卡站", 0x34, 62304);
        var coordinator = new RfidRuntimeCoordinator(
            new[] { station },
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            new InMemoryPassageRecordStore());

        var state = coordinator.States[0x34];

        Assert.Equal("station-west", state.StationId);
        Assert.Equal("西侧读卡站", state.StationName);
        Assert.Equal(0x34, state.StationAddress);
    }

    private static RfidStationConfig[] CreateSixStations() => Enumerable.Range(1, 6)
        .Select(index => CreateStation($"RFID-{index:00}", $"站{index}", (byte)(0x20 + index), 62300 + index))
        .ToArray();

    private static RfidStationConfig CreateStation(string id, string name, byte protocolAddress, int port) => new()
    {
        StationId = id,
        Name = name,
        IpAddress = "127.0.0.1",
        Port = port,
        ProtocolAddress = protocolAddress,
        Enabled = true,
        Mode = 0x04,
        CommandBytes = new byte[4],
        RequestPayload = new byte[28]
    };

    private static RfidStationFrame CreateFrame(RfidStationConfig station, DateTimeOffset receivedAt, ushort rfid)
    {
        var slots = new ushort[14];
        slots[0] = rfid;
        return new RfidStationFrame
        {
            StationAddress = station.ProtocolAddress,
            ReceivedAt = receivedAt,
            SourceEndpoint = new IPEndPoint(IPAddress.Parse(station.IpAddress), station.Port),
            RawRfidSlots = slots,
            ValidRfids = new[] { rfid },
            ActualNonZeroSlotCount = 1,
            ReportedCardCount = 1
        };
    }

    private sealed class RecordingSender : IRfidRequestSender
    {
        private readonly CancellationTokenSource? _cancellation;
        private readonly int _stopAfter;

        public RecordingSender(CancellationTokenSource? cancellation = null, int stopAfter = int.MaxValue)
        {
            _cancellation = cancellation;
            _stopAfter = stopAfter;
        }

        public List<(byte[] Request, IPEndPoint Endpoint)> Sends { get; } = new();

        public Task SendAsync(byte[] request, IPEndPoint destination, CancellationToken cancellationToken)
        {
            Sends.Add((request.ToArray(), new IPEndPoint(destination.Address, destination.Port)));
            if (Sends.Count >= _stopAfter)
            {
                _cancellation?.Cancel();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class UdpRequestSender : IRfidRequestSender, IDisposable
    {
        private readonly UdpClient _client = new();
        private readonly CancellationTokenSource _cancellation;
        private readonly int _stopAfter;
        private int _sendCount;

        public UdpRequestSender(CancellationTokenSource cancellation, int stopAfter)
        {
            _cancellation = cancellation;
            _stopAfter = stopAfter;
        }

        public async Task SendAsync(byte[] request, IPEndPoint destination, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _client.SendAsync(request, request.Length, destination).ConfigureAwait(false);
            if (Interlocked.Increment(ref _sendCount) >= _stopAfter)
            {
                _cancellation.Cancel();
            }
        }

        public void Dispose() => _client.Dispose();
    }

    private static async Task<byte[][]> ReceiveRequestsAsync(UdpClient listener, int expectedCount)
    {
        var requests = new List<byte[]>();
        for (var index = 0; index < expectedCount; index++)
        {
            var received = await listener.ReceiveAsync().ConfigureAwait(false);
            requests.Add(received.Buffer);
        }

        return requests.ToArray();
    }

    private sealed class SingleClearProvider : IRfidPollCommandProvider
    {
        private readonly byte _target;
        private bool _pending = true;

        public SingleClearProvider(byte target) => _target = target;

        public RfidPollCommand GetCommand(byte stationAddress) =>
            stationAddress == _target && _pending ? RfidPollCommand.Clear : RfidPollCommand.Read;

        public void MarkCommandSent(byte stationAddress, RfidPollCommand command, DateTimeOffset sentAt)
        {
            if (stationAddress == _target && command == RfidPollCommand.Clear)
            {
                _pending = false;
            }
        }
    }

    private sealed class ImmediateTimeProvider : IRfidTimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
