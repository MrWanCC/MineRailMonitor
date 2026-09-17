using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidStationIdentityTests
{
    [Fact]
    public void Creates_a_scoped_key_for_a_yard_local_number()
    {
        Assert.Equal("RFID-620-01", RfidStationIdentity.CreateScopedId("-620", 1));
    }

    [Fact]
    public void Displays_the_full_scoped_key_for_a_scoped_station()
    {
        Assert.Equal("RFID-620-01", RfidStationIdentity.GetDisplayId("RFID-620-01", "-620"));
    }

    [Fact]
    public void Normalizes_a_legacy_station_id_to_the_selected_yard()
    {
        Assert.Equal("RFID-620-07", RfidStationIdentity.GetDisplayId("RFID-07", "-620"));
    }

    [Fact]
    public void Builds_legacy_aliases_for_existing_scoped_station_ids()
    {
        var stations = new[]
        {
            new RfidStationConfig { StationId = "RFID-560-01", YardId = "560" },
            new RfidStationConfig { StationId = "RFID-620-01", YardId = "620" }
        };

        var aliases = RfidStationIdentity.BuildLegacyMigrationMap(stations);

        Assert.Equal("RFID-560-01", aliases["RFID-01"]);
        Assert.Equal("RFID-620-01", aliases["RFID-07"]);
        Assert.Equal("RFID-620-01", aliases["RFID-620-07"]);
    }
}
