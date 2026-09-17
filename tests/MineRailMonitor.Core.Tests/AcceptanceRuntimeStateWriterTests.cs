using System.Net;
using System.Text.Json;
using MineRailMonitor.Core.Acceptance;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class AcceptanceRuntimeStateWriterTests
{
    [Fact]
    public void Write_creates_valid_atomic_snapshot_with_lifecycle_history()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runtime-state.json");
        var store = new InMemoryPassageRecordStore();
        var coordinator = new RfidRuntimeCoordinator(
            new byte[] { 0x01 },
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 2 },
            store);

        try
        {
            coordinator.ProcessFrame(CreateFrame(DateTimeOffset.Now, new ushort[] { 0x0001, 0x0011 }));
            using var writer = new AcceptanceRuntimeStateWriter(path, coordinator);
            writer.Write("frame");

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.Equal("frame", root.GetProperty("Trigger").GetString());
            Assert.Equal("Recognizing", root.GetProperty("Stations")[0].GetProperty("LifecycleState").GetString());
            Assert.Contains(root.GetProperty("History").EnumerateArray(), item =>
                item.GetProperty("Stations")[0].GetProperty("Detected").GetInt32() == 2);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Clear_command_snapshot_captures_the_clearing_state_before_the_response()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runtime-state.json");
        var store = new InMemoryPassageRecordStore();
        var coordinator = new RfidRuntimeCoordinator(
            new byte[] { 0x01 },
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 2 },
            store);
        var values = Enumerable.Range(0, 11)
            .Select(index => (ushort)(index == 0 ? 0x0001 : 0x0010 + index))
            .ToArray();

        try
        {
            using var writer = new AcceptanceRuntimeStateWriter(path, coordinator);
            coordinator.ProcessFrame(CreateFrame(DateTimeOffset.Now, values));
            coordinator.CommandSent += (address, command, _) => writer.Write("clear-command-sent");
            coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, DateTimeOffset.Now);

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Contains(document.RootElement.GetProperty("History").EnumerateArray(), item =>
                item.GetProperty("Trigger").GetString() == "clear-command-sent" &&
                item.GetProperty("Stations")[0].GetProperty("LifecycleState").GetString() == "Clearing");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Write_aggregates_runtime_states_from_both_yards()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runtime-state.json");
        var store = new InMemoryPassageRecordStore();
        var settings = new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 2 };
        var first = new RfidRuntimeCoordinator(new byte[] { 0x01 }, settings, store);
        var second = new RfidRuntimeCoordinator(new byte[] { 0x04 }, settings, store);

        try
        {
            first.ProcessFrame(CreateFrame(DateTimeOffset.Now, new ushort[] { 0x0001, 0x0011 }));
            second.ProcessFrame(new RfidStationFrame
            {
                StationAddress = 0x04,
                Mode = 0x04,
                HeadRfid = 0x0004,
                RawRfidSlots = new ushort[] { 0x0004, 0x0041 },
                ValidRfids = new ushort[] { 0x0004, 0x0041 },
                ReportedCardCount = 2,
                ActualNonZeroSlotCount = 2,
                ReceivedAt = DateTimeOffset.Now,
                SourceEndpoint = new IPEndPoint(IPAddress.Loopback, 63111)
            });

            using var writer = new AcceptanceRuntimeStateWriter(path, new[] { first, second });
            writer.Write("dual-yard-frame");

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var stations = document.RootElement.GetProperty("Stations").EnumerateArray().ToArray();
            Assert.Equal(2, stations.Length);
            Assert.Contains(stations, item => item.GetProperty("StationAddress").GetByte() == 1);
            Assert.Contains(stations, item => item.GetProperty("StationAddress").GetByte() == 4);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static RfidStationFrame CreateFrame(DateTimeOffset at, IReadOnlyList<ushort> values)
    {
        var slots = new ushort[14];
        values.ToArray().CopyTo(slots, 0);
        return new RfidStationFrame
        {
            StationAddress = 0x01,
            Mode = 0x04,
            HeadRfid = slots[0],
            RawRfidSlots = slots,
            ValidRfids = slots.Where(value => value != 0).ToArray(),
            ReportedCardCount = (byte)values.Count,
            ActualNonZeroSlotCount = values.Count,
            ReceivedAt = at,
            SourceEndpoint = new IPEndPoint(IPAddress.Loopback, 62101)
        };
    }
}
