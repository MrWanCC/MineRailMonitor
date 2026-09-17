using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidStationBindingRulesTests
{
    [Fact]
    public void Rejects_binding_when_station_is_already_bound_elsewhere_and_describes_the_conflict()
    {
        var yards = new[]
        {
            Yard("560", "-560水平", Device("point-a", "卸矿站", "RFID-01", "01")),
            Yard("620", "-620水平", Device("point-b", "三号引坡", null, "01"))
        };

        var result = RfidStationBindingRules.ValidateBinding(
            yards,
            new[] { RfidStation("RFID-01") },
            "620",
            "point-b",
            "RFID-01");

        Assert.False(result.Succeeded);
        Assert.Contains("-560水平", result.Message);
        Assert.Contains("卸矿站", result.Message);
        Assert.DoesNotContain("[560]", result.Message);
        Assert.DoesNotContain("point-a", result.Message);
        Assert.Single(result.Conflicts);
    }

    [Fact]
    public void Rejects_binding_when_map_point_already_has_another_station()
    {
        var yards = new[]
        {
            Yard("560", "-560水平", Device("point-a", "卸矿站", "RFID-02", "01"))
        };

        var result = RfidStationBindingRules.ValidateBinding(
            yards,
            new[] { RfidStation("RFID-01"), RfidStation("RFID-02") },
            "560",
            "point-a",
            "RFID-01");

        Assert.False(result.Succeeded);
        Assert.Contains("卸矿站", result.Message);
        Assert.DoesNotContain("point-a", result.Message);
        Assert.Contains("RFID-02", result.Message);
    }

    [Fact]
    public void Accepts_an_unbound_station_and_map_point()
    {
        var yards = new[]
        {
            Yard("560", "-560水平", Device("point-a", "卸矿站", null, "01"))
        };

        var result = RfidStationBindingRules.ValidateBinding(
            yards,
            new[] { RfidStation("RFID-01") },
            "560",
            "point-a",
            "RFID-01");

        Assert.True(result.Succeeded);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void Finds_legacy_duplicate_bindings_without_changing_them()
    {
        var yards = new[]
        {
            Yard("560", "-560水平", Device("point-a", "卸矿站", "RFID-01", "01")),
            Yard("620", "-620水平", Device("point-b", "三号引坡", "RFID-01", "02"))
        };

        var duplicates = RfidStationBindingRules.FindDuplicateBindings(yards);

        Assert.Equal(2, duplicates.Count);
        Assert.Contains(duplicates, item => item.YardId == "560" && item.DeviceId == "point-a");
        Assert.Contains(duplicates, item => item.YardId == "620" && item.DeviceId == "point-b");
        Assert.Equal("RFID-01", yards[0].Devices[0].RfidStationId);
        Assert.Equal("RFID-01", yards[1].Devices[0].RfidStationId);
    }

    [Fact]
    public void Uses_station_ids_only_and_does_not_treat_protocol_address_as_a_binding()
    {
        var yards = new[]
        {
            Yard("560", "-560水平", Device("point-a", "卸矿站", "RFID-01", "01")),
            Yard("620", "-620水平", Device("point-b", "三号引坡", null, "01"))
        };

        var result = RfidStationBindingRules.ValidateBinding(
            yards,
            new[] { RfidStation("RFID-02") },
            "620",
            "point-b",
            "RFID-02");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Rejects_map_binding_when_station_communication_ownership_is_in_another_yard()
    {
        var yards = new[]
        {
            Yard("560", "-560水平", Device("point-a", "卸矿站", null, "01")),
            Yard("620", "-620水平", Device("point-b", "三号引坡", null, "01"))
        };

        var result = RfidStationBindingRules.ValidateBinding(
            yards,
            new[] { RfidStation("RFID-01", "560") },
            "620",
            "point-b",
            "RFID-01");

        Assert.False(result.Succeeded);
        Assert.Contains("通信归属站场", result.Message);
        Assert.Contains("560", result.Message);
        Assert.Contains("620", result.Message);
    }

    private static RfidStationConfig RfidStation(string stationId, string? yardId = null) => new()
    {
        StationId = stationId,
        Name = stationId,
        YardId = yardId,
        IpAddress = "127.0.0.1",
        Port = 62001,
        ProtocolAddress = 1
    };

    private static StationConfig Yard(string id, string name, DeviceConfig device) => new()
    {
        Id = id,
        Name = name,
        Devices = new[] { device }
    };

    private static DeviceConfig Device(string id, string name, string? rfidStationId, string protocolAddress) => new()
    {
        Id = id,
        Name = name,
        Type = DeviceType.RfidStation,
        StationId = "560",
        RfidStationId = rfidStationId,
        ProtocolAddress = protocolAddress,
        Enabled = true
    };
}
