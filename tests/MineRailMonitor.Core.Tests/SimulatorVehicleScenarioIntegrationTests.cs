using System.Net;
using MineRailMonitor.Core.Acceptance;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Simulator.Acceptance;

namespace MineRailMonitor.Core.Tests;

public sealed class SimulatorVehicleScenarioIntegrationTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Normal_vehicle_scenario_matches_configured_vehicle_and_completes_passage()
    {
        var plan = AcceptanceScenario.Parse("VehicleNormal7").Trains.Single();
        var station = CreateStation(expectedVehicleCount: 6);
        var store = new InMemoryPassageRecordStore();
        var events = new RecordingPassageEventLogger();
        var coordinator = CreateCoordinator(station, store, new[] { CreateSevenSectionVehicle() }, eventLogger: events);

        var session = coordinator.ProcessFrame(CreateFrame(station, Start, plan.VehicleRfids));

        Assert.NotNull(session);
        Assert.Equal(7, session!.ExpectedVehicleCount);
        Assert.True(session.VehicleMatchResult!.IsMatched);
        Assert.Equal("矿车001", session.VehicleName);
        Assert.Equal(PassageLifecycleState.Completed, coordinator.States[station.ProtocolAddress].LifecycleState);

        var passage = Assert.Single(store.Records);
        Assert.Equal(PassageOutcome.Completed, passage.Outcome);
        Assert.Equal(7, passage.ExpectedVehicleCount);
        Assert.Equal(7, passage.DetectedVehicleCount);
        Assert.Equal("矿车001", passage.VehicleName);
        Assert.Equal("K001", passage.VehicleNo);
        Assert.Equal((ushort)1, passage.VehicleHeadRfid);
        Assert.DoesNotContain(events.Events, item => item.EventType == PassageEventTypes.Alarm);
    }

    [Fact]
    public void Six_tags_for_a_configured_seven_section_vehicle_enter_existing_uncoupling_alarm()
    {
        var plan = AcceptanceScenario.Parse("VehicleUncoupling7Of6").Trains.Single();
        var station = CreateStation(expectedVehicleCount: 6);
        var store = new InMemoryPassageRecordStore();
        var events = new RecordingPassageEventLogger();
        var coordinator = CreateCoordinator(station, store, new[] { CreateSevenSectionVehicle() }, eventLogger: events);

        coordinator.ProcessFrame(CreateFrame(station, Start, plan.VehicleRfids));
        coordinator.ProcessFrame(CreateFrame(station, Start.AddSeconds(29.9), plan.VehicleRfids));
        coordinator.Evaluate(Start.AddSeconds(30));

        Assert.Equal(PassageLifecycleState.Alarm, coordinator.States[station.ProtocolAddress].LifecycleState);
        var passage = Assert.Single(store.Records);
        Assert.Equal(PassageOutcome.UncouplingAlarm, passage.Outcome);
        Assert.Equal(7, passage.ExpectedVehicleCount);
        Assert.Equal(6, passage.DetectedVehicleCount);
        Assert.Equal("矿车001", passage.VehicleName);
        Assert.Equal("K001", passage.VehicleNo);
        Assert.Equal((ushort)1, passage.VehicleHeadRfid);
        Assert.Equal(1, passage.ExpectedVehicleCount - passage.DetectedVehicleCount);
        Assert.Equal(AlarmStatus.Unconfirmed, passage.AlarmStatus);

        var alarmEvent = Assert.Single(events.Events, item => item.EventType == PassageEventTypes.Alarm);
        Assert.Equal(passage.PassageId, alarmEvent.PassageId);
        Assert.Equal(7, alarmEvent.ExpectedCount);
        Assert.Equal(6, alarmEvent.DetectedCount);
        Assert.Equal(1, alarmEvent.MissingCount);

        var acknowledgedAt = Start.AddSeconds(31);
        coordinator.AcknowledgeAlarm(passage.PassageId, acknowledgedAt);
        Assert.Equal(AlarmStatus.Acknowledged, Assert.Single(store.Records).AlarmStatus);
        Assert.Equal(acknowledgedAt, Assert.Single(store.Records).AlarmAcknowledgedAt);

        coordinator.MarkCommandSent(station.ProtocolAddress, MineRailMonitor.Core.Communication.RfidPollCommand.Clear, Start.AddSeconds(32));
        coordinator.ProcessFrame(CreateFrame(station, Start.AddSeconds(32.1), Array.Empty<ushort>()));
        coordinator.ProcessFrame(CreateFrame(station, Start.AddSeconds(32.2), Array.Empty<ushort>()));

        Assert.Equal(AlarmStatus.Recovered, Assert.Single(store.Records).AlarmStatus);
        Assert.Equal(Start.AddSeconds(32.2), Assert.Single(store.Records).AlarmRecoveredAt);
        Assert.Contains(events.Events, item => item.EventType == PassageEventTypes.Clear && item.PassageId == passage.PassageId);
    }

    [Fact]
    public void Missing_tag_that_arrives_before_timeout_completes_normally_without_position_validation()
    {
        var plan = AcceptanceScenario.Parse("VehicleLateSeventh7").Trains.Single();
        var station = CreateStation(expectedVehicleCount: 6);
        var store = new InMemoryPassageRecordStore();
        var vehicle = CreateVehicle(0x1001, "K001", "矿车001", plan.VehicleRfids);
        var coordinator = CreateCoordinator(station, store, new[] { vehicle });

        coordinator.ProcessFrame(CreateFrame(station, Start, plan.VehicleRfids.Take(6).ToArray()));
        var session = coordinator.ProcessFrame(CreateFrame(station, Start.AddSeconds(29.9), plan.VehicleRfids));

        Assert.NotNull(session);
        Assert.Equal(7, session!.DetectedVehicleCount);
        Assert.Equal(7, session.ExpectedVehicleCount);
        Assert.Equal(PassageLifecycleState.Completed, coordinator.States[station.ProtocolAddress].LifecycleState);
        var passage = Assert.Single(store.Records);
        Assert.Equal(PassageOutcome.Completed, passage.Outcome);
        Assert.Equal(7, passage.DetectedVehicleCount);
        Assert.Null(passage.AlarmStatus);
    }

    [Fact]
    public void Vehicle_head_in_position_zero_on_a_later_frame_supplements_identity_without_creating_another_passage()
    {
        var plan = AcceptanceScenario.Parse("VehicleHeadAppearsLate7").Trains.Single();
        var station = CreateStation(expectedVehicleCount: 7);
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(
            station,
            store,
            new[] { CreateVehicle(0x1001, "K001", "矿车001", plan.VehicleRfids) });

        var firstSession = coordinator.ProcessFrame(CreateFrameFromPlan(station, Start, plan, includeHead: false));
        Assert.NotNull(firstSession);
        Assert.Null(firstSession!.VehicleNo);
        Assert.Equal(6, firstSession.DetectedVehicleCount);

        var secondSession = coordinator.ProcessFrame(CreateFrameFromPlan(station, Start.AddSeconds(1), plan, includeHead: true));

        Assert.Same(firstSession, secondSession);
        Assert.Equal("K001", firstSession.VehicleNo);
        Assert.Equal("矿车001", firstSession.VehicleName);
        Assert.Equal((ushort?)0x1001, firstSession.VehicleHeadRfid);
        Assert.Equal(7, firstSession.DetectedVehicleCount);
        Assert.Equal(7, firstSession.ExpectedVehicleCount);
        var passage = Assert.Single(store.Records);
        Assert.Equal(PassageOutcome.Completed, passage.Outcome);
        Assert.Equal("K001", passage.VehicleNo);
        Assert.Equal("矿车001", passage.VehicleName);
        Assert.Equal((ushort?)0x1001, passage.VehicleHeadRfid);
    }

    [Fact]
    public void Repeated_reads_do_not_increase_detected_count_or_extend_new_tag_timeout()
    {
        var plan = AcceptanceScenario.Parse("VehicleRepeatedTagReads").Trains.Single();
        var station = CreateStation(expectedVehicleCount: 7);
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(station, store, Array.Empty<VehicleConfig>());
        var session = coordinator.ProcessFrame(CreateFrame(station, Start, plan.VehicleRfids));

        Assert.NotNull(session);
        Assert.Equal(1, session!.DetectedVehicleCount);
        Assert.Equal(Start, session.LastNewVehicleAt);

        foreach (var second in new[] { 10, 20, 29 })
        {
            coordinator.ProcessFrame(CreateFrame(station, Start.AddSeconds(second), plan.VehicleRfids));
        }

        Assert.Equal(1, session.DetectedVehicleCount);
        Assert.Equal(Start, session.LastNewVehicleAt);
        coordinator.Evaluate(Start.AddSeconds(30));

        Assert.Equal(PassageLifecycleState.Alarm, coordinator.States[station.ProtocolAddress].LifecycleState);
        var passage = Assert.Single(store.Records);
        Assert.Equal(6, passage.ExpectedVehicleCount - passage.DetectedVehicleCount);
        Assert.Equal(PassageOutcome.UncouplingAlarm, passage.Outcome);
    }

    [Theory]
    [InlineData(6, 11, VehicleMatchSource.StationConfiguration, 6)]
    [InlineData(null, 11, VehicleMatchSource.GlobalConfiguration, 11)]
    public void Unknown_head_scenario_keeps_station_or_global_expected_count(
        int? stationExpectedCount,
        int globalExpectedCount,
        VehicleMatchSource expectedSource,
        int expectedCount)
    {
        var plan = AcceptanceScenario.Parse("VehicleUnconfiguredFallback").Trains.Single();
        var station = CreateStation(stationExpectedCount);
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(
            station,
            store,
            new[] { CreateSevenSectionVehicle() },
            globalExpectedCount);
        VehicleMatchResult? matchResult = null;
        coordinator.VehicleMatchResolved += result => matchResult = result;

        var session = coordinator.ProcessFrame(CreateFrame(station, Start, plan.VehicleRfids));

        Assert.NotNull(session);
        Assert.Equal(expectedCount, session!.ExpectedVehicleCount);
        Assert.NotNull(matchResult);
        Assert.False(matchResult!.IsMatched);
        Assert.Equal(expectedSource, matchResult.Source);
        Assert.Equal(expectedCount, matchResult.ExpectedVehicleCount);
        Assert.Null(session.VehicleNo);
        Assert.Null(session.VehicleName);
        Assert.Null(session.VehicleHeadRfid);
        if (expectedCount == plan.VehicleRfids.Count)
        {
            var record = Assert.Single(store.Records);
            Assert.Null(record.VehicleNo);
            Assert.Null(record.VehicleName);
            Assert.Equal(PassageOutcome.Completed, record.Outcome);
        }
        else
        {
            Assert.Empty(store.Records);
        }
    }

    [Fact]
    public void Second_configured_vehicle_gets_a_new_passage_after_normal_clear_and_empty_confirmation()
    {
        var station = CreateStation(expectedVehicleCount: null);
        var store = new InMemoryPassageRecordStore();
        var firstRfids = new ushort[] { 1, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16 };
        var secondRfids = new ushort[] { 2, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26 };
        var coordinator = CreateCoordinator(station, store, new[]
        {
            CreateVehicle(1, "K001", "矿车001", firstRfids),
            CreateVehicle(2, "K002", "矿车002", secondRfids)
        });

        CompleteAndEmpty(coordinator, station, Start, firstRfids);
        CompleteAndEmpty(coordinator, station, Start.AddSeconds(3), secondRfids);

        Assert.Equal(2, store.Records.Count);
        Assert.NotEqual(store.Records[0].PassageId, store.Records[1].PassageId);
        Assert.Equal(("K001", "矿车001"), (store.Records[0].VehicleNo, store.Records[0].VehicleName));
        Assert.Equal(("K002", "矿车002"), (store.Records[1].VehicleNo, store.Records[1].VehicleName));
    }

    [Fact]
    public void Fast_follow_during_clear_is_not_mixed_but_can_be_lost_before_station_returns_idle()
    {
        var station = CreateStation(expectedVehicleCount: null);
        var store = new InMemoryPassageRecordStore();
        var firstRfids = new ushort[] { 1, 0x11 };
        var secondRfids = new ushort[] { 2, 0x21 };
        var coordinator = CreateCoordinator(station, store, new[]
        {
            CreateVehicle(1, "K001", "矿车001", firstRfids),
            CreateVehicle(2, "K002", "矿车002", secondRfids)
        });

        var firstSession = coordinator.ProcessFrame(CreateFrame(station, Start, firstRfids));
        Assert.NotNull(firstSession);
        Assert.Equal(PassageLifecycleState.Completed, coordinator.States[station.ProtocolAddress].LifecycleState);

        // A second vehicle read while the first completed passage is still latched is ignored.
        var ignoredFastFollow = coordinator.ProcessFrame(CreateFrame(station, Start.AddMilliseconds(100), secondRfids));
        Assert.Same(firstSession, ignoredFastFollow);
        Assert.Single(store.Records);

        coordinator.MarkCommandSent(station.ProtocolAddress, MineRailMonitor.Core.Communication.RfidPollCommand.Clear, Start.AddMilliseconds(200));
        coordinator.ProcessFrame(CreateFrame(station, Start.AddMilliseconds(300), secondRfids));
        Assert.Equal(PassageLifecycleState.WaitForEmpty, coordinator.States[station.ProtocolAddress].LifecycleState);
        Assert.True(coordinator.States[station.ProtocolAddress].PendingClear);
        Assert.Single(store.Records);

        // The retry clear and required consecutive empty reads return the station to Idle,
        // but this second vehicle has not been captured as a passage.
        coordinator.MarkCommandSent(station.ProtocolAddress, MineRailMonitor.Core.Communication.RfidPollCommand.Clear, Start.AddMilliseconds(400));
        coordinator.ProcessFrame(CreateFrame(station, Start.AddMilliseconds(500), Array.Empty<ushort>()));
        coordinator.ProcessFrame(CreateFrame(station, Start.AddMilliseconds(600), Array.Empty<ushort>()));

        Assert.Equal(PassageLifecycleState.Idle, coordinator.States[station.ProtocolAddress].LifecycleState);
        Assert.Single(store.Records);
        Assert.DoesNotContain(store.Records, item => item.VehicleNo == "K002");
    }

    [Fact]
    public void U09_configured_eleven_tag_vehicle_missing_five_tags_alarms_and_is_recorded()
    {
        var plan = AcceptanceScenario.Parse("U09_11CarMissingFive").Trains.Single();
        var station = CreateStation(expectedVehicleCount: 7);
        var store = new InMemoryPassageRecordStore();
        var events = new RecordingPassageEventLogger();
        var coordinator = CreateCoordinator(station, store, new[] { CreateTest11Vehicle() }, eventLogger: events);

        var session = coordinator.ProcessFrame(CreateFrame(station, Start, plan.VehicleRfids));
        Assert.NotNull(session);
        Assert.Equal(11, session!.ExpectedVehicleCount);
        Assert.Equal(6, session.DetectedVehicleCount);
        coordinator.Evaluate(Start.AddSeconds(30));

        var passage = Assert.Single(store.Records);
        Assert.Equal("TEST-11", passage.VehicleNo);
        Assert.Equal("11节验收车辆", passage.VehicleName);
        Assert.Equal(11, passage.ExpectedVehicleCount);
        Assert.Equal(6, passage.DetectedVehicleCount);
        Assert.Equal(5, passage.ExpectedVehicleCount - passage.DetectedVehicleCount);
        Assert.Equal(PassageOutcome.UncouplingAlarm, passage.Outcome);
        Assert.Equal(AlarmStatus.Unconfirmed, passage.AlarmStatus);
        var alarmEvent = Assert.Single(events.Events, item => item.EventType == PassageEventTypes.Alarm);
        Assert.Equal(passage.PassageId, alarmEvent.PassageId);
        Assert.Equal(11, alarmEvent.ExpectedCount);
        Assert.Equal(6, alarmEvent.DetectedCount);
        Assert.Equal(5, alarmEvent.MissingCount);
        Assert.Equal(1, store.GetStatistics(Start.AddSeconds(30)).TodayAlarmCount);
    }

    [Fact]
    public void U10_new_tags_at_twenty_five_seconds_refresh_last_new_time_and_complete_once()
    {
        var plan = AcceptanceScenario.Parse("U10_11CarLateCompletion").Trains.Single();
        var station = CreateStation(expectedVehicleCount: 7);
        var store = new InMemoryPassageRecordStore();
        var events = new RecordingPassageEventLogger();
        var coordinator = CreateCoordinator(station, store, new[] { CreateTest11Vehicle() }, eventLogger: events);
        var initialRfids = plan.VehicleRfids.Take(6).ToArray();
        var session = coordinator.ProcessFrame(CreateFrame(station, Start, initialRfids));

        Assert.NotNull(session);
        Assert.Equal(11, session!.ExpectedVehicleCount);
        Assert.Equal(6, session.DetectedVehicleCount);
        foreach (var second in new[] { 5, 10, 20, 24 })
        {
            coordinator.ProcessFrame(CreateFrame(station, Start.AddSeconds(second), initialRfids));
        }
        Assert.Equal(Start, session.LastNewVehicleAt);

        var observedRfids = initialRfids.ToList();
        for (var index = 6; index < plan.VehicleRfids.Count; index++)
        {
            observedRfids.Add(plan.VehicleRfids[index]);
            var receivedAt = Start.AddSeconds(25 + (index - 6) * 0.45);
            coordinator.ProcessFrame(CreateFrame(station, receivedAt, observedRfids));
        }

        Assert.Equal(11, session.DetectedVehicleCount);
        Assert.True(session.LastNewVehicleAt > Start.AddSeconds(24));
        Assert.Equal(PassageLifecycleState.Completed, coordinator.States[station.ProtocolAddress].LifecycleState);
        var passage = Assert.Single(store.Records);
        Assert.Equal("TEST-11", passage.VehicleNo);
        Assert.Equal(11, passage.ExpectedVehicleCount);
        Assert.Equal(11, passage.DetectedVehicleCount);
        Assert.Equal(PassageOutcome.Completed, passage.Outcome);
        Assert.Null(passage.AlarmStatus);
        Assert.DoesNotContain(events.Events, item => item.EventType == PassageEventTypes.Alarm);
    }

    [Fact]
    public void U11_configured_seven_tag_vehicle_completes_without_a_fixed_eleven_tag_assumption()
    {
        var plan = AcceptanceScenario.Parse("U11_CustomCountNotFixed11").Trains.Single();
        var station = CreateStation(expectedVehicleCount: 11);
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(station, store, new[] { CreateTest07Vehicle() });

        var session = coordinator.ProcessFrame(CreateFrame(station, Start, plan.VehicleRfids));

        Assert.NotNull(session);
        Assert.Equal(7, session!.ExpectedVehicleCount);
        Assert.Equal(7, session.DetectedVehicleCount);
        Assert.Equal(PassageLifecycleState.Completed, coordinator.States[station.ProtocolAddress].LifecycleState);
        var passage = Assert.Single(store.Records);
        Assert.Equal("TEST-07", passage.VehicleNo);
        Assert.Equal(7, passage.ExpectedVehicleCount);
        Assert.Equal(7, passage.DetectedVehicleCount);
        Assert.Equal(PassageOutcome.Completed, passage.Outcome);
    }

    [Fact]
    public void U12_fixed_TEST11_vehicle_completes_with_eleven_tags_despite_station_and_global_defaults()
    {
        var plan = AcceptanceScenario.Parse("U12_Test11Normal").Trains.Single();
        var station = CreateStation(expectedVehicleCount: 6);
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(station, store, new[] { CreateTest11Vehicle() }, globalExpectedCount: 6);

        var session = coordinator.ProcessFrame(CreateFrame(station, Start, plan.VehicleRfids));

        Assert.NotNull(session);
        Assert.Equal(11, session!.ExpectedVehicleCount);
        Assert.Equal(11, session.DetectedVehicleCount);
        Assert.Equal(PassageLifecycleState.Completed, coordinator.States[station.ProtocolAddress].LifecycleState);
        var passage = Assert.Single(store.Records);
        Assert.Equal("TEST-11", passage.VehicleNo);
        Assert.Equal(11, passage.ExpectedVehicleCount);
        Assert.Equal(11, passage.DetectedVehicleCount);
        Assert.Equal(PassageOutcome.Completed, passage.Outcome);
    }

    [Fact]
    public void U13_fixed_TEST11_vehicle_missing_one_tag_always_alarms()
    {
        var plan = AcceptanceScenario.Parse("U13_Test11MissingOne").Trains.Single();
        var station = CreateStation(expectedVehicleCount: 6);
        var store = new InMemoryPassageRecordStore();
        var events = new RecordingPassageEventLogger();
        var coordinator = CreateCoordinator(station, store, new[] { CreateTest11Vehicle() }, globalExpectedCount: 6, eventLogger: events);

        var session = coordinator.ProcessFrame(CreateFrame(station, Start, plan.VehicleRfids));
        Assert.NotNull(session);
        Assert.Equal(11, session!.ExpectedVehicleCount);
        Assert.Equal(10, session.DetectedVehicleCount);
        coordinator.Evaluate(Start.AddSeconds(30));

        Assert.Equal(PassageLifecycleState.Alarm, coordinator.States[station.ProtocolAddress].LifecycleState);
        var passage = Assert.Single(store.Records);
        Assert.Equal("TEST-11", passage.VehicleNo);
        Assert.Equal(11, passage.ExpectedVehicleCount);
        Assert.Equal(10, passage.DetectedVehicleCount);
        Assert.Equal(1, passage.ExpectedVehicleCount - passage.DetectedVehicleCount);
        Assert.Equal(PassageOutcome.UncouplingAlarm, passage.Outcome);
        var alarmEvent = Assert.Single(events.Events, item => item.EventType == PassageEventTypes.Alarm);
        Assert.Equal(11, alarmEvent.ExpectedCount);
        Assert.Equal(10, alarmEvent.DetectedCount);
        Assert.Equal(1, alarmEvent.MissingCount);
    }

    private static RfidRuntimeCoordinator CreateCoordinator(
        RfidStationConfig station,
        InMemoryPassageRecordStore store,
        IReadOnlyList<VehicleConfig> vehicles,
        int globalExpectedCount = 11,
        RecordingPassageEventLogger? eventLogger = null) => new(
            new[] { station },
            new RfidSettings { ExpectedVehicleCount = globalExpectedCount, InterVehicleTimeoutSeconds = 30 },
            store,
            new RfidRuntimePolicy(TimeSpan.FromMinutes(1)),
            vehicles,
            eventLogger);

    private static RfidStationConfig CreateStation(int? expectedVehicleCount) => new()
    {
        StationId = "RFID-SIM-01",
        Name = "模拟基站",
        IpAddress = "127.0.0.1",
        Port = 62001,
        ProtocolAddress = 0x01,
        ExpectedVehicleCount = expectedVehicleCount,
        Enabled = true
    };

    private static VehicleConfig CreateSevenSectionVehicle() => new()
    {
        VehicleNo = "K001",
        HeadRfid = 1,
        VehicleName = "矿车001",
        Enabled = true,
        Tags = new ushort[] { 1, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16 }
            .Select((rfid, position) => new VehicleTagConfig
            {
                Position = position,
                Rfid = rfid,
                Type = position == 0 ? VehicleTagType.Head : VehicleTagType.Carriage
            })
            .ToList()
    };

    private static VehicleConfig CreateTest11Vehicle() => AcceptanceVehicleCatalog.CreateVehicles()
        .Single(vehicle => vehicle.VehicleNo == "TEST-11");

    private static VehicleConfig CreateTest07Vehicle() => AcceptanceVehicleCatalog.CreateVehicles()
        .Single(vehicle => vehicle.VehicleNo == "TEST-07");

    private static VehicleConfig CreateVehicle(ushort headRfid, string vehicleNo, string vehicleName, IReadOnlyList<ushort> rfids) => new()
    {
        VehicleNo = vehicleNo,
        HeadRfid = headRfid,
        VehicleName = vehicleName,
        Enabled = true,
        Tags = rfids.Select((rfid, position) => new VehicleTagConfig
        {
            Position = position,
            Rfid = rfid,
            Type = position == 0 ? VehicleTagType.Head : VehicleTagType.Carriage
        }).ToList()
    };

    private static void CompleteAndEmpty(
        RfidRuntimeCoordinator coordinator,
        RfidStationConfig station,
        DateTimeOffset at,
        IReadOnlyList<ushort> rfids)
    {
        coordinator.ProcessFrame(CreateFrame(station, at, rfids));
        coordinator.MarkCommandSent(station.ProtocolAddress, MineRailMonitor.Core.Communication.RfidPollCommand.Clear, at.AddMilliseconds(100));
        coordinator.ProcessFrame(CreateFrame(station, at.AddMilliseconds(200), Array.Empty<ushort>()));
        coordinator.ProcessFrame(CreateFrame(station, at.AddMilliseconds(300), Array.Empty<ushort>()));
    }

    private static RfidStationFrame CreateFrame(RfidStationConfig station, DateTimeOffset receivedAt, IReadOnlyList<ushort> rfids)
    {
        var slots = new ushort[14];
        for (var index = 0; index < rfids.Count; index++)
        {
            slots[index] = rfids[index];
        }

        return CreateFrameFromSlots(station, receivedAt, slots);
    }

    private static RfidStationFrame CreateFrameFromPlan(
        RfidStationConfig station,
        DateTimeOffset receivedAt,
        AcceptanceTrainPlan plan,
        bool includeHead)
    {
        var slots = new ushort[14];
        for (var index = 0; index < plan.VehicleRfids.Count; index++)
        {
            var position = plan.OccupiedSlotIndexes[index];
            if (position == 0 && !includeHead)
            {
                continue;
            }

            slots[position] = plan.VehicleRfids[index];
        }

        return CreateFrameFromSlots(station, receivedAt, slots);
    }

    private static RfidStationFrame CreateFrameFromSlots(RfidStationConfig station, DateTimeOffset receivedAt, ushort[] slots)
    {
        station.TryResolveEndpoint(out var endpoint);

        return new RfidStationFrame
        {
            StationAddress = station.ProtocolAddress,
            ReceivedAt = receivedAt,
            RawRfidSlots = slots,
            HeadRfid = slots[0],
            ValidRfids = slots.Where(rfid => rfid != 0).ToArray(),
            ActualNonZeroSlotCount = slots.Count(rfid => rfid != 0),
            SourceEndpoint = endpoint
        };
    }

    private sealed class RecordingPassageEventLogger : MineRailMonitor.Core.Interfaces.IPassageEventLogger
    {
        public List<PassageEvent> Events { get; } = new();

        public void Write(PassageEvent passageEvent) => Events.Add(passageEvent);
    }
}
