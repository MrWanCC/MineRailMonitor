using System.Net;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class VehicleMatchResolverTests
{
    [Fact]
    public void Uses_vehicle_configuration_when_head_tag_matches()
    {
        var vehicle = CreateVehicle(1, "01号矿车", 4);
        var station = CreateStation(0x01, "127.0.0.1", 62001, 8);
        var project = CreateProject(new[] { vehicle }, new[] { station }, 11);
        var frame = CreateFrame(station, new ushort[] { 1, 17, 18 });

        var result = new VehicleMatchResolver(project).Resolve(frame);

        Assert.True(result.IsMatched);
        Assert.Equal("K0001", result.VehicleNo);
        Assert.Equal((ushort)1, result.HeadRfid);
        Assert.Equal("01号矿车", result.VehicleName);
        Assert.Equal(4, result.ExpectedVehicleCount);
        Assert.Equal(VehicleMatchSource.VehicleConfiguration, result.Source);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(7)]
    public void Uses_custom_vehicle_tag_count_for_matched_vehicle(int tagCount)
    {
        var vehicle = CreateVehicle(1, "自定义编组车辆", tagCount);
        var station = CreateStation(0x01, "127.0.0.1", 62001, null);
        var project = CreateProject(new[] { vehicle }, new[] { station }, 11);
        var frame = CreateFrame(station, new ushort[] { 1, 17, 18 });

        var result = new VehicleMatchResolver(project).Resolve(frame);

        Assert.True(result.IsMatched);
        Assert.Equal(tagCount, result.ExpectedVehicleCount);
        Assert.Equal(VehicleMatchSource.VehicleConfiguration, result.Source);
    }

    [Fact]
    public void Uses_station_expected_count_when_head_tag_has_no_vehicle_configuration()
    {
        var station = CreateStation(0x01, "127.0.0.1", 62001, 6);
        var project = CreateProject(Array.Empty<VehicleConfig>(), new[] { station }, 11);
        var frame = CreateFrame(station, new ushort[] { 2, 17, 18 });

        var result = new VehicleMatchResolver(project).Resolve(frame);

        Assert.False(result.IsMatched);
        Assert.Equal(string.Empty, result.VehicleNo);
        Assert.Equal((ushort)2, result.HeadRfid);
        Assert.Equal(string.Empty, result.VehicleName);
        Assert.Equal(6, result.ExpectedVehicleCount);
        Assert.Equal(VehicleMatchSource.StationConfiguration, result.Source);
    }

    [Fact]
    public void Uses_project_global_expected_count_when_station_has_no_override()
    {
        var station = CreateStation(0x01, "127.0.0.1", 62001, null);
        var project = CreateProject(Array.Empty<VehicleConfig>(), new[] { station }, 11);
        var frame = CreateFrame(station, new ushort[] { 2, 17, 18 });

        var result = new VehicleMatchResolver(project).Resolve(frame);

        Assert.False(result.IsMatched);
        Assert.Equal(11, result.ExpectedVehicleCount);
        Assert.Equal(VehicleMatchSource.GlobalConfiguration, result.Source);
    }

    [Fact]
    public void Does_not_assume_eleven_sections_for_unmatched_vehicle_when_no_fallback_is_configured()
    {
        var station = CreateStation(0x01, "127.0.0.1", 62001, null);
        var project = new ProjectConfig
        {
            RfidSettings = new RfidSettings(),
            RfidStations = new[] { station }
        };
        var frame = CreateFrame(station, new ushort[] { 2, 17, 18 });

        var result = new VehicleMatchResolver(project).Resolve(frame);

        Assert.False(result.IsMatched);
        Assert.True((object?)result.ExpectedVehicleCount is null);
        Assert.Equal(VehicleMatchSource.Unconfigured, result.Source);
    }

    [Fact]
    public void Does_not_use_head_rfid_property_when_raw_position_zero_is_empty()
    {
        var station = CreateStation(0x01, "127.0.0.1", 62001, 6);
        var project = CreateProject(new[] { CreateVehicle(1, "01号矿车", 4) }, new[] { station }, 11);
        var frame = CreateFrame(station, new ushort[] { 0, 17, 18 });
        frame.HeadRfid = 1;

        var result = new VehicleMatchResolver(project).Resolve(frame);

        Assert.False(result.IsMatched);
        Assert.Null(result.HeadRfid);
        Assert.Equal(6, result.ExpectedVehicleCount);
        Assert.Equal(VehicleMatchSource.StationConfiguration, result.Source);
    }

    private static ProjectConfig CreateProject(
        IReadOnlyList<VehicleConfig> vehicles,
        IReadOnlyList<RfidStationConfig> stations,
        int globalExpectedCount) => new()
        {
            Vehicles = vehicles,
            RfidStations = stations,
            RfidSettings = new RfidSettings { ExpectedVehicleCount = globalExpectedCount }
        };

    private static VehicleConfig CreateVehicle(ushort headRfid, string name, int tagCount) => new()
    {
        VehicleNo = $"K{headRfid:X4}",
        HeadRfid = headRfid,
        VehicleName = name,
        Enabled = true,
        Tags = Enumerable.Range(0, tagCount)
            .Select(position => new VehicleTagConfig
            {
                Position = position,
                Rfid = position == 0 ? headRfid : (ushort)(16 + position),
                Type = position == 0 ? VehicleTagType.Head : VehicleTagType.Carriage
            })
            .ToList()
    };

    private static RfidStationConfig CreateStation(byte address, string ipAddress, int port, int? expectedCount) => new()
    {
        StationId = "RFID-01",
        Name = "测试基站",
        ProtocolAddress = address,
        IpAddress = ipAddress,
        Port = port,
        ExpectedVehicleCount = expectedCount,
        Enabled = true
    };

    private static RfidStationFrame CreateFrame(RfidStationConfig station, IReadOnlyList<ushort> slots)
    {
        station.TryResolveEndpoint(out var endpoint);
        return new RfidStationFrame
        {
            StationAddress = station.ProtocolAddress,
            SourceEndpoint = endpoint,
            RawRfidSlots = slots
        };
    }
}
