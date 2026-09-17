using System.Net;
using System.Text;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidStationPollerTests
{
    [Fact]
    public void RequestBuilder_PreservesTemplateFieldsAndCalculatesModbusCrc()
    {
        var station = CreateStation(0x03);
        station.CommandBytes = new byte[] { 1, 2, 3, 4 };
        station.RequestPayload = Enumerable.Range(8, 28).Select(value => (byte)value).ToArray();

        var frame = RfidRequestFrameBuilder.Build(station);

        Assert.Equal(40, frame.Length);
        Assert.Equal(new byte[] { 0xB0, 0xB0, 0x03, 0x04 }, frame.Take(4));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, frame.Skip(4).Take(4));
        Assert.Equal(Enumerable.Range(8, 28).Select(value => (byte)value), frame.Skip(8).Take(28));
        var crc = ModbusCrc16.Compute(frame, 0, 36);
        Assert.Equal(ModbusCrc16.LowByte(crc), frame[36]);
        Assert.Equal(ModbusCrc16.HighByte(crc), frame[37]);
        Assert.Equal(new byte[] { 0xAA, 0xAA }, frame.Skip(38));
    }

    [Theory]
    [InlineData(0x00, 0x11, 0x69)]
    [InlineData(0x01, 0x10, 0xC5)]
    public void RequestBuilder_RealAddress01ReadAndClearCrcFixtures(byte command, byte expectedLow, byte expectedHigh)
    {
        var station = CreateStation(0x01);
        station.CommandBytes = new[] { command, (byte)0, (byte)0, (byte)0 };

        var frame = RfidRequestFrameBuilder.Build(station);

        Assert.Equal(expectedLow, frame[36]);
        Assert.Equal(expectedHigh, frame[37]);
    }

    [Fact]
    public void ModbusCrc16_UsesLowByteFirstWireOrder()
    {
        var crc = ModbusCrc16.Compute(Encoding.ASCII.GetBytes("123456789"));

        Assert.Equal((ushort)0x4B37, crc);
        Assert.Equal((byte)0x37, ModbusCrc16.LowByte(crc));
        Assert.Equal((byte)0x4B, ModbusCrc16.HighByte(crc));
    }

    [Fact]
    public async Task Poller_UsesRoundRobinOrderAndConfiguredIntervalWithoutWaitingForResponses()
    {
        using var cancellation = new CancellationTokenSource();
        var sender = new RecordingSender(cancellation, stopAfter: 7);
        var time = new ControllableTimeProvider(new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(8)));
        var stations = Enumerable.Range(1, 6).Select(index => CreateStation((byte)index)).ToArray();
        var poller = new RfidStationPoller(stations, 200, sender, time);

        await poller.RunAsync(cancellation.Token);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 1 }, sender.Addresses);
        Assert.Equal(6, time.Delays.Count);
        Assert.All(time.Delays, delay => Assert.Equal(TimeSpan.FromMilliseconds(200), delay));
        Assert.Equal(2, poller.StationStatuses[0x01].RequestCount);
        Assert.Equal(1, poller.StationStatuses[0x06].RequestCount);
        Assert.Null(poller.StationStatuses[0x01].LastResponseAt);
    }

    [Fact]
    public async Task Poller_sends_a_pending_clear_only_when_that_station_turn_arrives()
    {
        using var cancellation = new CancellationTokenSource();
        var sender = new RecordingSender(cancellation, stopAfter: 3);
        var time = new ControllableTimeProvider(new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(8)));
        var provider = new SingleClearProvider(0x02);
        var stations = new[] { CreateStation(0x01), CreateStation(0x02) };
        var poller = new RfidStationPoller(stations, 200, sender, time, provider);

        await poller.RunAsync(cancellation.Token);

        Assert.Equal(new byte[] { 0x01, 0x02, 0x01 }, sender.Addresses);
        Assert.Equal(0x00, sender.Requests[0][4]);
        Assert.Equal(0x01, sender.Requests[1][4]);
        Assert.Equal(0x00, sender.Requests[2][4]);
        Assert.Contains((byte)0x02, provider.MarkedAddresses);
    }

    [Fact]
    public async Task Poller_continues_round_robin_when_one_station_send_fails()
    {
        using var cancellation = new CancellationTokenSource();
        var sender = new FailingOnceSender(cancellation, stopAfter: 3);
        var time = new ControllableTimeProvider(new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(8)));
        var stations = new[] { CreateStation(0x01), CreateStation(0x02) };
        var poller = new RfidStationPoller(stations, 200, sender, time);

        await poller.RunAsync(cancellation.Token);

        Assert.Equal(new byte[] { 0x01, 0x02, 0x01 }, sender.Addresses);
        Assert.NotNull(poller.StationStatuses[0x01].LastErrorAt);
        Assert.Equal(1, poller.StationStatuses[0x02].RequestCount);
    }

    [Fact]
    public async Task Poller_records_send_receive_latency_and_clears_timeout_diagnostics()
    {
        using var cancellation = new CancellationTokenSource();
        var sentAt = new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);
        var sender = new RecordingSender(cancellation, stopAfter: 1);
        var time = new ControllableTimeProvider(sentAt);
        var station = CreateStation(0x01);
        var poller = new RfidStationPoller(new[] { station }, 200, sender, time);
        var status = poller.EndpointStatuses.Values.Single();

        await poller.RunAsync(cancellation.Token);
        poller.EvaluateTimeouts(sentAt.AddSeconds(5), TimeSpan.FromSeconds(5));
        Assert.Equal(1, status.SentCount);
        Assert.Equal(sentAt, status.LastSentAt);
        Assert.Equal(1, status.TimeoutCount);
        Assert.Equal("设备响应超时", status.LastError);

        var receivedAt = sentAt.AddMilliseconds(125);
        Assert.True(poller.RecordResponse(station.DestinationEndpoint!, station.ProtocolAddress, receivedAt));

        Assert.Equal(1, status.ReceivedCount);
        Assert.Equal(receivedAt, status.LastReceivedAt);
        Assert.Equal(125, status.LastResponseMilliseconds);
        Assert.Equal(0, status.ConsecutiveTimeoutCount);
        Assert.Null(status.LastError);
        Assert.True(status.IsOnline);
    }

    [Fact]
    public async Task Poller_counts_one_timeout_once_for_the_same_unanswered_request()
    {
        using var cancellation = new CancellationTokenSource();
        var sentAt = new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);
        var sender = new RecordingSender(cancellation, stopAfter: 1);
        var time = new ControllableTimeProvider(sentAt);
        var station = CreateStation(0x01);
        var poller = new RfidStationPoller(new[] { station }, 200, sender, time);
        var status = poller.EndpointStatuses.Values.Single();

        await poller.RunAsync(cancellation.Token);
        poller.EvaluateTimeouts(sentAt.AddSeconds(5), TimeSpan.FromSeconds(5));
        poller.EvaluateTimeouts(sentAt.AddSeconds(5), TimeSpan.FromSeconds(5));
        poller.EvaluateTimeouts(sentAt.AddSeconds(5).AddMilliseconds(500), TimeSpan.FromSeconds(5));

        Assert.Equal(1, status.TimeoutCount);
        Assert.Equal(1, status.ConsecutiveTimeoutCount);
    }

    [Fact]
    public void Poller_keeps_diagnostics_isolated_for_same_protocol_address_on_different_endpoints()
    {
        using var cancellation = new CancellationTokenSource();
        var first = CreateStation("RFID-560-01", "560 一号站", 0x01, 62001);
        var second = CreateStation("RFID-620-01", "620 一号站", 0x01, 62002);
        var poller = new RfidStationPoller(
            new[] { first, second },
            200,
            new RecordingSender(cancellation, stopAfter: int.MaxValue),
            new ControllableTimeProvider(DateTimeOffset.UtcNow));
        var now = DateTimeOffset.UtcNow;
        var firstEndpoint = new IPEndPoint(IPAddress.Loopback, 62001);
        var secondEndpoint = new IPEndPoint(IPAddress.Loopback, 62002);

        Assert.True(poller.RecordSent(firstEndpoint, first.ProtocolAddress, now));
        Assert.True(poller.RecordSent(secondEndpoint, second.ProtocolAddress, now));
        Assert.True(poller.RecordResponse(firstEndpoint, first.ProtocolAddress, now.AddMilliseconds(50)));

        var firstStatus = poller.EndpointStatuses.Values.Single(status => status.EndpointKey!.Value.Endpoint.Port == 62001);
        var secondStatus = poller.EndpointStatuses.Values.Single(status => status.EndpointKey!.Value.Endpoint.Port == 62002);
        Assert.Equal(1, firstStatus.SentCount);
        Assert.Equal(1, firstStatus.ReceivedCount);
        Assert.Equal(50, firstStatus.LastResponseMilliseconds);
        Assert.Equal(1, secondStatus.SentCount);
        Assert.Equal(0, secondStatus.ReceivedCount);
        Assert.Null(secondStatus.LastResponseMilliseconds);
    }

    [Fact]
    public void Poller_uses_the_latest_send_for_that_station_when_requests_overlap()
    {
        var station = CreateStation("RFID-01", "一号站", 0x01, 62001);
        var poller = new RfidStationPoller(
            new[] { station },
            200,
            new RecordingSender(new CancellationTokenSource(), stopAfter: int.MaxValue),
            new ControllableTimeProvider(DateTimeOffset.UtcNow));
        var endpoint = new IPEndPoint(IPAddress.Loopback, 62001);
        var firstSentAt = DateTimeOffset.UtcNow;
        var secondSentAt = firstSentAt.AddMilliseconds(200);

        Assert.True(poller.RecordSent(endpoint, station.ProtocolAddress, firstSentAt));
        Assert.True(poller.RecordSent(endpoint, station.ProtocolAddress, secondSentAt));
        Assert.True(poller.RecordResponse(endpoint, station.ProtocolAddress, secondSentAt.AddMilliseconds(35)));

        Assert.Equal(35, poller.EndpointStatuses.Values.Single().LastResponseMilliseconds);
    }

    [Fact]
    public void Poller_online_state_follows_the_existing_runtime_coordinator_rule()
    {
        var station = CreateStation("RFID-01", "一号站", 0x01, 62001);
        var poller = new RfidStationPoller(
            new[] { station },
            200,
            new RecordingSender(new CancellationTokenSource(), stopAfter: int.MaxValue),
            new ControllableTimeProvider(DateTimeOffset.UtcNow));
        var coordinator = new RfidRuntimeCoordinator(
            new[] { station },
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            new InMemoryPassageRecordStore());
        var endpoint = new IPEndPoint(IPAddress.Loopback, 62001);
        var now = DateTimeOffset.UtcNow;

        Assert.True(poller.RecordSent(endpoint, station.ProtocolAddress, now));
        Assert.True(poller.RecordResponse(endpoint, station.ProtocolAddress, now.AddMilliseconds(20)));
        coordinator.ProcessFrame(new RfidStationFrame
        {
            StationAddress = station.ProtocolAddress,
            SourceEndpoint = endpoint,
            ReceivedAt = now.AddMilliseconds(20),
            RawRfidSlots = new ushort[14],
            ValidRfids = Array.Empty<ushort>()
        });

        poller.SynchronizeOnlineStates(coordinator.EndpointStates);
        Assert.True(poller.EndpointStatuses.Values.Single().IsOnline);

        coordinator.Evaluate(now.AddSeconds(6));
        poller.SynchronizeOnlineStates(coordinator.EndpointStates);
        Assert.False(poller.EndpointStatuses.Values.Single().IsOnline);
    }

    [Theory]
    [InlineData(0, 11, 30)]
    [InlineData(200, 0, 30)]
    [InlineData(200, 15, 30)]
    [InlineData(200, 11, 0)]
    public void Settings_RejectInvalidCoreValues(int interval, int vehicles, int timeout)
    {
        var settings = new RfidSettings
        {
            PollIntervalMs = interval,
            ExpectedVehicleCount = vehicles,
            InterVehicleTimeoutSeconds = timeout
        };

        Assert.NotEmpty(settings.Validate());
    }

    private static RfidStationConfig CreateStation(byte address) => new()
    {
        Address = address,
        Enabled = true,
        DestinationEndpoint = new IPEndPoint(IPAddress.Loopback, 62001),
        CommandBytes = new byte[4],
        RequestPayload = new byte[28]
    };

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

    private sealed class RecordingSender : IRfidRequestSender
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly int _stopAfter;

        public RecordingSender(CancellationTokenSource cancellation, int stopAfter)
        {
            _cancellation = cancellation;
            _stopAfter = stopAfter;
        }

        public List<byte> Addresses { get; } = new();

        public List<byte[]> Requests { get; } = new();

        public Task SendAsync(byte[] request, IPEndPoint destination, CancellationToken cancellationToken)
        {
            Addresses.Add(request[2]);
            Requests.Add(request.ToArray());
            if (Addresses.Count == _stopAfter)
            {
                _cancellation.Cancel();
            }
            return Task.CompletedTask;
        }
    }

    private sealed class SingleClearProvider : IRfidPollCommandProvider
    {
        private readonly byte _stationAddress;
        private bool _pending = true;

        public SingleClearProvider(byte stationAddress) => _stationAddress = stationAddress;

        public List<byte> MarkedAddresses { get; } = new();

        public RfidPollCommand GetCommand(byte stationAddress) =>
            stationAddress == _stationAddress && _pending ? RfidPollCommand.Clear : RfidPollCommand.Read;

        public void MarkCommandSent(byte stationAddress, RfidPollCommand command, DateTimeOffset sentAt)
        {
            if (stationAddress == _stationAddress && command == RfidPollCommand.Clear)
            {
                _pending = false;
                MarkedAddresses.Add(stationAddress);
            }
        }
    }

    private sealed class FailingOnceSender : IRfidRequestSender
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly int _stopAfter;
        private bool _failed;

        public FailingOnceSender(CancellationTokenSource cancellation, int stopAfter)
        {
            _cancellation = cancellation;
            _stopAfter = stopAfter;
        }

        public List<byte> Addresses { get; } = new();

        public Task SendAsync(byte[] request, IPEndPoint destination, CancellationToken cancellationToken)
        {
            Addresses.Add(request[2]);
            if (!_failed)
            {
                _failed = true;
                throw new InvalidOperationException("模拟发送失败");
            }

            if (Addresses.Count == _stopAfter)
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

        public List<TimeSpan> Delays { get; } = new();

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            UtcNow += delay;
            return Task.CompletedTask;
        }
    }
}
