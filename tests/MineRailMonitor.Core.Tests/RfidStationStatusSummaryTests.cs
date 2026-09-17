using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidStationStatusSummaryTests
{
    [Fact]
    public void Waiting_stations_are_not_counted_as_offline_or_as_online_rate_denominator()
    {
        var station560 = CreateStation("RFID-560-01", 0x01, 62001);
        var station620 = CreateStation("RFID-620-01", 0x01, 62002);
        var poller = new RfidStationPoller(
            new[] { station560, station620 },
            200,
            new NoopSender(),
            new FixedTimeProvider());
        var statuses = poller.EndpointStatuses.Values.ToArray();
        var start = new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

        var waiting = RfidStationStatusSummary.Calculate(statuses);

        Assert.Equal(0, waiting.OnlineCount);
        Assert.Equal(0, waiting.OfflineCount);
        Assert.Equal(2, waiting.WaitingCount);
        Assert.Null(waiting.OnlineRatePercentage);

        var station560Endpoint = new IPEndPoint(IPAddress.Loopback, station560.Port);
        Assert.True(poller.RecordSent(station560Endpoint, station560.ProtocolAddress, start));
        var afterSend = RfidStationStatusSummary.Calculate(statuses);

        Assert.Equal(0, afterSend.OnlineCount);
        Assert.Equal(1, afterSend.OfflineCount);
        Assert.Equal(1, afterSend.WaitingCount);

        Assert.True(poller.RecordResponse(
            station560Endpoint,
            station560.ProtocolAddress,
            start.AddMilliseconds(20)));
        var afterResponse = RfidStationStatusSummary.Calculate(statuses);

        Assert.Equal(1, afterResponse.OnlineCount);
        Assert.Equal(0, afterResponse.OfflineCount);
        Assert.Equal(1, afterResponse.WaitingCount);
        Assert.Equal(100d, afterResponse.OnlineRatePercentage);
    }

    private static RfidStationConfig CreateStation(string stationId, byte address, int port) => new()
    {
        StationId = stationId,
        Name = stationId,
        IpAddress = IPAddress.Loopback.ToString(),
        Port = port,
        ProtocolAddress = address,
        Enabled = true
    };

    private sealed class NoopSender : IRfidRequestSender
    {
        public Task SendAsync(byte[] request, IPEndPoint destination, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedTimeProvider : IRfidTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
