namespace MineRailMonitor.Simulator.Acceptance;

public sealed class AcceptanceScenarioDefinition
{
    public AcceptanceScenarioDefinition(string name, IEnumerable<AcceptanceTrainPlan> trains, bool reappearsOldTags = false)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Scenario name must not be empty.", nameof(name));
        if (trains is null) throw new ArgumentNullException(nameof(trains));

        Name = name;
        Trains = trains.ToArray();
        if (Trains.Count == 0)
        {
            throw new ArgumentException("A scenario must contain at least one train plan.", nameof(trains));
        }

        ReappearsOldTags = reappearsOldTags;
    }

    public string Name { get; }

    public IReadOnlyList<AcceptanceTrainPlan> Trains { get; }

    public bool ReappearsOldTags { get; }
}

public sealed class AcceptanceTrainPlan
{
    public AcceptanceTrainPlan(byte stationAddress, IEnumerable<ushort> vehicleRfids, IEnumerable<int>? occupiedSlotIndexes = null)
    {
        if (vehicleRfids is null) throw new ArgumentNullException(nameof(vehicleRfids));

        StationAddress = stationAddress;
        VehicleRfids = vehicleRfids.ToArray();
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
}

public static class AcceptanceScenario
{
    public static IReadOnlyList<AcceptanceScenarioDefinition> All { get; } = new[]
    {
        new AcceptanceScenarioDefinition(
            "Normal11",
            new[] { Train(0x01, new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019, 0x001A }) }),
        new AcceptanceScenarioDefinition(
            "Uncoupling10",
            new[] { Train(0x01, new ushort[] { 0x0002, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019 }) }),
        new AcceptanceScenarioDefinition(
            "TwoConsecutiveTrains",
            new[]
            {
                Train(0x01, new ushort[] { 0x0003, 0x0021, 0x0022, 0x0023, 0x0024, 0x0025, 0x0026, 0x0027, 0x0028, 0x0029, 0x002A }),
                Train(0x01, new ushort[] { 0x0004, 0x0031, 0x0032, 0x0033, 0x0034, 0x0035, 0x0036, 0x0037, 0x0038, 0x0039, 0x003A })
            }),
        new AcceptanceScenarioDefinition(
            "TwoStationsConcurrent",
            new[]
            {
                Train(0x01, new ushort[] { 0x0001, 0x0041, 0x0042, 0x0043, 0x0044, 0x0045, 0x0046, 0x0047, 0x0048, 0x0049, 0x004A }),
                Train(0x04, new ushort[] { 0x0002, 0x0051, 0x0052, 0x0053, 0x0054, 0x0055, 0x0056, 0x0057, 0x0058, 0x0059 })
            }),
        new AcceptanceScenarioDefinition(
            "ClearReappearingTags",
            new[] { Train(0x01, new ushort[] { 0x0001, 0x0061, 0x0062, 0x0063, 0x0064, 0x0065, 0x0066, 0x0067, 0x0068, 0x0069, 0x006A }) },
            reappearsOldTags: true),
        new AcceptanceScenarioDefinition(
            "SparseSlots",
            new[]
            {
                Train(
                    0x01,
                    new ushort[] { 0x001D, 0x0021, 0x0022, 0x0023, 0x0024, 0x0025, 0x0026, 0x0027, 0x0028, 0x0029, 0x002A },
                    new[] { 2, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 })
            }),
        new AcceptanceScenarioDefinition(
            "MultipleHeads",
            new[] { Train(0x01, new ushort[] { 0x0001, 0x0003, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019 }) }),
        new AcceptanceScenarioDefinition(
            "NoHead",
            new[] { Train(0x01, new ushort[] { 0x000B, 0x000C, 0x000D, 0x000E, 0x000F, 0x0010, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015 }) })
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

    private static AcceptanceTrainPlan Train(byte stationAddress, IEnumerable<ushort> values, IEnumerable<int>? slots = null) =>
        new(stationAddress, values, slots);
}
