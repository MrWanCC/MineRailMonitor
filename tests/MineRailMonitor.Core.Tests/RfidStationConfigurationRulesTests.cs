using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidStationConfigurationRulesTests
{
    [Fact]
    public void Finds_duplicate_station_ids_case_insensitively_after_trimming()
    {
        var stations = new[]
        {
            CreateStation("RFID-04"),
            CreateStation(" rfid-04 "),
            CreateStation("RFID-05")
        };

        var duplicateIds = RfidStationConfigurationRules.FindDuplicateStationIds(stations);

        Assert.Equal(new[] { "RFID-04" }, duplicateIds);
    }

    [Fact]
    public void Defensive_first_by_id_projection_keeps_one_row_per_station_id()
    {
        var first = CreateStation("RFID-04");
        var stations = new[]
        {
            first,
            CreateStation("rfid-04"),
            CreateStation("RFID-05")
        };

        var result = RfidStationConfigurationRules.TakeFirstByStationId(stations);

        Assert.Equal(2, result.Count);
        Assert.Same(first, result[0]);
        Assert.Equal("RFID-05", result[1].StationId);
    }

    private static RfidStationConfig CreateStation(string stationId) => new()
    {
        StationId = stationId
    };
}
