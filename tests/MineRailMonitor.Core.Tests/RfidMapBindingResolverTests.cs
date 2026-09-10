using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidMapBindingResolverTests
{
    [Fact]
    public void Resolves_map_runtime_identity_by_station_id_even_when_protocol_addresses_match()
    {
        var first = CreateStation("RFID-01", "一号读卡站", 10001, 0x01);
        var second = CreateStation("RFID-02", "二号读卡站", 10002, 0x01);

        var firstResolution = RfidMapBindingResolver.Resolve(new DeviceConfig
        {
            Type = DeviceType.RfidStation,
            RfidStationId = first.StationId,
            Name = "旧地图名称"
        }, new[] { first, second });
        var secondResolution = RfidMapBindingResolver.Resolve(new DeviceConfig
        {
            Type = DeviceType.RfidStation,
            RfidStationId = second.StationId,
            Name = "旧地图名称"
        }, new[] { first, second });

        Assert.Same(first, firstResolution.Configuration);
        Assert.Same(second, secondResolution.Configuration);
        Assert.Equal("一号读卡站", firstResolution.EffectiveName);
        Assert.Equal("二号读卡站", secondResolution.EffectiveName);
        Assert.Equal(RfidMapBindingState.Offline, firstResolution.State);
        Assert.Equal(RfidMapBindingState.Offline, secondResolution.State);
    }

    [Fact]
    public void Migrates_a_legacy_protocol_binding_only_when_exactly_one_station_matches()
    {
        var station = new StationConfig
        {
            Devices = new List<DeviceConfig>
            {
                new()
                {
                    Id = "map-rfid-1",
                    Type = DeviceType.RfidStation,
                    ProtocolAddress = "01",
                    RfidStationId = null
                }
            }
        };

        var result = RfidMapBindingResolver.ApplyLegacyProtocolBindings(
            new[] { station },
            new[] { CreateStation("RFID-01", "一号读卡站", 10001, 0x01) });

        Assert.Equal(1, result.MigratedCount);
        Assert.Equal("RFID-01", station.Devices[0].RfidStationId);
        Assert.Equal("01", station.Devices[0].ProtocolAddress);
    }

    [Fact]
    public void Leaves_legacy_protocol_binding_unbound_when_protocol_matches_multiple_stations()
    {
        var device = new DeviceConfig
        {
            Id = "map-rfid-1",
            Type = DeviceType.RfidStation,
            ProtocolAddress = "01"
        };
        var station = new StationConfig { Devices = new List<DeviceConfig> { device } };

        var result = RfidMapBindingResolver.ApplyLegacyProtocolBindings(
            new[] { station },
            new[]
            {
                CreateStation("RFID-01", "一号读卡站", 10001, 0x01),
                CreateStation("RFID-02", "二号读卡站", 10002, 0x01)
            });

        Assert.Equal(0, result.MigratedCount);
        Assert.Equal(1, result.AmbiguousCount);
        Assert.Null(device.RfidStationId);
    }

    [Fact]
    public void Distinguishes_unbound_missing_disabled_and_offline_states()
    {
        var disabled = CreateStation("RFID-D", "禁用站", 10003, 0x03);
        disabled.Enabled = false;
        var stations = new[] { CreateStation("RFID-01", "在线配置", 10001, 0x01), disabled };

        Assert.Equal(RfidMapBindingState.Unbound, Resolve(null, stations).State);
        Assert.Equal(RfidMapBindingState.MissingConfiguration, Resolve("missing", stations).State);
        Assert.Equal(RfidMapBindingState.Disabled, Resolve("RFID-D", stations).State);
        Assert.Equal(RfidMapBindingState.Offline, Resolve("RFID-01", stations).State);
    }

    private static RfidMapBindingResolution Resolve(string? stationId, IEnumerable<RfidStationConfig> stations) =>
        RfidMapBindingResolver.Resolve(new DeviceConfig
        {
            Type = DeviceType.RfidStation,
            RfidStationId = stationId,
            Name = "地图点"
        }, stations);

    private static RfidStationConfig CreateStation(string id, string name, int port, byte protocolAddress) => new()
    {
        StationId = id,
        Name = name,
        IpAddress = "127.0.0.1",
        Port = port,
        ProtocolAddress = protocolAddress,
        Enabled = true
    };
}
