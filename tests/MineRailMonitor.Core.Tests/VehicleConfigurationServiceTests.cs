using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class VehicleConfigurationServiceTests
{
    [Fact]
    public void Finds_configured_vehicle_using_position_zero_from_raw_slots()
    {
        var vehicle = CreateVehicle(1);
        var project = new ProjectConfig { Vehicles = new[] { vehicle } };
        var frame = new RfidStationFrame { RawRfidSlots = new ushort[] { 1, 17, 18 } };

        var service = new VehicleConfigurationService(project);
        var match = service.FindByHeadRfid(frame.RawRfidSlots[0]);

        Assert.Same(vehicle, match);
        Assert.Equal(3, match!.TotalSectionCount);
    }

    [Fact]
    public void Returns_no_vehicle_when_position_zero_is_not_configured()
    {
        var project = new ProjectConfig { Vehicles = new[] { CreateVehicle(1) } };
        var frame = new RfidStationFrame { RawRfidSlots = new ushort[] { 2, 17, 18 } };

        var service = new VehicleConfigurationService(project);
        var match = service.FindByHeadRfid(frame.RawRfidSlots[0]);

        Assert.Null(match);
    }

    [Fact]
    public void Does_not_match_disabled_vehicle_configuration()
    {
        var vehicle = CreateVehicle(1);
        vehicle.Enabled = false;
        var project = new ProjectConfig { Vehicles = new[] { vehicle } };

        Assert.Null(new VehicleConfigurationService(project).FindByHeadRfid(1));
    }

    private static VehicleConfig CreateVehicle(ushort headRfid) => new()
    {
        HeadRfid = headRfid,
        Name = "01号矿车",
        Enabled = true,
        Tags = new List<VehicleTagConfig>
        {
            new() { Position = 0, Rfid = headRfid, Type = VehicleTagType.Head },
            new() { Position = 1, Rfid = 17, Type = VehicleTagType.Carriage },
            new() { Position = 2, Rfid = 18, Type = VehicleTagType.Carriage }
        }
    };
}
