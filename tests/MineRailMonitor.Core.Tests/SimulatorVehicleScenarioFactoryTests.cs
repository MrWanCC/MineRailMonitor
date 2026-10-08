using MineRailMonitor.Core.Models;
using MineRailMonitor.Simulator.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class SimulatorVehicleScenarioFactoryTests
{
    [Fact]
    public void Configured_vehicle_scenarios_use_its_real_rfid_sequence_and_expected_count()
    {
        var vehicle = CreateVehicle();
        vehicle.Tags = vehicle.Tags.OrderByDescending(tag => tag.Position).ToList();

        var normal = SimulatorVehicleScenarioFactory.CreateNormal(vehicle);
        var missingFive = SimulatorVehicleScenarioFactory.CreateMissingFive(vehicle);
        var missingOne = SimulatorVehicleScenarioFactory.CreateMissingOne(vehicle);
        var supplement = SimulatorVehicleScenarioFactory.CreateLateCompletion(vehicle);

        Assert.Equal(new ushort[] { 1001, 2001, 2002, 2003, 2004, 2005, 2006, 2007, 2008, 2009, 2010 }, normal.Rfids);
        Assert.Equal("矿车001 · 11节正常通过", normal.Name);
        Assert.Equal(new ushort[] { 1001, 2001, 2002, 2003, 2004, 2005 }, missingFive.Rfids);
        Assert.Equal("矿车001 · 11节缺5节", missingFive.Name);
        Assert.Equal(new ushort[] { 1001, 2001, 2002, 2003, 2004, 2005, 2006, 2007, 2008, 2009 }, missingOne.Rfids);
        Assert.Equal("矿车001 · 11节缺1节", missingOne.Name);
        Assert.Equal(normal.Rfids, supplement.Rfids);
        Assert.Equal(6, supplement.DelayBeforeRfidIndex);
        Assert.Equal(TimeSpan.FromSeconds(25), supplement.DelayBeforeRfid);
    }

    [Fact]
    public void Unconfigured_vehicle_scenario_uses_a_head_and_tags_not_bound_to_project_vehicles()
    {
        var vehicle = CreateVehicle();

        var scenario = SimulatorVehicleScenarioFactory.CreateUnconfiguredVehicle(new[] { vehicle }, expectedCount: 11);

        Assert.Equal(11, scenario.Rfids.Count);
        Assert.Equal(11, scenario.Rfids.Distinct().Count());
        Assert.Equal("未配置车辆 · 11节", scenario.Name);
        Assert.DoesNotContain(scenario.Rfids[0], vehicle.Tags.Select(tag => tag.Rfid));
        Assert.All(scenario.Rfids, rfid => Assert.DoesNotContain(rfid, vehicle.Tags.Select(tag => tag.Rfid)));
    }

    [Fact]
    public void Missing_tag_scenarios_follow_the_selected_vehicle_count_instead_of_assuming_eleven()
    {
        var vehicle = CreateVehicle();
        vehicle.Tags = vehicle.Tags.Take(7).ToList();

        var missingFive = SimulatorVehicleScenarioFactory.CreateMissingFive(vehicle);
        var missingOne = SimulatorVehicleScenarioFactory.CreateMissingOne(vehicle);

        Assert.Equal("矿车001 · 7节缺5节", missingFive.Name);
        Assert.Equal(new ushort[] { 1001, 2001 }, missingFive.Rfids);
        Assert.Equal("矿车001 · 7节缺1节", missingOne.Name);
        Assert.Equal(new ushort[] { 1001, 2001, 2002, 2003, 2004, 2005 }, missingOne.Rfids);
    }

    [Fact]
    public void Project_vehicle_loader_reads_names_rfid_tags_and_expected_count_from_manifest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mine-rail-project-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "Name": "现场项目",
              "RfidSettings": { "ExpectedVehicleCount": 11 },
              "Vehicles": [
                {
                  "HeadRfid": 1001,
                  "Name": "矿车001",
                  "Enabled": true,
                  "Tags": [
                    { "Position": 0, "Rfid": 1001, "Type": "Head" },
                    { "Position": 1, "Rfid": 2001, "Type": "Carriage" }
                  ]
                },
                {
                  "HeadRfid": 1002,
                  "Name": "停用车辆",
                  "Enabled": false,
                  "Tags": []
                }
              ]
            }
            """);

        try
        {
            var project = SimulatorProjectVehicleLoader.Load(path);

            var vehicle = Assert.Single(project.Vehicles);
            Assert.Equal("现场项目", project.ProjectName);
            Assert.Equal(11, project.ExpectedVehicleCount);
            Assert.Equal("RFID-03E9", vehicle.VehicleNo);
            Assert.Equal("矿车001", vehicle.VehicleName);
            Assert.Equal(new ushort[] { 1001, 2001 }, vehicle.Tags.OrderBy(tag => tag.Position).Select(tag => tag.Rfid));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static VehicleConfig CreateVehicle() => new()
    {
        VehicleNo = "K001",
        VehicleName = "矿车001",
        HeadRfid = 1001,
        Tags = Enumerable.Range(0, 11)
            .Select(index => new VehicleTagConfig
            {
                Position = index,
                Rfid = index == 0 ? (ushort)1001 : (ushort)(2000 + index),
                Type = index == 0 ? VehicleTagType.Head : VehicleTagType.Carriage
            })
            .ToList()
    };
}
