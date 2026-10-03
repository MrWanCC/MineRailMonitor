using System.Net;
using System.Reflection;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidStationStatusDisplayFormatterTests
{
    [Fact]
    public void Enabled_station_with_received_online_status_is_online()
    {
        var (station, poller) = CreateStationAndPoller();
        var status = poller.EndpointStatuses.Values.Single();
        var receivedAt = new DateTimeOffset(2026, 10, 3, 4, 0, 0, TimeSpan.Zero);
        Assert.True(poller.RecordResponse(Endpoint(station), station.ProtocolAddress, receivedAt));

        Assert.Equal("在线", RfidStationStatusDisplayFormatter.FormatStatus(station, status));
        Assert.NotEqual("—", RfidStationStatusDisplayFormatter.FormatLastReceivedTime(status.LastReceivedAt));
    }

    [Fact]
    public void Enabled_station_with_received_but_offline_status_is_offline()
    {
        var (station, poller) = CreateStationAndPoller();
        var status = poller.EndpointStatuses.Values.Single();
        Assert.True(poller.RecordResponse(Endpoint(station), station.ProtocolAddress, DateTimeOffset.UtcNow));
        typeof(RfidStationPollingStatus)
            .GetMethod("SetOnlineState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(status, new object[] { false });

        Assert.Equal("离线", RfidStationStatusDisplayFormatter.FormatStatus(station, status));
    }

    [Fact]
    public void New_valid_response_after_offline_state_returns_station_to_online()
    {
        var (station, poller) = CreateStationAndPoller();
        var status = poller.EndpointStatuses.Values.Single();
        var firstResponseAt = new DateTimeOffset(2026, 10, 3, 4, 0, 0, TimeSpan.Zero);
        var recoveredAt = firstResponseAt.AddMinutes(1);
        Assert.True(poller.RecordResponse(Endpoint(station), station.ProtocolAddress, firstResponseAt));
        typeof(RfidStationPollingStatus)
            .GetMethod("SetOnlineState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(status, new object[] { false });

        Assert.Equal("离线", RfidStationStatusDisplayFormatter.FormatStatus(station, status));
        Assert.True(poller.RecordResponse(Endpoint(station), station.ProtocolAddress, recoveredAt));

        Assert.Equal("在线", RfidStationStatusDisplayFormatter.FormatStatus(station, status));
        Assert.Equal(recoveredAt, status.LastReceivedAt);
    }

    [Fact]
    public void Runtime_offline_timeout_is_reflected_in_display_and_recovers_on_next_response()
    {
        var (station, poller) = CreateStationAndPoller();
        var coordinator = new RfidRuntimeCoordinator(
            new[] { station },
            new RfidSettings { ExpectedVehicleCount = 7 },
            new InMemoryPassageRecordStore(),
            new RfidRuntimePolicy(TimeSpan.FromSeconds(5)));
        var firstResponseAt = new DateTimeOffset(2026, 10, 3, 4, 0, 0, TimeSpan.Zero);
        var recoveredAt = firstResponseAt.AddSeconds(6);

        Assert.True(poller.RecordResponse(Endpoint(station), station.ProtocolAddress, firstResponseAt));
        coordinator.ProcessFrame(CreateEmptyFrame(station, firstResponseAt));
        poller.SynchronizeOnlineStates(coordinator.EndpointStates);
        Assert.Equal("在线", RfidStationStatusDisplayFormatter.FormatStatus(station, poller.EndpointStatuses.Values.Single()));

        coordinator.Evaluate(firstResponseAt.AddSeconds(5));
        poller.SynchronizeOnlineStates(coordinator.EndpointStates);
        Assert.Equal("离线", RfidStationStatusDisplayFormatter.FormatStatus(station, poller.EndpointStatuses.Values.Single()));

        Assert.True(poller.RecordResponse(Endpoint(station), station.ProtocolAddress, recoveredAt));
        coordinator.ProcessFrame(CreateEmptyFrame(station, recoveredAt));
        poller.SynchronizeOnlineStates(coordinator.EndpointStates);
        Assert.Equal("在线", RfidStationStatusDisplayFormatter.FormatStatus(station, poller.EndpointStatuses.Values.Single()));
        Assert.Equal(recoveredAt, poller.EndpointStatuses.Values.Single().LastReceivedAt);
    }

    [Fact]
    public void Enabled_station_without_received_status_is_waiting_even_if_state_was_set_online()
    {
        var (station, poller) = CreateStationAndPoller();
        var status = poller.EndpointStatuses.Values.Single();
        typeof(RfidStationPollingStatus)
            .GetMethod("SetOnlineState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(status, new object[] { true });

        Assert.Equal("等待通信", RfidStationStatusDisplayFormatter.FormatStatus(station, status));
        Assert.Equal("—", RfidStationStatusDisplayFormatter.FormatLastReceivedTime(status.LastReceivedAt));
    }

    [Fact]
    public void Disabled_station_is_stopped_regardless_of_polling_status()
    {
        var (station, poller) = CreateStationAndPoller();
        station.Enabled = false;

        Assert.Equal("已停用", RfidStationStatusDisplayFormatter.FormatStatus(station, poller.EndpointStatuses.Values.Single()));
        Assert.Equal("已停用", RfidStationStatusDisplayFormatter.FormatStatus(station, null));
    }

    [Fact]
    public void Runtime_endpoint_mismatch_is_detected_against_communication_configuration()
    {
        var (configuredStation, poller) = CreateStationAndPoller();
        var changedConfiguration = CreateStation();
        changedConfiguration.IpAddress = "127.0.0.2";
        var status = poller.EndpointStatuses.Values.Single();
        Assert.True(poller.RecordResponse(Endpoint(configuredStation), configuredStation.ProtocolAddress, DateTimeOffset.UtcNow));

        Assert.True(RfidStationStatusDisplayFormatter.HasEndpointMismatch(changedConfiguration, status));
        Assert.False(RfidStationStatusDisplayFormatter.HasEndpointMismatch(configuredStation, status));
    }

    private static (RfidStationConfig Station, RfidStationPoller Poller) CreateStationAndPoller()
    {
        var station = CreateStation();
        return (station, new RfidStationPoller(new[] { station }, 200, new NoopSender(), new FixedTimeProvider()));
    }

    private static RfidStationConfig CreateStation() => new()
    {
        StationId = "RFID-560-01",
        Name = "560-01",
        YardId = "560",
        IpAddress = "127.0.0.1",
        Port = 62001,
        ProtocolAddress = 1,
        Enabled = true
    };

    private static IPEndPoint Endpoint(RfidStationConfig station) =>
        new(IPAddress.Parse(station.IpAddress), station.Port);

    private static RfidStationFrame CreateEmptyFrame(RfidStationConfig station, DateTimeOffset receivedAt)
    {
        station.TryResolveEndpoint(out var endpoint);
        return new RfidStationFrame
        {
            StationAddress = station.ProtocolAddress,
            ReceivedAt = receivedAt,
            RawRfidSlots = new ushort[14],
            ValidRfids = Array.Empty<ushort>(),
            SourceEndpoint = endpoint
        };
    }

    private sealed class NoopSender : IRfidRequestSender
    {
        public Task SendAsync(byte[] request, IPEndPoint destination, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider : IRfidTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 10, 3, 4, 0, 0, TimeSpan.Zero);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
