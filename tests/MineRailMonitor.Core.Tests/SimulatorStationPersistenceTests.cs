using System.Net;
using MineRailMonitor.Simulator.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class SimulatorStationPersistenceTests
{
    [Fact]
    public void Save_and_load_round_trip_custom_station_endpoints()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "stations.json");
        var expected = new[]
        {
            new SimulatorStationConfig
            {
                StationName = "自定义站",
                YardId = "620",
                ListenIp = IPAddress.Loopback.ToString(),
                ListenPort = 62123,
                ProtocolAddress = 0x2A,
                Enabled = false,
                EmptySlotValue = 0xFFFF,
                CrcHigh = 0x12,
                CrcLow = 0x34
            },
            new SimulatorStationConfig
            {
                StationName = "第二站",
                ListenIp = "127.0.0.1",
                ListenPort = 62124,
                ProtocolAddress = 0x2B,
                Enabled = true
            }
        };

        try
        {
            SimulatorStationPersistence.Save(path, expected);

            var actual = SimulatorStationPersistence.Load(path);

            Assert.Equal(2, actual.Count);
            Assert.Equal("自定义站", actual[0].StationName);
            Assert.Equal("620", actual[0].YardId);
            Assert.Equal(62123, actual[0].ListenPort);
            Assert.Equal((byte)0x2A, actual[0].ProtocolAddress);
            Assert.False(actual[0].Enabled);
            Assert.Equal((ushort)0xFFFF, actual[0].EmptySlotValue);
            Assert.Equal((byte)0x12, actual[0].CrcHigh);
            Assert.Equal((byte)0x34, actual[0].CrcLow);
            Assert.Equal("第二站", actual[1].StationName);
            Assert.Equal(62124, actual[1].ListenPort);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Missing_or_invalid_configuration_file_returns_no_stations()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "stations.json");

        try
        {
            Assert.Empty(SimulatorStationPersistence.Load(path));
            File.WriteAllText(path, "{ not valid json }");
            Assert.Empty(SimulatorStationPersistence.Load(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Default_six_preset_uses_editable_loopback_endpoints_and_skips_62002()
    {
        var stations = SimulatorStationPresets.CreateDefaultSix();

        Assert.Equal(6, stations.Count);
        Assert.Equal(new[] { 62001, 62003, 62004, 62005, 62006, 62007 }, stations.Select(item => item.ListenPort));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, stations.Select(item => item.ProtocolAddress));
        Assert.All(stations, station =>
        {
            Assert.Equal("127.0.0.1", station.ListenIp);
            Assert.True(station.Enabled);
        });
    }

    [Fact]
    public void Default_dual_yard_preset_creates_six_independent_stations_per_yard()
    {
        var stations = SimulatorStationPresets.CreateDefaultDualYardStations();

        Assert.Equal(12, stations.Count);
        Assert.Equal(6, stations.Count(item => item.YardId == "560"));
        Assert.Equal(6, stations.Count(item => item.YardId == "620"));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, stations.Where(item => item.YardId == "560").Select(item => item.ProtocolAddress));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, stations.Where(item => item.YardId == "620").Select(item => item.ProtocolAddress));
        Assert.Equal(12, stations.Select(item => item.ListenPort).Distinct().Count());
        Assert.Equal(new[] { "RFID-560-01", "RFID-560-02", "RFID-560-03", "RFID-560-04", "RFID-560-05", "RFID-560-06" },
            stations.Where(item => item.YardId == "560").Select(item => item.StationName));
        Assert.Equal(new[] { "RFID-620-01", "RFID-620-02", "RFID-620-03", "RFID-620-04", "RFID-620-05", "RFID-620-06" },
            stations.Where(item => item.YardId == "620").Select(item => item.StationName));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MineRailMonitor.Simulator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
