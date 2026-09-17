using MineRailMonitor.Core.Models;
using MineRailMonitor.Infrastructure.Configuration;
using MineRailMonitor.Infrastructure.Logging;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class ProjectConfigPersistenceTests
{
    [Fact]
    public async Task Saves_coordinates_without_dropping_unknown_fields_or_map_protocol_configuration()
    {
        using var project = TemporaryProject.Create(twoStations: false);
        var service = CreateService();
        var loaded = await service.LoadAsync(project.Path);
        var station = Assert.Single(loaded.Project!.Stations);
        var device = Assert.Single(station.Devices);
        device.CadX = 2771.8203;
        device.CadY = 1916.0352;

        var save = await service.SaveStationAsync(project.Path, station);

        Assert.True(save.Succeeded, string.Join(Environment.NewLine, save.Errors));
        var json = File.ReadAllText(project.Station560Path);
        Assert.Contains("unknown-station-value", json, StringComparison.Ordinal);
        Assert.Contains("unknown-device-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ProtocolAddress", json, StringComparison.Ordinal);

        var reloaded = await service.LoadAsync(project.Path);
        var savedDevice = Assert.Single(reloaded.Project!.Stations.Single().Devices);
        Assert.Equal(2771.8203, savedDevice.CadX);
        Assert.Equal(1916.0352, savedDevice.CadY);
        Assert.Null(savedDevice.ProtocolAddress);
    }

    [Fact]
    public async Task Removing_one_device_does_not_remove_other_devices()
    {
        using var project = TemporaryProject.Create(twoStations: false, twoDevices: true);
        var service = CreateService();
        var loaded = await service.LoadAsync(project.Path);
        var station = Assert.Single(loaded.Project!.Stations);
        station.Devices = station.Devices.Where(device => device.Id != "Y6-11").ToList();

        var save = await service.SaveStationAsync(project.Path, station);

        Assert.True(save.Succeeded, string.Join(Environment.NewLine, save.Errors));
        var reloaded = await service.LoadAsync(project.Path);
        var devices = reloaded.Project!.Stations.Single().Devices;
        Assert.Single(devices);
        Assert.Equal("Y6-10", devices[0].Id);
    }

    [Fact]
    public async Task Saving_560_does_not_modify_620_configuration()
    {
        using var project = TemporaryProject.Create(twoStations: true);
        var service = CreateService();
        var loaded = await service.LoadAsync(project.Path);
        var station560 = loaded.Project!.Stations.Single(station => station.Id == "560");
        station560.Devices = station560.Devices.Concat(new[]
        {
            new DeviceConfig
            {
                Id = "TEST-01",
                Name = "TEST-01",
                Type = DeviceType.RfidStation,
                StationId = "560",
                CadX = 2771.8203,
                CadY = 1916.0352,
                ProtocolAddress = null,
                Enabled = true
            }
        }).ToList();

        var save = await service.SaveStationAsync(project.Path, station560);

        Assert.True(save.Succeeded, string.Join(Environment.NewLine, save.Errors));
        var reloaded = await service.LoadAsync(project.Path);
        Assert.Contains(reloaded.Project!.Stations.Single(station => station.Id == "560").Devices, device => device.Id == "TEST-01");
        Assert.Empty(reloaded.Project.Stations.Single(station => station.Id == "620").Devices);
    }

    [Fact]
    public async Task Saves_map_points_and_duplicate_labels_without_protocol_fields()
    {
        using var project = TemporaryProject.CreateWithMapOverlays();
        var service = CreateService();
        var loaded = await service.LoadAsync(project.Path);
        var station = Assert.Single(loaded.Project!.Stations);
        var point = Assert.Single(station.Points);
        point.CadX = 2801.25;
        station.Labels = station.Labels
            .Concat(new[]
            {
                new MapLabel
                {
                    Id = "label-duplicate-2",
                    Text = "东西联络巷",
                    CadX = 3100.5,
                    CadY = 2200.25,
                    Rotation = 8.066157,
                    TextHeight = 5,
                    Enabled = true
                }
            })
            .ToList();

        var save = await service.SaveStationAsync(project.Path, station);

        Assert.True(save.Succeeded, string.Join(Environment.NewLine, save.Errors));
        var json = File.ReadAllText(project.Station560Path);
        Assert.Contains("unknown-station-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ProtocolAddress", json, StringComparison.Ordinal);

        var reloaded = await service.LoadAsync(project.Path);
        var savedStation = Assert.Single(reloaded.Project!.Stations);
        Assert.Equal(2801.25, Assert.Single(savedStation.Points).CadX);
        Assert.Equal(2, savedStation.Labels.Count);
        Assert.Equal(
            new[] { "label-duplicate-1", "label-duplicate-2" },
            savedStation.Labels.Select(label => label.Id));
        Assert.Equal("东西联络巷", savedStation.Labels[0].Text);
        Assert.Equal(3100.5, savedStation.Labels[1].CadX);
        Assert.Equal(2200.25, savedStation.Labels[1].CadY);
        Assert.Equal(8.066157, savedStation.Labels[1].Rotation);
        Assert.Equal(5.0, savedStation.Labels[1].TextHeight);
    }

    [Fact]
    public async Task Saves_and_reloads_all_three_annotation_types_with_an_empty_rfid_address()
    {
        using var project = TemporaryProject.CreateWithMapOverlays();
        var service = CreateService();
        var loaded = await service.LoadAsync(project.Path);
        var station = Assert.Single(loaded.Project!.Stations);
        station.Points = station.Points.Concat(new[]
        {
            new MapPoint { Id = "point-new", Name = "Y6-18", CadX = 3010, CadY = 2210, Enabled = true }
        }).ToList();
        station.Labels = station.Labels.Concat(new[]
        {
            new MapLabel { Id = "label-new", Text = "2号穿北", CadX = 3020, CadY = 2220, Rotation = 12, TextHeight = 10, Enabled = true }
        }).ToList();
        station.Devices = station.Devices.Concat(new[]
        {
            new DeviceConfig
            {
                Id = "rfid-new", Name = "临时RFID", Type = DeviceType.RfidStation, StationId = station.Id,
                CadX = 3030, CadY = 2230, ProtocolAddress = null, Enabled = true
            }
        }).ToList();

        var save = await service.SaveStationAsync(project.Path, station);

        Assert.True(save.Succeeded, string.Join(Environment.NewLine, save.Errors));
        var reloaded = await service.LoadAsync(project.Path);
        var savedStation = Assert.Single(reloaded.Project!.Stations);
        var point = Assert.Single(savedStation.Points, item => item.Id == "point-new");
        var label = Assert.Single(savedStation.Labels, item => item.Id == "label-new");
        var device = Assert.Single(savedStation.Devices, item => item.Id == "rfid-new");
        Assert.Equal(3010, point.CadX);
        Assert.Equal("2号穿北", label.Text);
        Assert.Equal(12, label.Rotation);
        Assert.Null(device.ProtocolAddress);
        Assert.Equal(3030, device.CadX);
    }

    [Fact]
    public async Task Loads_legacy_map_protocol_binding_in_memory_without_rewriting_until_map_is_saved()
    {
        const string manifest = """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "RfidStations": [
                { "StationId": "RFID-01", "Name": "一号读卡站", "IpAddress": "127.0.0.1", "Port": 10001, "ProtocolAddress": 1, "Enabled": true }
              ],
              "Stations": [ { "Id": "560", "ConfigFile": "stations/560.json" } ]
            }
            """;
        var stationJson = """
            {
              "Id": "560",
              "Name": "-560 站场",
              "Devices": [
                { "Id": "map-rfid-1", "Name": "旧名称", "Type": "RfidStation", "StationId": "560", "ProtocolAddress": "01", "IpAddress": "192.0.2.10", "Port": 62001, "CommunicationState": "Online", "LastResponseAt": "2026-09-11T10:00:00+08:00", "CadX": 1, "CadY": 2, "Enabled": true }
              ]
            }
            """;
        using var project = TemporaryProject.Create(false, false, stationJson, manifest);
        var service = CreateService();
        var original = File.ReadAllText(project.Station560Path);

        var loaded = await service.LoadAsync(project.Path);

        var station = Assert.Single(loaded.Project!.Stations);
        var device = Assert.Single(station.Devices);
        Assert.Equal("RFID-01", device.RfidStationId);
        Assert.Equal(original, File.ReadAllText(project.Station560Path));

        var save = await service.SaveStationAsync(project.Path, station);

        Assert.True(save.Succeeded, string.Join(Environment.NewLine, save.Errors));
        var savedJson = File.ReadAllText(project.Station560Path);
        Assert.Contains("\"RfidStationId\": \"RFID-01\"", savedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("ProtocolAddress", savedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_to_create_a_new_cross_yard_duplicate_rfid_binding_when_saving()
    {
        const string manifest = """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "RfidStations": [
                { "StationId": "RFID-01", "Name": "一号读卡站", "IpAddress": "127.0.0.1", "Port": 10001, "ProtocolAddress": 1, "Enabled": true },
                { "StationId": "RFID-02", "Name": "二号读卡站", "IpAddress": "127.0.0.1", "Port": 10002, "ProtocolAddress": 2, "Enabled": true }
              ],
              "Stations": [
                { "Id": "560", "ConfigFile": "stations/560.json" },
                { "Id": "620", "ConfigFile": "stations/620.json" }
              ]
            }
            """;
        const string station560Json = """
            {
              "Id": "560",
              "Name": "-560 站场",
              "Devices": [
                { "Id": "point-a", "Name": "卸矿站", "Type": "RfidStation", "StationId": "560", "RfidStationId": "RFID-01", "Enabled": true }
              ]
            }
            """;
        const string station620Json = """
            {
              "Id": "620",
              "Name": "-620 站场",
              "Devices": []
            }
            """;
        using var project = TemporaryProject.Create(
            twoStations: true,
            twoDevices: false,
            station560Json: station560Json,
            manifest: manifest);
        File.WriteAllText(project.Station620Path, station620Json);
        var service = CreateService();
        var loaded = await service.LoadAsync(project.Path);
        var station620 = loaded.Project!.Stations.Single(station => station.Id == "620");
        station620.Devices = new[]
        {
            new DeviceConfig
            {
                Id = "point-b",
                Name = "三号引坡",
                Type = DeviceType.RfidStation,
                StationId = "620",
                RfidStationId = "RFID-01",
                Enabled = true
            }
        };

        var save = await service.SaveStationAsync(project.Path, station620);

        Assert.False(save.Succeeded);
        Assert.Contains(save.Errors, error => error.Contains("重复绑定"));
        Assert.DoesNotContain("\"point-b\"", File.ReadAllText(project.Station620Path));
    }

    [Fact]
    public async Task Saves_and_reloads_rfid_station_yard_ownership()
    {
        const string manifest = """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "RfidStations": [
                { "StationId": "RFID-01", "Name": "一号读卡站", "YardId": "560", "IpAddress": "127.0.0.1", "Port": 62001, "ProtocolAddress": 1, "Enabled": true }
              ],
              "Stations": [
                { "Id": "560", "ConfigFile": "stations/560.json" },
                { "Id": "620", "ConfigFile": "stations/620.json" }
              ]
            }
            """;
        using var project = TemporaryProject.Create(true, false, manifest: manifest);
        var service = CreateService();
        var loaded = await service.LoadAsync(project.Path);
        var station = Assert.Single(loaded.Project!.RfidStations);

        Assert.Equal("560", station.YardId);

        station.YardId = "620";
        var save = await service.SaveRfidStationsAsync(project.Path, new[] { station });

        Assert.True(save.Succeeded, string.Join(Environment.NewLine, save.Errors));
        var reloaded = await service.LoadAsync(project.Path);
        Assert.Equal("620", Assert.Single(reloaded.Project!.RfidStations).YardId);
    }

    [Fact]
    public async Task Refuses_to_save_station_ownership_that_conflicts_with_map_binding()
    {
        const string manifest = """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "RfidStations": [
                { "StationId": "RFID-01", "Name": "一号读卡站", "YardId": "620", "IpAddress": "127.0.0.1", "Port": 62001, "ProtocolAddress": 1, "Enabled": true }
              ],
              "Stations": [
                { "Id": "560", "ConfigFile": "stations/560.json" },
                { "Id": "620", "ConfigFile": "stations/620.json" }
              ]
            }
            """;
        const string station560Json = """
            {
              "Id": "560",
              "Name": "-560 站场",
              "Devices": [
                { "Id": "point-a", "Name": "卸矿站", "Type": "RfidStation", "StationId": "560", "RfidStationId": "RFID-01", "Enabled": true }
              ]
            }
            """;
        using var project = TemporaryProject.Create(true, false, station560Json, manifest);
        var service = CreateService();
        var loaded = await service.LoadAsync(project.Path);
        var station = Assert.Single(loaded.Project!.RfidStations);
        var originalManifest = File.ReadAllText(System.IO.Path.Combine(project.Path, "project.json"));

        var save = await service.SaveRfidStationsAsync(project.Path, new[] { station });

        Assert.False(save.Succeeded);
        Assert.Contains(save.Errors, error => error.IndexOf("其它站场", StringComparison.Ordinal) >= 0);
        Assert.Equal(originalManifest, File.ReadAllText(System.IO.Path.Combine(project.Path, "project.json")));
    }

    [Fact]
    public async Task Reports_duplicate_rfid_station_ids_when_loading_project_manifest()
    {
        const string manifest = """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "RfidStations": [
                { "StationId": "RFID-04", "Name": "四号读卡站", "IpAddress": "127.0.0.1", "Port": 10004, "ProtocolAddress": 4, "Enabled": true },
                { "StationId": "rfid-04", "Name": "四号读卡站副本", "IpAddress": "127.0.0.1", "Port": 10014, "ProtocolAddress": 14, "Enabled": true }
              ],
              "Stations": [ { "Id": "560", "ConfigFile": "stations/560.json" } ]
            }
            """;
        using var project = TemporaryProject.Create(false, false, manifest: manifest);
        var service = CreateService();

        var result = await service.LoadAsync(project.Path);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.IndexOf("RFID基站编号重复：RFID-04", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static ProjectConfigService CreateService() =>
        new(new FileLogger(Path.Combine(AppContext.BaseDirectory, "test-logs")));

    private sealed class TemporaryProject : IDisposable
    {
        private const string ManifestOneStation = """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "Stations": [ { "Id": "560", "ConfigFile": "stations/560.json" } ]
            }
            """;

        private const string ManifestTwoStations = """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "Stations": [
                { "Id": "560", "ConfigFile": "stations/560.json" },
                { "Id": "620", "ConfigFile": "stations/620.json" }
              ]
            }
            """;

        private TemporaryProject(bool twoStations, bool twoDevices, string? station560Json = null, string? manifest = null)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MineRailMonitor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "stations"));
            File.WriteAllText(System.IO.Path.Combine(Path, "project.json"), manifest ?? (twoStations ? ManifestTwoStations : ManifestOneStation));
            File.WriteAllText(Station560Path, station560Json ?? CreateStationJson(twoDevices));
            if (twoStations)
            {
                File.WriteAllText(Station620Path, CreateStationJson(twoDevices: false, stationId: "620", includeFirst: false));
            }
        }

        public string Path { get; }

        public string Station560Path => System.IO.Path.Combine(Path, "stations", "560.json");

        public string Station620Path => System.IO.Path.Combine(Path, "stations", "620.json");

        public static TemporaryProject Create(bool twoStations, bool twoDevices = false, string? station560Json = null, string? manifest = null) =>
            new(twoStations, twoDevices, station560Json, manifest);

        public static TemporaryProject CreateWithMapOverlays() => new(false, false, CreateMapOverlayJson());

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }

        private static string CreateStationJson(bool twoDevices, string stationId = "560", bool includeFirst = true)
        {
            var first = includeFirst
                ? "    { \"Id\": \"Y6-10\", \"Name\": \"Y6-10\", \"Type\": \"RfidStation\", \"StationId\": \"560\", \"CadX\": 2770.0, \"CadY\": 1910.0, \"ProtocolAddress\": null, \"Enabled\": true, \"UnknownDeviceProperty\": \"unknown-device-value\" }"
                : string.Empty;
            var second = twoDevices
                ? (includeFirst ? "," : "    ") + "\n        { \"Id\": \"Y6-11\", \"Name\": \"Y6-11\", \"Type\": \"RfidStation\", \"StationId\": \"560\", \"CadX\": 2763.2233, \"CadY\": 1963.9322, \"ProtocolAddress\": null, \"Enabled\": true }"
                : string.Empty;
            return "{\n" +
                $"  \"Id\": \"{stationId}\",\n" +
                $"  \"Name\": \"-{stationId} 站场\",\n" +
                $"  \"BackgroundImage\": \"maps/{stationId}.png\",\n" +
                "  \"CadMinX\": 2700.0,\n" +
                "  \"CadMaxX\": 2800.0,\n" +
                "  \"CadMinY\": 1850.0,\n" +
                "  \"CadMaxY\": 2100.0,\n" +
                "  \"UnknownStationProperty\": \"unknown-station-value\",\n" +
                "  \"Devices\": [\n" +
                first +
                second +
                "\n  ]\n}";
        }

        private static string CreateMapOverlayJson()
        {
            return """
                {
                  "Id": "560",
                  "Name": "-560 站场",
                  "BackgroundImage": "maps/560.png",
                  "CadMinX": 2700.0,
                  "CadMaxX": 2850.0,
                  "CadMinY": 1900.0,
                  "CadMaxY": 2300.0,
                  "UnknownStationProperty": "unknown-station-value",
                  "Points": [
                    { "Id": "Y6-1", "Name": "Y6-1", "CadX": 2800.0, "CadY": 2100.0, "Enabled": true }
                  ],
                  "Labels": [
                    { "Id": "label-duplicate-1", "Text": "东西联络巷", "CadX": 3000.0, "CadY": 2200.0, "Rotation": 8.066157, "TextHeight": 5.0, "Enabled": true }
                  ],
                  "Devices": []
                }
                """;
        }
    }
}
