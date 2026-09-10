using System.Text.Json;
using System.Text.Json.Serialization;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class ConfigurationModelTests
{
    [Fact]
    public void DeviceConfig_preserves_null_protocol_address_without_deriving_it_from_name()
    {
        const string json = """
            {
              "Id": "Y6-10",
              "Name": "Y6-10",
              "Type": "RfidReader",
              "StationId": "560",
              "CadX": 2771.8203,
              "CadY": 1916.0352,
              "ProtocolAddress": null,
              "Enabled": true
            }
            """;

        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());

        var device = JsonSerializer.Deserialize<DeviceConfig>(json, options);

        Assert.NotNull(device);
        Assert.Equal("Y6-10", device.Id);
        Assert.Equal(DeviceType.RfidReader, device.Type);
        Assert.Equal(2771.8203, device.CadX, precision: 4);
        Assert.Equal(1916.0352, device.CadY, precision: 4);
        Assert.Null(device.ProtocolAddress);
    }

    [Fact]
    public void StationConfig_contains_devices_loaded_as_configuration_data()
    {
        var station = new StationConfig
        {
            Id = "560",
            Name = "-560 站场",
            BackgroundImage = "maps/560.png",
            CadMinX = 2700,
            CadMaxX = 2800,
            CadMinY = 1900,
            CadMaxY = 2050,
            Devices =
            [
                new DeviceConfig
                {
                    Id = "Y6-11",
                    Name = "Y6-11",
                    Type = DeviceType.RfidReader,
                    StationId = "560",
                    CadX = 2763.2233,
                    CadY = 1963.9322,
                    Enabled = true
                }
            ]
        };

        Assert.Single(station.Devices);
        Assert.Equal("Y6-11", station.Devices[0].Id);
    }
}
