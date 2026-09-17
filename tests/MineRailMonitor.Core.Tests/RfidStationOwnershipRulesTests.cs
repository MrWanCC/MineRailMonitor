using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidStationOwnershipRulesTests
{
    [Fact]
    public void Rejects_a_station_that_references_an_unknown_yard()
    {
        var stations = new[] { CreateRfidStation("RFID-01", "missing") };

        var errors = RfidStationOwnershipRules.Validate(
            new[] { CreateYard("560") },
            stations);

        Assert.Contains(errors, error =>
            error.IndexOf("RFID-01", StringComparison.Ordinal) >= 0 &&
            error.IndexOf("所属站场不存在", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void Rejects_a_station_owned_by_a_different_yard_than_its_map_binding()
    {
        var stations = new[] { CreateRfidStation("RFID-01", "620") };
        var yards = new[]
        {
            CreateYard("560", "RFID-01"),
            CreateYard("620")
        };

        var errors = RfidStationOwnershipRules.Validate(yards, stations);

        Assert.Contains(errors, error =>
            error.IndexOf("RFID-01", StringComparison.Ordinal) >= 0 &&
            error.IndexOf("560", StringComparison.Ordinal) >= 0 &&
            error.IndexOf("620", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void Allows_an_owned_station_without_a_map_point_binding()
    {
        var errors = RfidStationOwnershipRules.Validate(
            new[] { CreateYard("560") },
            new[] { CreateRfidStation("RFID-01", "560") });

        Assert.Empty(errors);
    }

    [Fact]
    public void Allows_an_owned_station_when_its_map_binding_is_in_the_same_yard()
    {
        var errors = RfidStationOwnershipRules.Validate(
            new[] { CreateYard("560", "RFID-01") },
            new[] { CreateRfidStation("RFID-01", "560") });

        Assert.Empty(errors);
    }

    private static RfidStationConfig CreateRfidStation(string stationId, string? yardId) => new()
    {
        StationId = stationId,
        Name = stationId,
        YardId = yardId,
        IpAddress = "127.0.0.1",
        Port = 62001,
        ProtocolAddress = 1,
        Enabled = true
    };

    private static StationConfig CreateYard(string yardId, params string[] rfidStationIds) => new()
    {
        Id = yardId,
        Name = $"-{yardId} 站场",
        Devices = rfidStationIds
            .Select((rfidStationId, index) => new DeviceConfig
            {
                Id = $"point-{index + 1}",
                Name = $"点位{index + 1}",
                Type = DeviceType.RfidStation,
                StationId = yardId,
                RfidStationId = rfidStationId,
                Enabled = true
            })
            .ToArray()
    };
}
