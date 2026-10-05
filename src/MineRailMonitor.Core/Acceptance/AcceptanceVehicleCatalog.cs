using System;
using System.Collections.Generic;
using System.Linq;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Acceptance;

public static class AcceptanceVehicleCatalog
{
    private static readonly ushort[] Test11Tags =
    {
        0xA001, 0xA002, 0xA003, 0xA004, 0xA005, 0xA006,
        0xA007, 0xA008, 0xA009, 0xA010, 0xA011
    };

    private static readonly ushort[] Test07Tags =
    {
        0xB001, 0xB002, 0xB003, 0xB004, 0xB005, 0xB006, 0xB007
    };

    public static IReadOnlyList<ushort> Test11Rfids => Array.AsReadOnly(Test11Tags);

    public static IReadOnlyList<ushort> Test07Rfids => Array.AsReadOnly(Test07Tags);

    public static IReadOnlyList<VehicleConfig> CreateVehicles() => new[]
    {
        CreateVehicle("TEST-11", "11节验收车辆", Test11Tags),
        CreateVehicle("TEST-07", "7节验收车辆", Test07Tags)
    };

    private static VehicleConfig CreateVehicle(string vehicleNo, string vehicleName, IReadOnlyList<ushort> rfids) => new()
    {
        VehicleNo = vehicleNo,
        HeadRfid = rfids[0],
        VehicleName = vehicleName,
        Enabled = true,
        Tags = rfids.Select((rfid, position) => new VehicleTagConfig
        {
            Position = position,
            Rfid = rfid,
            Type = position == 0 ? VehicleTagType.Head : VehicleTagType.Carriage
        }).ToList()
    };
}
