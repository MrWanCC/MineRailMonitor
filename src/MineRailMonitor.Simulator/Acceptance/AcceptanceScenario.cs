using MineRailMonitor.Core.Acceptance;

namespace MineRailMonitor.Simulator.Acceptance;

public sealed class AcceptanceScenarioDefinition
{
    public AcceptanceScenarioDefinition(
        string name,
        IEnumerable<AcceptanceTrainPlan> trains,
        bool reappearsOldTags = false,
        bool sendNextTrainImmediatelyAfterClear = false,
        string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Scenario name must not be empty.", nameof(name));
        if (trains is null) throw new ArgumentNullException(nameof(trains));

        Name = name;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName!;
        Trains = trains.ToArray();
        if (Trains.Count == 0)
        {
            throw new ArgumentException("A scenario must contain at least one train plan.", nameof(trains));
        }

        ReappearsOldTags = reappearsOldTags;
        SendNextTrainImmediatelyAfterClear = sendNextTrainImmediatelyAfterClear;
    }

    public string Name { get; }

    public string DisplayName { get; }

    public IReadOnlyList<AcceptanceTrainPlan> Trains { get; }

    public bool ReappearsOldTags { get; }

    public bool SendNextTrainImmediatelyAfterClear { get; }
}

public sealed class AcceptanceTrainPlan
{
    public AcceptanceTrainPlan(
        byte stationAddress,
        IEnumerable<ushort> vehicleRfids,
        IEnumerable<int>? occupiedSlotIndexes = null,
        TimeSpan? delayBeforeFinalRfid = null,
        int? delayBeforeRfidIndex = null,
        TimeSpan? delayBeforeRfid = null)
    {
        if (vehicleRfids is null) throw new ArgumentNullException(nameof(vehicleRfids));

        StationAddress = stationAddress;
        VehicleRfids = vehicleRfids.ToArray();
        DelayBeforeFinalRfid = delayBeforeFinalRfid ?? TimeSpan.Zero;
        if (DelayBeforeFinalRfid < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delayBeforeFinalRfid));
        }
        DelayBeforeRfidIndex = delayBeforeRfidIndex;
        DelayBeforeRfid = delayBeforeRfid ?? TimeSpan.Zero;
        if (DelayBeforeRfid < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delayBeforeRfid));
        }
        if (DelayBeforeRfidIndex.HasValue &&
            (DelayBeforeRfidIndex.Value < 0 || DelayBeforeRfidIndex.Value >= VehicleRfids.Count))
        {
            throw new ArgumentOutOfRangeException(nameof(delayBeforeRfidIndex));
        }
        if (VehicleRfids.Count == 0 || VehicleRfids.Count > 14)
        {
            throw new ArgumentException("A train must contain from 1 to 14 RFID values.", nameof(vehicleRfids));
        }

        OccupiedSlotIndexes = (occupiedSlotIndexes ?? Enumerable.Range(0, VehicleRfids.Count)).ToArray();
        if (OccupiedSlotIndexes.Count != VehicleRfids.Count ||
            OccupiedSlotIndexes.Any(index => index < 0 || index >= 14) ||
            OccupiedSlotIndexes.Distinct().Count() != OccupiedSlotIndexes.Count)
        {
            throw new ArgumentException("Occupied slot indexes must be unique values from 0 to 13 and match the RFID count.", nameof(occupiedSlotIndexes));
        }

        if (VehicleRfids.Distinct().Count() != VehicleRfids.Count || VehicleRfids.Any(value => value == 0))
        {
            throw new ArgumentException("Train RFID values must be unique and non-zero.", nameof(vehicleRfids));
        }
    }

    public byte StationAddress { get; }

    public IReadOnlyList<ushort> VehicleRfids { get; }

    public IReadOnlyList<int> OccupiedSlotIndexes { get; }

    public TimeSpan DelayBeforeFinalRfid { get; }

    public int? DelayBeforeRfidIndex { get; }

    public TimeSpan DelayBeforeRfid { get; }
}

public static class AcceptanceScenario
{
    public static IReadOnlyList<AcceptanceScenarioDefinition> All { get; } = new[]
    {
        new AcceptanceScenarioDefinition(
            "Normal11",
            new[] { Train(0x01, new ushort[] { 0x03E9, 0x07D1, 0x07D2, 0x07D3, 0x07D4, 0x07D5, 0x07D6, 0x07D7, 0x07D8, 0x07D9, 0x07DA }) }),
        new AcceptanceScenarioDefinition(
            "Uncoupling10",
            new[] { Train(0x01, new ushort[] { 0x03E9, 0x07D1, 0x07D2, 0x07D3, 0x07D4, 0x07D5, 0x07D6, 0x07D7, 0x07D8, 0x07D9 }) }),
        new AcceptanceScenarioDefinition(
            "TwoConsecutiveTrains",
            new[]
            {
                Train(0x01, new ushort[] { 0x03E9, 0x07D1, 0x07D2, 0x07D3, 0x07D4, 0x07D5, 0x07D6, 0x07D7, 0x07D8, 0x07D9, 0x07DA }),
                Train(0x01, new ushort[] { 0x03EA, 0x07DB, 0x07DC, 0x07DD, 0x07DE, 0x07DF, 0x07E0, 0x07E1, 0x07E2, 0x07E3, 0x07E4 })
            }),
        new AcceptanceScenarioDefinition(
            "TwoStationsConcurrent",
            new[]
            {
                Train(0x01, new ushort[] { 0x03E9, 0x07D1, 0x07D2, 0x07D3, 0x07D4, 0x07D5, 0x07D6, 0x07D7, 0x07D8, 0x07D9, 0x07DA }),
                Train(0x04, new ushort[] { 0x03EB, 0x07E5, 0x07E6, 0x07E7, 0x07E8, 0x07E9, 0x07EA, 0x07EB, 0x07EC, 0x07ED, 0x07EE })
            }),
        new AcceptanceScenarioDefinition(
            "ClearReappearingTags",
            new[] { Train(0x01, new ushort[] { 0x03E9, 0x07D1, 0x07D2, 0x07D3, 0x07D4, 0x07D5, 0x07D6, 0x07D7, 0x07D8, 0x07D9, 0x07DA }) },
            reappearsOldTags: true),
        new AcceptanceScenarioDefinition(
            "SparseSlots",
            new[]
            {
                Train(
                    0x01,
                    new ushort[] { 0x03E9, 0x07D1, 0x07D2, 0x07D3, 0x07D4, 0x07D5, 0x07D6, 0x07D7, 0x07D8, 0x07D9, 0x07DA },
                    new[] { 2, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 })
            }),
        new AcceptanceScenarioDefinition(
            "MultipleHeads",
            new[] { Train(0x01, new ushort[] { 0x03E9, 0x03EA, 0x07D1, 0x07D2, 0x07D3, 0x07D4, 0x07D5, 0x07D6, 0x07D7, 0x07D8, 0x07D9 }) }),
        new AcceptanceScenarioDefinition(
            "NoHead",
            new[] { Train(0x01, new ushort[] { 0x07D1, 0x07D2, 0x07D3, 0x07D4, 0x07D5, 0x07D6, 0x07D7, 0x07D8, 0x07D9, 0x07DA, 0x07DB }, Enumerable.Range(1, 11)) }),
        new AcceptanceScenarioDefinition(
            "VehicleNormal7",
            new[] { Train(0x01, new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016 }) }),
        new AcceptanceScenarioDefinition(
            "VehicleUncoupling7Of6",
            new[] { Train(0x01, new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015 }) }),
        new AcceptanceScenarioDefinition(
            "VehicleUnconfiguredFallback",
            new[] { Train(0x01, new ushort[] { 0x0002, 0x0021, 0x0022, 0x0023, 0x0024, 0x0025 }) }),
        new AcceptanceScenarioDefinition(
            "VehicleLateSeventh7",
            new[] { Train(0x01, new ushort[] { 0x1001, 0x1101, 0x1102, 0x1103, 0x1104, 0x1105, 0x1106 }) }),
        new AcceptanceScenarioDefinition(
            "VehicleRepeatedTagReads",
            new[] { Train(0x01, new ushort[] { 0x1001 }) }),
        new AcceptanceScenarioDefinition(
            "VehicleHeadAppearsLate7",
            new[]
            {
                Train(
                    0x01,
                    new ushort[] { 0x1101, 0x1102, 0x1103, 0x1104, 0x1105, 0x1106, 0x1001 },
                    new[] { 1, 2, 3, 4, 5, 6, 0 })
            }),
        new AcceptanceScenarioDefinition(
            "UdpU01Normal11",
            new[] { Train(0x01, StandardVehicle(0x1001, 0x1101)) }),
        new AcceptanceScenarioDefinition(
            "UdpU02Uncoupling10Of11",
            new[] { Train(0x01, StandardVehicle(0x1001, 0x1101).Take(10)) }),
        new AcceptanceScenarioDefinition(
            "UdpU03LateEleventhCompletes",
            new[] { Train(0x01, StandardVehicle(0x1001, 0x1101), delayBeforeFinalRfid: TimeSpan.FromSeconds(27)) }),
        new AcceptanceScenarioDefinition(
            "UdpU04RepeatedTagsTimeout",
            new[] { Train(0x01, StandardVehicle(0x1001, 0x1101).Take(10)) }),
        new AcceptanceScenarioDefinition(
            "UdpU05HeadAppearsLate",
            new[]
            {
                Train(
                    0x01,
                    StandardVehicle(0x1001, 0x1101).Skip(1).Concat(new ushort[] { 0x1001 }),
                    Enumerable.Range(1, 10).Concat(new[] { 0 }))
            }),
        new AcceptanceScenarioDefinition(
            "UdpU06UnknownHeadFallback",
            new[] { Train(0x01, StandardVehicle(0x2001, 0x2101)) }),
        new AcceptanceScenarioDefinition(
            "UdpU07ConsecutiveAfterClear",
            new[]
            {
                Train(0x01, StandardVehicle(0x1001, 0x1101)),
                Train(0x01, StandardVehicle(0x1002, 0x1201))
            }),
        new AcceptanceScenarioDefinition(
            "UdpU08ImmediateConsecutive",
            new[]
            {
                Train(0x01, StandardVehicle(0x1001, 0x1101)),
                Train(0x01, StandardVehicle(0x1002, 0x1201))
            },
            sendNextTrainImmediatelyAfterClear: true),
        new AcceptanceScenarioDefinition(
            "U09_11CarMissingFive",
            new[] { Train(0x01, AcceptanceVehicleCatalog.Test11Rfids.Take(6)) },
            displayName: "11节缺5节"),
        new AcceptanceScenarioDefinition(
            "U10_11CarLateCompletion",
            new[]
            {
                Train(
                    0x01,
                    AcceptanceVehicleCatalog.Test11Rfids,
                    delayBeforeRfidIndex: 6,
                    delayBeforeRfid: TimeSpan.FromSeconds(25))
            },
            displayName: "11节补扫完成"),
        new AcceptanceScenarioDefinition(
            "U11_CustomCountNotFixed11",
            new[] { Train(0x01, AcceptanceVehicleCatalog.Test07Rfids) },
            displayName: "7节正常通过"),
        new AcceptanceScenarioDefinition(
            "U12_Test11Normal",
            new[] { Train(0x01, AcceptanceVehicleCatalog.Test11Rfids) },
            displayName: "11节正常通过"),
        new AcceptanceScenarioDefinition(
            "U13_Test11MissingOne",
            new[] { Train(0x01, AcceptanceVehicleCatalog.Test11Rfids.Take(10)) },
            displayName: "11节缺1节"),
        new AcceptanceScenarioDefinition(
            "U14_UnverifiedNoFallback",
            new[] { Train(0x01, new ushort[] { 0xC001, 0xC101, 0xC102, 0xC103 }) },
            displayName: "未知车辆 · 无回退未判定")
    };

    public static AcceptanceScenarioDefinition Parse(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Scenario name must not be empty.", nameof(name));
        }

        var scenario = All.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        return scenario ?? throw new ArgumentException($"Unknown acceptance scenario: {name}", nameof(name));
    }

    private static AcceptanceTrainPlan Train(
        byte stationAddress,
        IEnumerable<ushort> values,
        IEnumerable<int>? slots = null,
        TimeSpan? delayBeforeFinalRfid = null,
        int? delayBeforeRfidIndex = null,
        TimeSpan? delayBeforeRfid = null) =>
        new(stationAddress, values, slots, delayBeforeFinalRfid, delayBeforeRfidIndex, delayBeforeRfid);

    private static ushort[] StandardVehicle(ushort headRfid, ushort firstWagonRfid) =>
        new[] { headRfid }.Concat(Enumerable.Range(0, 10).Select(index => (ushort)(firstWagonRfid + index))).ToArray();
}
