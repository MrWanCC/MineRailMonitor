using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class PassageLifecycleEventTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Logs_start_progress_complete_and_clear_for_one_stable_passage_id()
    {
        var events = new RecordingPassageEventLogger();
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store, events, CreateVehicle(3), timeoutSeconds: 30);

        var session = coordinator.ProcessFrame(CreateFrame(Start, 1, 17));

        Assert.NotNull(session);
        var passageId = session!.PassageId;
        Assert.Equal(new[] { "PASSAGE_START", "PASSAGE_PROGRESS" }, events.Events.Select(item => item.EventType));
        Assert.Equal(passageId, events.Events[0].PassageId);
        Assert.Equal("K001", events.Events[0].VehicleNo);
        Assert.Equal("矿车001", events.Events[0].VehicleName);
        Assert.Equal((ushort)1, events.Events[0].VehicleHeadRfid);
        Assert.Equal(3, events.Events[0].ExpectedCount);
        Assert.Equal(2, events.Events[1].DetectedCount);

        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(1), 1, 17));
        Assert.Equal(2, events.Events.Count);

        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(2), 1, 17, 18));

        Assert.Equal(
            new[] { "PASSAGE_START", "PASSAGE_PROGRESS", "PASSAGE_PROGRESS", "PASSAGE_COMPLETE" },
            events.Events.Select(item => item.EventType));
        Assert.Equal(3, events.Events[2].DetectedCount);
        Assert.Equal(3, events.Events[3].ExpectedCount);
        Assert.Equal(3, events.Events[3].DetectedCount);
        Assert.Equal(2000, events.Events[3].DurationMilliseconds);
        Assert.Equal(passageId, Assert.Single(store.Records).PassageId);

        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddSeconds(3));
        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(4)));
        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(5)));

        var clearEvent = events.Events[events.Events.Count - 1];
        Assert.Equal("PASSAGE_CLEAR", clearEvent.EventType);
        Assert.Equal(passageId, clearEvent.PassageId);
        Assert.Equal("K001", clearEvent.VehicleNo);
        Assert.Null(coordinator.States[0x01].RecognitionSession);
    }

    [Fact]
    public void Logs_alarm_reason_missing_count_and_session_timeout()
    {
        var events = new RecordingPassageEventLogger();
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store, events, CreateVehicle(4), timeoutSeconds: 3);

        var session = coordinator.ProcessFrame(CreateFrame(Start, 1, 17, 18));
        coordinator.Evaluate(Start.AddSeconds(3));

        var alarmEvent = Assert.Single(events.Events, item => item.EventType == "PASSAGE_ALARM");
        Assert.Equal(session!.PassageId, alarmEvent.PassageId);
        Assert.Equal("脱节报警", alarmEvent.Reason);
        Assert.Equal(4, alarmEvent.ExpectedCount);
        Assert.Equal(3, alarmEvent.DetectedCount);
        Assert.Equal(1, alarmEvent.MissingCount);
        Assert.Equal(3, alarmEvent.TimeoutSeconds);
    }

    [Fact]
    public void Passage_event_logging_failure_does_not_interrupt_recognition()
    {
        var coordinator = CreateCoordinator(
            new InMemoryPassageRecordStore(),
            new FailingPassageEventLogger(),
            CreateVehicle(2),
            timeoutSeconds: 30);

        var session = coordinator.ProcessFrame(CreateFrame(Start, 1, 17));

        Assert.NotNull(session);
        Assert.Equal(PassageLifecycleState.Completed, coordinator.States[0x01].LifecycleState);
    }

    private static RfidRuntimeCoordinator CreateCoordinator(
        InMemoryPassageRecordStore store,
        IPassageEventLogger eventLogger,
        VehicleConfig vehicle,
        int timeoutSeconds) => new(
        new byte[] { 0x01 },
        new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = timeoutSeconds },
        store,
        new RfidRuntimePolicy(TimeSpan.FromMinutes(1)),
        new[] { vehicle },
        eventLogger);

    private static VehicleConfig CreateVehicle(int sectionCount) => new()
    {
        VehicleNo = "K001",
        HeadRfid = 1,
        VehicleName = "矿车001",
        Enabled = true,
        Tags = Enumerable.Range(0, sectionCount)
            .Select(position => new VehicleTagConfig
            {
                Position = position,
                Rfid = position == 0 ? (ushort)1 : (ushort)(16 + position),
                Type = position == 0 ? VehicleTagType.Head : VehicleTagType.Carriage
            })
            .ToList()
    };

    private static RfidStationFrame CreateFrame(DateTimeOffset at, params ushort[] rfids)
    {
        var slots = new ushort[14];
        rfids.CopyTo(slots, 0);
        return new RfidStationFrame
        {
            StationAddress = 0x01,
            ReceivedAt = at,
            SourceEndpoint = new IPEndPoint(IPAddress.Loopback, 62001),
            RawRfidSlots = slots,
            HeadRfid = slots[0],
            ValidRfids = slots.Where(value => value != 0).ToArray(),
            ActualNonZeroSlotCount = slots.Count(value => value != 0),
            ReportedCardCount = (byte)slots.Count(value => value != 0)
        };
    }

    private sealed class RecordingPassageEventLogger : IPassageEventLogger
    {
        public List<PassageEvent> Events { get; } = new();

        public void Write(PassageEvent passageEvent) => Events.Add(passageEvent);
    }

    private sealed class FailingPassageEventLogger : IPassageEventLogger
    {
        public void Write(PassageEvent passageEvent) => throw new IOException("故意模拟日志写入失败");
    }
}
