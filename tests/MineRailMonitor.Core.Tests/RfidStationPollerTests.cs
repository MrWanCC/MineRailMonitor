using System.Net;
using System.Text;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;

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
