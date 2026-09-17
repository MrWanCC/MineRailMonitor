using MineRailMonitor.Infrastructure.Configuration;
using MineRailMonitor.Infrastructure.Logging;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class ProjectConfigServiceTests
{
    [Fact]
    public async Task LoadsAndSavesRfidCoreSettingsInProjectManifest()
    {
        const string manifest = """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "Unknown": "keep-me",
              "RfidSettings": {
                "PollIntervalMs": 250,
                "ExpectedWagonCount": 9,
                "InterWagonTimeoutSeconds": 45,
                "EmptyRfidValue": 0
              },
              "Stations": [ { "Id": "560", "ConfigFile": "stations/560.json" } ]
            }
            """;
        using var project = TemporaryProject.Create(manifest, TemporaryProject.StationWithoutY611);
        var service = CreateService();

        var loaded = await service.LoadAsync(project.Path);
        Assert.Equal(250, loaded.Project!.RfidSettings.PollIntervalMs);
        Assert.Equal(9, loaded.Project.RfidSettings.ExpectedVehicleCount);
        Assert.Equal(45, loaded.Project.RfidSettings.InterVehicleTimeoutSeconds);

        var saved = await service.SaveRfidSettingsAsync(project.Path, new RfidSettings
        {
            PollIntervalMs = 200,
            ExpectedVehicleCount = 11,
            InterVehicleTimeoutSeconds = 30,
            EmptyRfidValue = 0
        });

        Assert.True(saved.Succeeded, string.Join(Environment.NewLine, saved.Errors));
        var json = File.ReadAllText(Path.Combine(project.Path, "project.json"));
        Assert.Contains("keep-me", json);
        Assert.Contains("ExpectedVehicleCount", json);
        Assert.Contains("InterVehicleTimeoutSeconds", json);
        Assert.DoesNotContain("ExpectedWagonCount", json);
        Assert.DoesNotContain("InterWagonTimeoutSeconds", json);
        var reloaded = await service.LoadAsync(project.Path);
        Assert.Equal(200, reloaded.Project!.RfidSettings.PollIntervalMs);
        Assert.Equal(11, reloaded.Project.RfidSettings.ExpectedVehicleCount);
        Assert.Equal(30, reloaded.Project.RfidSettings.InterVehicleTimeoutSeconds);
    }

    [Fact]
    public async Task Loads_sanitized_example_project_without_field_map_data()
    {
        var service = CreateService();
        var projectDirectory = Path.Combine(AppContext.BaseDirectory, "Projects", "Example");

        var result = await service.LoadAsync(projectDirectory);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Errors));
        Assert.NotNull(result.Project);
        Assert.Equal("Example", result.Project!.Id);
        var station = Assert.Single(result.Project!.Stations, station => station.Id == "560");
        Assert.Empty(station.Points);
        Assert.Empty(station.Labels);
        Assert.Empty(station.Devices);
        Assert.Empty(station.BackgroundImage);
        Assert.Equal(0, station.CadMinX);
        Assert.Equal(100, station.CadMaxX);
        Assert.Equal(0, station.CadMinY);
        Assert.Equal(100, station.CadMaxY);
        Assert.Equal(new[] { "RFID-01", "RFID-02" }, result.Project.RfidStations.Select(item => item.StationId));
        Assert.False(result.Project.UsesLegacySharedListener);
        Assert.Equal(2, result.Project.YardCommunications.Count);
        Assert.Equal(56002, result.Project.YardCommunications.Single(item => item.YardId == "560").ListenPort);
        Assert.Equal(56012, result.Project.YardCommunications.Single(item => item.YardId == "620").ListenPort);
        Assert.Equal("560", result.Project.RfidStations.Single(item => item.StationId == "RFID-01").YardId);
        Assert.Equal("620", result.Project.RfidStations.Single(item => item.StationId == "RFID-02").YardId);
        Assert.All(result.Project.RfidStations, item =>
        {
            Assert.Equal("127.0.0.1", item.IpAddress);
            Assert.InRange(item.Port, 62101, 62102);
            Assert.False(item.Enabled);
        });
    }

    [Fact]
    public async Task Returns_a_user_readable_error_for_malformed_project_json()
    {
        using var project = TemporaryProject.Create("{ not valid json");
        var service = CreateService();

        var result = await service.LoadAsync(project.Path);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("JSON", string.Join(Environment.NewLine, result.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reloading_after_removing_y6_11_does_not_return_the_removed_device()
    {
        using var project = TemporaryProject.Create(TemporaryProject.Manifest, TemporaryProject.StationWithThreeDevices);
        var service = CreateService();

        var first = await service.LoadAsync(project.Path);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Errors));
        Assert.Contains(first.Project!.Stations.Single().Devices, device => device.Id == "Y6-11");

        File.WriteAllText(
            Path.Combine(project.Path, "stations", "560.json"),
            TemporaryProject.StationWithoutY611);

        var second = await service.LoadAsync(project.Path);

        Assert.True(second.Succeeded, string.Join(Environment.NewLine, second.Errors));
        Assert.DoesNotContain(second.Project!.Stations.Single().Devices, device => device.Id == "Y6-11");
    }

    private static ProjectConfigService CreateService()
    {
        return new ProjectConfigService(new FileLogger(Path.Combine(AppContext.BaseDirectory, "test-logs")));
    }

    private sealed class TemporaryProject : IDisposable
    {
        public const string Manifest = """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "Stations": [ { "Id": "560", "ConfigFile": "stations/560.json" } ]
            }
            """;

        public const string StationWithThreeDevices = """
            {
              "Id": "560",
              "Name": "-560",
              "BackgroundImage": "maps/560.png",
              "CadMinX": 2700,
              "CadMaxX": 2800,
              "CadMinY": 1900,
              "CadMaxY": 2100,
              "Devices": [
                { "Id": "Y6-10", "Name": "Y6-10", "Type": "RfidStation", "StationId": "560", "CadX": 2771.8203, "CadY": 1916.0352, "ProtocolAddress": null, "Enabled": true },
                { "Id": "Y6-11", "Name": "Y6-11", "Type": "RfidStation", "StationId": "560", "CadX": 2763.2233, "CadY": 1963.9322, "ProtocolAddress": null, "Enabled": true },
                { "Id": "Y6-12", "Name": "Y6-12", "Type": "RfidStation", "StationId": "560", "CadX": 2754.5233, "CadY": 2017.9292, "ProtocolAddress": null, "Enabled": true }
              ]
            }
            """;

        public const string StationWithoutY611 = """
            {
              "Id": "560",
              "Name": "-560",
              "BackgroundImage": "maps/560.png",
              "CadMinX": 2700,
              "CadMaxX": 2800,
              "CadMinY": 1900,
              "CadMaxY": 2100,
              "Devices": [
                { "Id": "Y6-10", "Name": "Y6-10", "Type": "RfidStation", "StationId": "560", "CadX": 2771.8203, "CadY": 1916.0352, "ProtocolAddress": null, "Enabled": true },
                { "Id": "Y6-12", "Name": "Y6-12", "Type": "RfidStation", "StationId": "560", "CadX": 2754.5233, "CadY": 2017.9292, "ProtocolAddress": null, "Enabled": true }
              ]
            }
            """;

        private TemporaryProject(string manifest, string? station = null)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MineRailMonitor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "stations"));
            File.WriteAllText(System.IO.Path.Combine(Path, "project.json"), manifest);
            if (station is not null)
            {
                File.WriteAllText(System.IO.Path.Combine(Path, "stations", "560.json"), station);
            }
        }

        public string Path { get; }

        public static TemporaryProject Create(string manifest, string? station = null) => new(manifest, station);

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
