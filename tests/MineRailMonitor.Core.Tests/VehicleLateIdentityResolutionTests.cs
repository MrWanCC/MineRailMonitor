using System.Net;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class VehicleLateIdentityResolutionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Later_position_zero_match_fills_identity_without_changing_session_expected_count()
    {
        var station = CreateStation(expectedVehicleCount: null);
        var coordinator = CreateCoordinator(
            station,
            new[] { CreateVehicle(0x1001, "K001", "1号矿车", 4) },
            globalExpectedCount: 6);

        var session = coordinator.ProcessFrame(CreateFrame(station, Start, 0, 0x1101, 0x1102, 0x1001));

        Assert.NotNull(session);
        Assert.Null(session!.VehicleNo);
        Assert.Equal(6, session.ExpectedVehicleCount);

        coordinator.ProcessFrame(CreateFrame(station, Start.AddSeconds(1), 0x1001, 0x1101, 0x1102));

        Assert.Equal("K001", session.VehicleNo);
        Assert.Equal("1号矿车", session.VehicleName);
        Assert.Equal((ushort?)0x1001, session.VehicleHeadRfid);
        Assert.Equal(6, session.ExpectedVehicleCount);
        Assert.Equal(3, session.DetectedVehicleCount);
    }

    [Fact]
    public void Existing_identity_is_not_matched_again_or_replaced_by_later_head_slot()
    {
        var station = CreateStation(expectedVehicleCount: null);
        var coordinator = CreateCoordinator(station, new[]
        {
            CreateVehicle(0x1001, "K001", "1号矿车", 7),
            CreateVehicle(0x1002, "K002", "2号矿车", 7)
        });
        var matchedResults = new List<VehicleMatchResult>();
        coordinator.VehicleMatchResolved += result =>
        {
            if (result.IsMatched)
            {
                matchedResults.Add(result);
            }
        };

        var session = coordinator.ProcessFrame(CreateFrame(station, Start, 0x1001, 0x1101));
        coordinator.ProcessFrame(CreateFrame(station, Start.AddSeconds(1), 0x1002, 0x1101, 0x1102));

        Assert.NotNull(session);
        Assert.Equal("K001", session!.VehicleNo);
        Assert.Equal("1号矿车", session.VehicleName);
        Assert.Equal((ushort?)0x1001, session.VehicleHeadRfid);
        Assert.Equal(7, session.ExpectedVehicleCount);
        Assert.Single(matchedResults);
    }

    [Fact]
    public void Session_stays_unmatched_when_no_configured_head_appears()
    {
        var station = CreateStation(expectedVehicleCount: null);
        var eventLogger = new RecordingPassageEventLogger();
        var coordinator = CreateCoordinator(
            station,
            new[] { CreateVehicle(0x1001, "K001", "1号矿车", 4) },
            eventLogger);

        var session = coordinator.ProcessFrame(CreateFrame(station, Start, 0, 0x1101, 0x1102));
        coordinator.ProcessFrame(CreateFrame(station, Start.AddSeconds(1), 0x2222, 0x1101, 0x1102));

        Assert.NotNull(session);
        Assert.Null(session!.VehicleNo);
        Assert.Null(session.VehicleName);
        Assert.Null(session.VehicleHeadRfid);
        Assert.DoesNotContain(eventLogger.Events, item => item.EventType == "VEHICLE_MATCH_LATE_RESOLVED");
    }

    [Fact]
    public void Late_identity_is_saved_in_passage_snapshot_and_logged_with_passage_id()
    {
        var station = CreateStation(expectedVehicleCount: null);
        var store = new InMemoryPassageRecordStore();
        var eventLogger = new RecordingPassageEventLogger();
        var coordinator = CreateCoordinator(
            station,
            new[] { CreateVehicle(0x1001, "K001", "1号矿车", 4) },
            eventLogger,
            store,
            globalExpectedCount: 6);

        var session = coordinator.ProcessFrame(CreateFrame(station, Start, 0, 0x1101, 0x1102, 0x1001));
        var discoveredAt = Start.AddSeconds(1);
        coordinator.ProcessFrame(CreateFrame(station, discoveredAt, 0x1001, 0x1101, 0x1102));
        coordinator.Evaluate(discoveredAt.AddSeconds(2));

        var record = Assert.Single(store.Records);
        Assert.Equal(PassageOutcome.UncouplingAlarm, record.Outcome);
        Assert.Equal("K001", record.VehicleNo);
        Assert.Equal("1号矿车", record.VehicleName);
        Assert.Equal((ushort?)0x1001, record.VehicleHeadRfid);
        Assert.Equal(6, record.ExpectedVehicleCount);
        Assert.Equal(3, record.DetectedVehicleCount);

        var lateEvent = Assert.Single(eventLogger.Events, item => item.EventType == "VEHICLE_MATCH_LATE_RESOLVED");
        Assert.Equal(session!.PassageId, lateEvent.PassageId);
        Assert.Equal(discoveredAt, lateEvent.OccurredAt);
        Assert.Equal((ushort?)0x1001, lateEvent.VehicleHeadRfid);
        Assert.Equal("K001", lateEvent.VehicleNo);
        Assert.Equal("1号矿车", lateEvent.VehicleName);
    }

    private static RfidRuntimeCoordinator CreateCoordinator(
        RfidStationConfig station,
        IReadOnlyList<VehicleConfig> vehicles,
        RecordingPassageEventLogger? eventLogger = null,
        InMemoryPassageRecordStore? store = null,
        int globalExpectedCount = 11) => new(
            new[] { station },
            new RfidSettings { ExpectedVehicleCount = globalExpectedCount, InterVehicleTimeoutSeconds = 2 },
            store ?? new InMemoryPassageRecordStore(),
            new RfidRuntimePolicy(TimeSpan.FromSeconds(10)),
            vehicles,
            eventLogger);

    private static RfidStationConfig CreateStation(int? expectedVehicleCount) => new()
    {
        StationId = "RFID-TEST-01",
        Name = "测试基站",
        IpAddress = "127.0.0.1",
        Port = 62001,
        ProtocolAddress = 0x01,
        ExpectedVehicleCount = expectedVehicleCount,
        Enabled = true
    };

    private static VehicleConfig CreateVehicle(ushort headRfid, string vehicleNo, string vehicleName, int sectionCount) => new()
    {
        VehicleNo = vehicleNo,
        HeadRfid = headRfid,
        VehicleName = vehicleName,
        Enabled = true,
        Tags = Enumerable.Range(0, sectionCount)
            .Select(position => new VehicleTagConfig
            {
                Position = position,
                Rfid = position == 0 ? headRfid : (ushort)(0x1100 + position),
                Type = position == 0 ? VehicleTagType.Head : VehicleTagType.Carriage
            })
            .ToList()
    };

    private static RfidStationFrame CreateFrame(RfidStationConfig station, DateTimeOffset at, params ushort[] rfids)
    {
        var slots = new ushort[14];
        rfids.CopyTo(slots, 0);
        station.TryResolveEndpoint(out var endpoint);
        return new RfidStationFrame
        {
            StationAddress = station.ProtocolAddress,
            ReceivedAt = at,
            RawRfidSlots = slots,
            HeadRfid = slots[0],
            ValidRfids = slots.Where(rfid => rfid != 0).ToArray(),
            ActualNonZeroSlotCount = slots.Count(rfid => rfid != 0),
            SourceEndpoint = endpoint
        };
    }

    private sealed class RecordingPassageEventLogger : IPassageEventLogger
    {
        public List<PassageEvent> Events { get; } = new();

        public void Write(PassageEvent passageEvent) => Events.Add(passageEvent);
    }
}
