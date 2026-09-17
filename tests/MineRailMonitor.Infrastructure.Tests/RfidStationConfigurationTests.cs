using System.Net;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Infrastructure.Configuration;
using MineRailMonitor.Infrastructure.Logging;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class RfidStationConfigurationTests
{
    [Fact]
    public async Task Save_rfid_stations_writes_canonical_independent_endpoints()
    {
        using var project = TemporaryProject.Create();
        var service = new ProjectConfigService(new FileLogger(Path.Combine(project.Path, "test-logs")));

        var result = await service.SaveRfidStationsAsync(project.Path, new[]
        {
            CreateStation("RFID-01", "一号站", "127.0.0.1", 62301, 0x31),
            CreateStation("RFID-04", "四号站", "127.0.0.1", 62304, 0x34)
        });

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Errors));
        var text = File.ReadAllText(Path.Combine(project.Path, "project.json"));
        Assert.Contains("StationId", text);
        Assert.Contains("IpAddress", text);
        Assert.Contains("ProtocolAddress", text);
        Assert.Contains("62304", text);
        Assert.DoesNotContain("198.51.100.254", text);
    }

    [Fact]
    public async Task Load_rfid_stations_maps_legacy_destination_fields_without_inventing_values()
    {
        using var project = TemporaryProject.Create("""
            {
              "Id": "test",
              "Name": "test",
              "RfidStations": [
                {
                  "Address": 52,
                  "DestinationAddress": "127.0.0.1",
                  "DestinationPort": 62304,
                  "Enabled": true
                }
              ],
              "Stations": []
            }
            """);
        var service = new ProjectConfigService(new FileLogger(Path.Combine(project.Path, "test-logs")));

        var result = await service.LoadAsync(project.Path);

        var station = Assert.Single(result.Project!.RfidStations);
        Assert.Equal(52, station.ProtocolAddress);
        Assert.Equal("127.0.0.1", station.IpAddress);
        Assert.Equal(62304, station.Port);
        Assert.Equal(IPAddress.Loopback, station.DestinationEndpoint.Address);
        Assert.Equal(62304, station.DestinationEndpoint.Port);
    }

    [Fact]
    public async Task Save_allows_same_protocol_address_when_ports_are_different()
    {
        using var project = TemporaryProject.Create();
        var service = new ProjectConfigService(new FileLogger(Path.Combine(project.Path, "test-logs")));

        var result = await service.SaveRfidStationsAsync(project.Path, new[]
        {
            CreateStation("RFID-01", "一号站", "127.0.0.1", 10001, 0x01),
            CreateStation("RFID-02", "二号站", "127.0.0.1", 10002, 0x01)
        });

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public async Task Save_allows_same_protocol_address_when_ips_are_different()
    {
        using var project = TemporaryProject.Create();
        var service = new ProjectConfigService(new FileLogger(Path.Combine(project.Path, "test-logs")));

        var result = await service.SaveRfidStationsAsync(project.Path, new[]
        {
            CreateStation("RFID-01", "一号站", "198.51.100.101", 10000, 0x01),
            CreateStation("RFID-02", "二号站", "198.51.100.102", 10000, 0x01)
        });

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public async Task Save_rejects_only_an_exact_duplicate_ip_port_and_protocol_tuple()
    {
        using var project = TemporaryProject.Create();
        var service = new ProjectConfigService(new FileLogger(Path.Combine(project.Path, "test-logs")));

        var result = await service.SaveRfidStationsAsync(project.Path, new[]
        {
            CreateStation("RFID-01", "一号站", "198.51.100.101", 10000, 0x01),
            CreateStation("RFID-02", "二号站", "198.51.100.101", 10000, 0x01)
        });

        Assert.False(result.Succeeded);
        Assert.Contains("通信键重复", string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public async Task Save_rejects_an_unknown_station_ownership_yard()
    {
        using var project = TemporaryProject.Create();
        var service = new ProjectConfigService(new FileLogger(Path.Combine(project.Path, "test-logs")));
        var station = CreateStation("RFID-01", "一号站", "127.0.0.1", 62301, 0x31);
        station.YardId = "missing";

        var result = await service.SaveRfidStationsAsync(project.Path, new[] { station });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.IndexOf("所属站场不存在", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public async Task Save_station_rejects_map_binding_to_a_different_communication_yard()
    {
        using var project = TemporaryProject.Create("""
            {
              "Id": "test",
              "Name": "test",
              "RfidStations": [
                {
                  "StationId": "RFID-01",
                  "Name": "一号站",
                  "YardId": "560",
                  "IpAddress": "127.0.0.1",
                  "Port": 62301,
                  "ProtocolAddress": 1,
                  "Enabled": true
                }
              ],
              "Stations": [
                { "Id": "560", "ConfigFile": "560.json" },
                { "Id": "620", "ConfigFile": "620.json" }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(project.Path, "560.json"), "{\"Id\":\"560\",\"Name\":\"560\",\"Devices\":[]}");
        File.WriteAllText(Path.Combine(project.Path, "620.json"), "{\"Id\":\"620\",\"Name\":\"620\",\"Devices\":[]}");
        var service = new ProjectConfigService(new FileLogger(Path.Combine(project.Path, "test-logs")));

        var result = await service.SaveStationAsync(project.Path, new StationConfig
        {
            Id = "620",
            Name = "620",
            Devices = new[]
            {
                new DeviceConfig
                {
                    Id = "map-rfid-01",
                    Name = "620 RFID点位",
                    Type = DeviceType.RfidStation,
                    StationId = "620",
                    RfidStationId = "RFID-01",
                    Enabled = true
                }
            }
        });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.IndexOf("通信归属站场", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public async Task Save_and_load_round_trip_numeric_byte_array_fields()
    {
        using var project = TemporaryProject.Create();
        var service = new ProjectConfigService(new FileLogger(Path.Combine(project.Path, "test-logs")));
        var commandBytes = new byte[] { 1, 2, 3, 4 };
        var requestPayload = Enumerable.Range(5, 28).Select(value => (byte)value).ToArray();

        var save = await service.SaveRfidStationsAsync(project.Path, new[]
        {
            new RfidStationConfig
            {
                StationId = "RFID-01",
                Name = "一号站",
                IpAddress = "127.0.0.1",
                Port = 10001,
                ProtocolAddress = 0x01,
                CommandBytes = commandBytes,
                RequestPayload = requestPayload,
                Enabled = true
            }
        });

        Assert.True(save.Succeeded, string.Join(Environment.NewLine, save.Errors));
        var loaded = await service.LoadAsync(project.Path);

        Assert.True(loaded.Succeeded, string.Join(Environment.NewLine, loaded.Errors));
        var station = Assert.Single(loaded.Project!.RfidStations);
        Assert.Equal(commandBytes, station.CommandBytes);
        Assert.Equal(requestPayload, station.RequestPayload);
    }

    private static RfidStationConfig CreateStation(string id, string name, string ip, int port, byte protocolAddress) => new()
    {
        StationId = id,
        Name = name,
        IpAddress = ip,
        Port = port,
        ProtocolAddress = protocolAddress,
        Enabled = true,
        Mode = 0x04,
        CommandBytes = new byte[4],
        RequestPayload = new byte[28]
    };

    private sealed class TemporaryProject : IDisposable
    {
        private TemporaryProject(string manifest)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MineRailMonitor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, "project.json"), manifest);
        }

        public string Path { get; }

        public static TemporaryProject Create(string? manifest = null) => new(manifest ?? """
            {
              "Id": "test",
              "Name": "test",
              "RfidStations": [],
              "Stations": []
            }
            """);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
