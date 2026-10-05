using MineRailMonitor.Core.Acceptance;

namespace MineRailMonitor.Simulator.Models;

public static class SimulatorStationPresets
{
    private static readonly IReadOnlyList<ushort> Normal11 = AcceptanceVehicleCatalog.Test11Rfids;

    public static IReadOnlyList<SimulatorStationConfig> CreateDefaultSix()
    {
        var ports = new[] { 62001, 62003, 62004, 62005, 62006, 62007 };
        return ports.Select((port, index) => new SimulatorStationConfig
        {
            StationName = $"RFID-{index + 1:00}",
            ListenIp = "127.0.0.1",
            ListenPort = port,
            ProtocolAddress = (byte)(index + 1),
            Enabled = true,
            EmptySlotValue = 0,
            CrcHigh = 0,
            CrcLow = 0
        }).ToArray();
    }

    public static IReadOnlyList<SimulatorStationConfig> CreateDefaultDualYardStations()
    {
        var stations = new List<SimulatorStationConfig>(capacity: 12);
        AddYardStations(stations, "560", new[] { 62001, 10002, 10003, 10004, 10005, 10006 });
        AddYardStations(stations, "620", new[] { 10007, 10008, 10009, 10010, 10011, 10012 });
        return stations;
    }

    private static void AddYardStations(ICollection<SimulatorStationConfig> target, string yardId, IReadOnlyList<int> ports)
    {
        for (var index = 0; index < ports.Count; index++)
        {
            target.Add(new SimulatorStationConfig
            {
                YardId = yardId,
                StationName = $"RFID-{yardId}-{index + 1:00}",
                ListenIp = "127.0.0.1",
                ListenPort = ports[index],
                ProtocolAddress = (byte)(index + 1),
                Enabled = true,
                EmptySlotValue = 0,
                CrcHigh = 0,
                CrcLow = 0
            });
        }
    }

    public static void ApplyAllNormal(IEnumerable<SimulatorStationContext> stations)
    {
        if (stations is null)
        {
            throw new ArgumentNullException(nameof(stations));
        }

        foreach (var station in stations)
        {
            station.LoadScenario("11节正常通过", Normal11);
        }
    }

    public static void ApplyOneNormalFiveOffline(IEnumerable<SimulatorStationContext> stations)
    {
        if (stations is null)
        {
            throw new ArgumentNullException(nameof(stations));
        }

        var stationList = stations.ToArray();
        foreach (var station in stationList)
        {
            station.Config.Enabled = false;
            station.Stop();
        }

        if (stationList.Length > 0)
        {
            stationList[0].Config.Enabled = true;
            stationList[0].LoadScenario("11节正常通过", Normal11);
        }
    }

    public static void ApplySameAddressIsolation(IReadOnlyList<SimulatorStationContext> stations)
    {
        if (stations is null)
        {
            throw new ArgumentNullException(nameof(stations));
        }
        if (stations.Count < 2)
        {
            throw new ArgumentException("同Address隔离预设至少需要两个虚拟基站。", nameof(stations));
        }

        stations[0].Config.ListenPort = 62001;
        stations[0].Config.ProtocolAddress = 0x01;
        stations[1].Config.ListenPort = 62003;
        stations[1].Config.ProtocolAddress = 0x01;
    }
}
