using System.Net;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class VehicleMatchRuntimeIntegrationTests
{
    [Theory]
    [InlineData(11)]
    [InlineData(7)]
    public void Matched_custom_vehicle_uses_its_tag_count_as_the_completion_threshold(int sectionCount)
    {
        var station = CreateStation(expectedVehicleCount: 6);
        var vehicle = CreateVehicle(headRfid: 1, name: "自定义车辆", sectionCount: sectionCount);
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(station, new[] { vehicle }, globalExpectedVehicleCount: 9, store: store);
        var rfids = vehicle.Tags.OrderBy(tag => tag.Position).Select(tag => tag.Rfid).ToArray();

        var session = coordinator.ProcessFrame(CreateFrame(station, rfids));

        Assert.NotNull(session);
        Assert.Equal(sectionCount, session!.ExpectedVehicleCount);
        Assert.Equal(sectionCount, session.DetectedVehicleCount);
        var passage = Assert.Single(store.Records);
        Assert.Equal(sectionCount, passage.ExpectedVehicleCount);
        Assert.Equal(PassageOutcome.Completed, passage.Outcome);
    }

    [Fact]
    public void Creates_session_using_configured_vehicle_section_count_and_publishes_match()
    {
        var station = CreateStation(expectedVehicleCount: 6);
        var vehicle = CreateVehicle(headRfid: 1, name: "矿车001", sectionCount: 4);
        var coordinator = CreateCoordinator(station, new[] { vehicle }, globalExpectedVehicleCount: 11);
        VehicleMatchResult? publishedMatch = null;
        coordinator.VehicleMatchResolved += result => publishedMatch = result;

        var session = coordinator.ProcessFrame(CreateFrame(station, 1, 17, 18, 19));

        Assert.NotNull(session);
        Assert.Equal(4, session!.ExpectedVehicleCount);
        Assert.Equal(4, coordinator.States[station.ProtocolAddress].ExpectedVehicleCount);
        Assert.NotNull(publishedMatch);
        Assert.True(publishedMatch!.IsMatched);
        Assert.Equal(4, publishedMatch.ExpectedCount);
        Assert.Equal(VehicleMatchSource.VehicleConfiguration, publishedMatch.Source);
    }

    [Fact]
    public void Ignores_station_count_when_head_tag_does_not_match_a_vehicle()
    {
        var station = CreateStation(expectedVehicleCount: 6);
        var vehicle = CreateVehicle(headRfid: 1, name: "矿车001", sectionCount: 4);
        var coordinator = CreateCoordinator(station, new[] { vehicle }, globalExpectedVehicleCount: 11);
        VehicleMatchResult? publishedMatch = null;
        coordinator.VehicleMatchResolved += result => publishedMatch = result;

        var session = coordinator.ProcessFrame(CreateFrame(station, 2, 17, 18));

        Assert.NotNull(session);
        Assert.Equal(11, session!.ExpectedVehicleCount);
        Assert.NotNull(publishedMatch);
        Assert.False(publishedMatch!.IsMatched);
        Assert.Equal(11, publishedMatch.ExpectedCount);
        Assert.Equal(VehicleMatchSource.GlobalConfiguration, publishedMatch.Source);
    }

    [Fact]
    public void Uses_global_count_when_head_tag_does_not_match_and_station_has_no_override()
    {
        var station = CreateStation(expectedVehicleCount: null);
        var vehicle = CreateVehicle(headRfid: 1, name: "矿车001", sectionCount: 4);
        var coordinator = CreateCoordinator(station, new[] { vehicle }, globalExpectedVehicleCount: 11);
        VehicleMatchResult? publishedMatch = null;
        coordinator.VehicleMatchResolved += result => publishedMatch = result;

        var session = coordinator.ProcessFrame(CreateFrame(station, 2, 17, 18));

        Assert.NotNull(session);
        Assert.Equal(11, session!.ExpectedVehicleCount);
        Assert.NotNull(publishedMatch);
        Assert.False(publishedMatch!.IsMatched);
        Assert.Equal(11, publishedMatch.ExpectedCount);
        Assert.Equal(VehicleMatchSource.GlobalConfiguration, publishedMatch.Source);
    }

    [Fact]
    public void Unmatched_vehicle_without_any_fallback_is_saved_unverified_without_alarm()
    {
        var station = CreateStation(expectedVehicleCount: null);
        var store = new InMemoryPassageRecordStore();
        var eventLogger = new RecordingPassageEventLogger();
        var coordinator = new RfidRuntimeCoordinator(
            new[] { station },
            new RfidSettings { ExpectedVehicleCount = null, InterVehicleTimeoutSeconds = 2 },
            store,
            new RfidRuntimePolicy(TimeSpan.FromMinutes(1)),
            passageEventLogger: eventLogger);
        var receivedAt = new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.FromHours(8));

        var session = coordinator.ProcessFrame(CreateFrame(station, receivedAt, 2, 17, 18));
        Assert.NotNull(session);
        Assert.True((object?)session!.ExpectedVehicleCount is null);
        Assert.Equal(3, session.DetectedVehicleCount);

        coordinator.Evaluate(receivedAt.AddSeconds(2));

        var passage = Assert.Single(store.Records);
        Assert.Equal(PassageOutcome.Unverified, passage.Outcome);
        Assert.True((object?)passage.ExpectedVehicleCount is null);
        Assert.Equal(3, passage.DetectedVehicleCount);
        Assert.False(passage.RequiresAlarmAcknowledgement);
        Assert.False(passage.IsAlert);
        Assert.Null(passage.AlarmStatus);
        Assert.Null(passage.AlarmMessage);
        Assert.Null(passage.VehicleNo);
        Assert.Null(passage.VehicleName);
        Assert.Equal(receivedAt, passage.StartedAt);
        Assert.Equal(receivedAt.AddSeconds(2), passage.CompletedAt);

        var unverifiedEvent = Assert.Single(eventLogger.Events, item => item.EventType == PassageEventTypes.Unverified);
        Assert.DoesNotContain(eventLogger.Events, item => item.EventType == PassageEventTypes.Alarm);
        Assert.Equal(passage.PassageId, unverifiedEvent.PassageId);
        Assert.Equal(station.StationId, unverifiedEvent.StationId);
        Assert.Null(unverifiedEvent.ExpectedCount);
        Assert.Equal(3, unverifiedEvent.DetectedCount);
        Assert.Equal("车头未匹配车辆配置，完整性未判定", unverifiedEvent.Reason);
        Assert.DoesNotContain(store.Query(new PassageQuery { IncludeWarnings = true }).Items, item => item.PassageId == passage.PassageId);
    }

    private static RfidRuntimeCoordinator CreateCoordinator(
        RfidStationConfig station,
        IReadOnlyList<VehicleConfig> vehicles,
        int? globalExpectedVehicleCount,
        InMemoryPassageRecordStore? store = null) => new(
            new[] { station },
            new RfidSettings
            {
                ExpectedVehicleCount = globalExpectedVehicleCount,
                InterVehicleTimeoutSeconds = 30
            },
            store ?? new InMemoryPassageRecordStore(),
            vehicleConfigurations: vehicles);

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

    private static VehicleConfig CreateVehicle(ushort headRfid, string name, int sectionCount) => new()
    {
        HeadRfid = headRfid,
        Name = name,
        Enabled = true,
        Tags = Enumerable.Range(0, sectionCount)
            .Select(position => new VehicleTagConfig
            {
                Position = position,
                Rfid = position == 0 ? headRfid : (ushort)(16 + position),
                Type = position == 0 ? VehicleTagType.Head : VehicleTagType.Carriage
            })
            .ToList()
    };

    private static RfidStationFrame CreateFrame(RfidStationConfig station, params ushort[] rfids) =>
        CreateFrame(station, DateTimeOffset.UtcNow, rfids);

    private static RfidStationFrame CreateFrame(RfidStationConfig station, DateTimeOffset receivedAt, params ushort[] rfids)
    {
        var slots = new ushort[14];
        rfids.Take(slots.Length).ToArray().CopyTo(slots, 0);
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

    private sealed class RecordingPassageEventLogger : IPassageEventLogger
    {
        public List<PassageEvent> Events { get; } = new();

        public void Write(PassageEvent passageEvent) => Events.Add(passageEvent);
    }
}
