using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class YardRfidStationResolverTests
{
    [Fact]
    public void Resolves_explicit_560_bindings_only()
    {
        var resolver = CreateResolver(out _);

        Assert.Equal(new[] { "RFID-01", "RFID-02", "RFID-03", "RFID-04" },
            resolver.Resolve("560").RfidStationIds);
    }

    [Fact]
    public void Resolves_explicit_620_bindings_only()
    {
        var resolver = CreateResolver(out _);

        Assert.Equal(new[] { "RFID-05", "RFID-06", "RFID-07" },
            resolver.Resolve("620").RfidStationIds);
    }

    [Fact]
    public void ResolveAll_returns_all_configured_rfid_stations()
    {
        var resolver = CreateResolver(out _);

        Assert.Equal(8, resolver.ResolveAll().RfidStations.Count);
        Assert.Equal(8, resolver.ResolveAll().RfidStationIds.Count);
    }

    [Fact]
    public void Unbound_station_is_not_assigned_by_protocol_address()
    {
        var resolver = CreateResolver(out var stations);
        var unbound = stations.Single(item => item.StationId == "RFID-08");

        Assert.DoesNotContain("RFID-08", resolver.Resolve("560").RfidStationIds);
        Assert.Contains(unbound, resolver.ResolveUnbound().RfidStations);
    }

    [Fact]
    public void Explicit_station_ownership_adds_an_unbound_station_to_the_selected_yard()
    {
        var resolver = CreateResolver(out var stations);
        var owned = stations.Single(item => item.StationId == "RFID-08");
        owned.YardId = "560";

        var scope = resolver.Resolve("560");

        Assert.Contains("RFID-08", scope.RfidStationIds);
        Assert.Contains(owned, scope.UnmappedStations);
        Assert.DoesNotContain(owned, resolver.ResolveUnbound().RfidStations);
    }

    [Fact]
    public void Missing_explicit_reference_is_reported()
    {
        var resolver = CreateResolver(out _);

        var scope = resolver.Resolve("560");

        var diagnostic = Assert.Single(scope.MissingConfigurations);
        Assert.Equal("RFID-MISSING", diagnostic.RfidStationId);
    }

    [Fact]
    public void Duplicate_explicit_reference_is_deduplicated_and_reported()
    {
        var resolver = CreateResolver(out _);

        var scope = resolver.Resolve("560");

        Assert.Equal(1, scope.RfidStationIds.Count(item => item == "RFID-01"));
        var diagnostic = Assert.Single(scope.DuplicateBindings);
        Assert.Equal("RFID-01", diagnostic.RfidStationId);
    }

    [Fact]
    public void ResolveAll_retains_duplicate_binding_diagnostics_across_yards()
    {
        var stations = new[]
        {
            CreateRfidStation("RFID-01", 10001, 1)
        };
        var yards = new[]
        {
            CreateYard("560", "RFID-01"),
            CreateYard("620", "RFID-01")
        };
        yards[0].Devices[0].Id = "point-a";
        yards[1].Devices[0].Id = "point-b";
        var resolver = new YardRfidStationResolver(yards, stations);

        var diagnostics = resolver.ResolveAll().DuplicateBindings;

        Assert.Equal(2, diagnostics.Count);
        Assert.Contains(diagnostics, item => item.YardId == "560" && item.DeviceId == "point-a");
        Assert.Contains(diagnostics, item => item.YardId == "620" && item.DeviceId == "point-b");
    }

    [Fact]
    public void Disabled_station_remains_in_its_yard_scope_and_is_marked_disabled()
    {
        var resolver = CreateResolver(out var stations);
        stations.Single(item => item.StationId == "RFID-02").Enabled = false;

        var scope = resolver.Resolve("560");

        Assert.Contains("RFID-02", scope.RfidStationIds);
        Assert.Contains(scope.RfidStations, item => item.StationId == "RFID-02");
        Assert.Contains(scope.DisabledStations, item => item.StationId == "RFID-02");
    }

    [Fact]
    public void Current_yard_context_switches_between_yards_and_global()
    {
        var context = new CurrentYardContext();

        Assert.True(context.IsGlobalOverview);
        context.SelectYard("560");
        Assert.Equal("560", context.CurrentYardId);
        Assert.False(context.IsGlobalOverview);
        context.SelectYard("620");
        Assert.Equal("620", context.CurrentYardId);
        context.SelectGlobal();
        Assert.True(context.IsGlobalOverview);
        Assert.Null(context.CurrentYardId);
    }

    [Fact]
    public void Yard_switch_does_not_mutate_poller_or_runtime_station_sets()
    {
        var resolver = CreateResolver(out var stations);
        var poller = new RfidStationPoller(stations, 200, new NoopRequestSender(), new ImmediateTimeProvider());
        var runtime = new RfidRuntimeCoordinator(
            stations,
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            new InMemoryPassageRecordStore());
        var context = new CurrentYardContext();

        context.SelectYard("560");
        context.SelectYard("620");

        Assert.Equal(8, resolver.ResolveAll().RfidStations.Count);
        Assert.Equal(8, poller.EndpointStatuses.Count);
        Assert.Equal(8, runtime.States.Count);
    }

    [Fact]
    public void Runtime_for_another_yard_remains_available_after_switching_display_scope()
    {
        var resolver = CreateResolver(out var stations);
        var runtime = new RfidRuntimeCoordinator(
            stations,
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            new InMemoryPassageRecordStore());
        var rfidStation = stations.Single(item => item.StationId == "RFID-06");
        var slots = new ushort[14];
        slots[0] = 0x0601;

        runtime.ProcessFrame(new RfidStationFrame
        {
            StationAddress = rfidStation.ProtocolAddress,
            SourceEndpoint = new IPEndPoint(IPAddress.Loopback, rfidStation.Port),
            ReceivedAt = DateTimeOffset.UtcNow,
            RawRfidSlots = slots,
            ValidRfids = new[] { (ushort)0x0601 },
            ActualNonZeroSlotCount = 1,
            ReportedCardCount = 1
        });

        Assert.Equal(1, runtime.States.Values.Single(item => item.StationId == "RFID-06").DetectedVehicleCount);
        Assert.Contains("RFID-06", resolver.Resolve("620").RfidStationIds);
        Assert.DoesNotContain("RFID-06", resolver.Resolve("560").RfidStationIds);
    }

    [Fact]
    public void Same_protocol_address_different_endpoints_still_follow_explicit_yard_binding()
    {
        var first = CreateRfidStation("RFID-A", 63001, 0x01);
        var second = CreateRfidStation("RFID-B", 63002, 0x01);
        first.YardId = "A";
        second.YardId = "B";
        var resolver = new YardRfidStationResolver(
            new[]
            {
                CreateYard("A", "RFID-A"),
                CreateYard("B", "RFID-B")
            },
            new[] { first, second });

        Assert.Equal(new[] { "RFID-A" }, resolver.Resolve("A").RfidStationIds);
        Assert.Equal(new[] { "RFID-B" }, resolver.Resolve("B").RfidStationIds);
    }

    [Fact]
    public void Map_only_reference_does_not_add_unassigned_station_to_yard_communication_scope()
    {
        var station = CreateRfidStation("RFID-UNASSIGNED", 63001, 0x01);
        var yard = CreateYard("560", "RFID-UNASSIGNED");
        var resolver = new YardRfidStationResolver(new[] { yard }, new[] { station });

        Assert.DoesNotContain("RFID-UNASSIGNED", resolver.Resolve("560").RfidStationIds);
        Assert.Contains(station, resolver.ResolveUnbound().RfidStations);
    }

    private static YardRfidStationResolver CreateResolver(out RfidStationConfig[] stations)
    {
        stations = Enumerable.Range(1, 8)
            .Select(index => CreateRfidStation($"RFID-{index:00}", 62000 + index, (byte)index))
            .ToArray();
        foreach (var station in stations.Take(4))
        {
            station.YardId = "560";
        }
        foreach (var station in stations.Skip(4).Take(3))
        {
            station.YardId = "620";
        }
        var yards = new[]
        {
            new StationConfig
            {
                Id = "560",
                Devices = new List<DeviceConfig>
                {
                    CreateDevice("RFID-01"),
                    CreateDevice("RFID-02"),
                    CreateDevice("RFID-03"),
                    CreateDevice("RFID-04"),
                    CreateDevice("RFID-01"),
                    CreateDevice("RFID-MISSING")
                }
            },
            new StationConfig
            {
                Id = "620",
                Devices = new List<DeviceConfig>
                {
                    CreateDevice("RFID-05"),
                    CreateDevice("RFID-06"),
                    CreateDevice("RFID-07")
                }
            }
        };
        return new YardRfidStationResolver(yards, stations);
    }

    private static StationConfig CreateYard(string yardId, string rfidStationId) => new()
    {
        Id = yardId,
        Devices = new List<DeviceConfig> { CreateDevice(rfidStationId) }
    };

    private static DeviceConfig CreateDevice(string rfidStationId) => new()
    {
        Type = DeviceType.RfidStation,
        RfidStationId = rfidStationId,
        ProtocolAddress = "01"
    };

    private static RfidStationConfig CreateRfidStation(string rfidStationId, int port, byte protocolAddress) => new()
    {
        StationId = rfidStationId,
        Name = rfidStationId,
        IpAddress = "127.0.0.1",
        Port = port,
        ProtocolAddress = protocolAddress,
        Enabled = true
    };

    private sealed class NoopRequestSender : IRfidRequestSender
    {
        public Task SendAsync(byte[] request, IPEndPoint destination, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ImmediateTimeProvider : IRfidTimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
