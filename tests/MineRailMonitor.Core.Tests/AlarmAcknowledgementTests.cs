using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class AlarmAcknowledgementTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Cleared_uncoupling_alarm_stays_red_until_acknowledged()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        CreateAndClearAlarm(coordinator);

        var state = coordinator.States[0x01];
        var passageId = Assert.Single(store.Records).PassageId;
        Assert.True(state.HasUnacknowledgedAlarms);
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);

        coordinator.AcknowledgeAlarm(passageId, Start.AddMinutes(1));

        Assert.False(state.HasUnacknowledgedAlarms);
        Assert.Equal(RfidStationVisualState.Idle, state.VisualState);
        var record = Assert.Single(store.Records);
        Assert.Equal(Start.AddMinutes(1), record.AlarmAcknowledgedAt);
        Assert.Equal(Start.AddSeconds(30.4), record.AlarmRecoveredAt);
    }

    [Fact]
    public void Acknowledged_alarm_stays_red_until_clear_recovery_completes()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var alarmValues = AlarmValues();

        coordinator.ProcessFrame(CreateFrame(0x01, Start, alarmValues));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(29.9), alarmValues));
        coordinator.Evaluate(Start.AddSeconds(30));

        var state = coordinator.States[0x01];
        var passageId = Assert.Single(store.Records).PassageId;
        coordinator.AcknowledgeAlarm(passageId, Start.AddSeconds(30.1));
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);

        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddSeconds(30.2));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(30.3), Array.Empty<ushort>()));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(30.4), Array.Empty<ushort>()));

        Assert.Equal(PassageLifecycleState.Idle, state.LifecycleState);
        Assert.Equal(RfidStationVisualState.Idle, state.VisualState);
    }

    [Fact]
    public void Acknowledged_alarm_stays_red_during_clear_and_wait_for_empty()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var alarmValues = AlarmValues();

        coordinator.ProcessFrame(CreateFrame(0x01, Start, alarmValues));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(29.9), alarmValues));
        coordinator.Evaluate(Start.AddSeconds(30));

        var state = coordinator.States[0x01];
        var passageId = Assert.Single(store.Records).PassageId;
        coordinator.AcknowledgeAlarm(passageId, Start.AddSeconds(30.1));
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);

        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddSeconds(30.2));
        Assert.Equal(PassageLifecycleState.Clearing, state.LifecycleState);
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);

        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(30.3), Array.Empty<ushort>()));
        Assert.Equal(PassageLifecycleState.WaitForEmpty, state.LifecycleState);
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);

        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(30.4), Array.Empty<ushort>()));
        Assert.Equal(PassageLifecycleState.Idle, state.LifecycleState);
        Assert.NotEqual(RfidStationVisualState.Alarm, state.VisualState);
    }

    [Fact]
    public void Acknowledged_pending_clear_alarm_restores_red_after_restart()
    {
        var store = new InMemoryPassageRecordStore();
        var original = CreateCoordinator(store);
        var alarmValues = AlarmValues();

        original.ProcessFrame(CreateFrame(0x01, Start, alarmValues));
        original.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(29.9), alarmValues));
        original.Evaluate(Start.AddSeconds(30));
        var passageId = Assert.Single(store.Records).PassageId;
        original.AcknowledgeAlarm(passageId, Start.AddSeconds(30.1));

        var restored = CreateCoordinator(store);
        restored.RestorePendingClear(store.GetPendingClear());
        restored.RestoreUnacknowledgedAlarms(store.GetUnacknowledgedAlarms());

        var state = restored.States[0x01];
        Assert.False(state.HasUnacknowledgedAlarms);
        Assert.True(state.PendingClear);
        Assert.Equal(PassageLifecycleState.Clearing, state.LifecycleState);
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);

        restored.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddSeconds(30.2));
        restored.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(30.3), Array.Empty<ushort>()));
        Assert.Equal(PassageLifecycleState.WaitForEmpty, state.LifecycleState);
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);

        restored.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(30.4), Array.Empty<ushort>()));
        Assert.Equal(PassageLifecycleState.Idle, state.LifecycleState);
        Assert.NotEqual(RfidStationVisualState.Alarm, state.VisualState);
    }

    [Fact]
    public void Two_unacknowledged_alarm_passages_require_both_acknowledgements()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var first = AlarmValues(0x0001);
        var second = AlarmValues(0x0002);

        CreateAndClearAlarm(coordinator, Start, first);
        CreateAndClearAlarm(coordinator, Start.AddMinutes(2), second);

        var state = coordinator.States[0x01];
        var passageIds = store.Records.Select(record => record.PassageId).ToArray();
        Assert.Equal(2, passageIds.Length);
        Assert.True(state.HasUnacknowledgedAlarms);

        coordinator.AcknowledgeAlarm(passageIds[0], Start.AddMinutes(3));
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);
        Assert.True(state.HasUnacknowledgedAlarms);

        coordinator.AcknowledgeAlarm(passageIds[1], Start.AddMinutes(4));
        Assert.False(state.HasUnacknowledgedAlarms);
        Assert.Equal(RfidStationVisualState.Idle, state.VisualState);
    }

    [Fact]
    public void Same_protocol_address_on_different_endpoints_keeps_alarm_acknowledgement_isolated()
    {
        var store = new InMemoryPassageRecordStore();
        var stations = new[]
        {
            new RfidStationConfig
            {
                StationId = "RFID-560-01",
                Name = "560 一号基站",
                IpAddress = "127.0.0.1",
                Port = 62001,
                ProtocolAddress = 0x01
            },
            new RfidStationConfig
            {
                StationId = "RFID-620-01",
                Name = "620 一号基站",
                IpAddress = "127.0.0.1",
                Port = 62002,
                ProtocolAddress = 0x01
            }
        };
        var coordinator = new RfidRuntimeCoordinator(
            stations,
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            store,
            new RfidRuntimePolicy(TimeSpan.FromSeconds(5), 3, 2));

        coordinator.ProcessFrame(CreateFrame(0x01, Start, AlarmValues(), new IPEndPoint(IPAddress.Loopback, 62001)));
        coordinator.ProcessFrame(CreateFrame(0x01, Start, AlarmValues(0x0002), new IPEndPoint(IPAddress.Loopback, 62002)));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(29.9), AlarmValues(), new IPEndPoint(IPAddress.Loopback, 62001)));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(29.9), AlarmValues(0x0002), new IPEndPoint(IPAddress.Loopback, 62002)));
        coordinator.Evaluate(Start.AddSeconds(30));

        var records = store.Records.OrderBy(record => record.StationId).ToArray();
        Assert.Equal(new[] { "RFID-560-01", "RFID-620-01" }, records.Select(record => record.StationId));
        coordinator.AcknowledgeAlarm(records[0].PassageId, Start.AddMinutes(1));

        Assert.False(coordinator.EndpointStates.Single(pair => pair.Value.StationId == "RFID-560-01").Value.HasUnacknowledgedAlarms);
        Assert.True(coordinator.EndpointStates.Single(pair => pair.Value.StationId == "RFID-620-01").Value.HasUnacknowledgedAlarms);
    }

    [Fact]
    public void Restored_unacknowledged_alarm_is_latched_against_the_stable_station_id()
    {
        var store = new InMemoryPassageRecordStore();
        var original = CreateCoordinator(store);
        original.ProcessFrame(CreateFrame(0x01, Start, AlarmValues()));
        original.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(29.9), AlarmValues()));
        original.Evaluate(Start.AddSeconds(30));

        var restored = CreateCoordinator(store);
        restored.RestoreUnacknowledgedAlarms(store.GetUnacknowledgedAlarms());

        var state = restored.States[0x01];
        Assert.True(state.HasUnacknowledgedAlarms);
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);
    }

    [Fact]
    public void Restored_recovered_unacknowledged_alarm_clears_red_after_acknowledgement()
    {
        var store = new InMemoryPassageRecordStore();
        var original = CreateCoordinator(store);
        CreateAndClearAlarm(original);

        var passage = Assert.Single(store.Records);
        Assert.NotNull(passage.AlarmRecoveredAt);

        var restored = CreateCoordinator(store);
        restored.RestorePendingClear(store.GetPendingClear());
        restored.RestoreUnacknowledgedAlarms(store.GetUnacknowledgedAlarms());

        var state = restored.States[0x01];
        Assert.False(state.PendingClear);
        Assert.True(state.HasUnacknowledgedAlarms);
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);

        restored.AcknowledgeAlarm(passage.PassageId, Start.AddMinutes(1));

        Assert.False(state.HasUnacknowledgedAlarms);
        Assert.NotEqual(RfidStationVisualState.Alarm, state.VisualState);
        var updated = store.GetDetails(passage.PassageId);
        Assert.NotNull(updated);
        Assert.Equal(Start.AddMinutes(1), updated.AlarmAcknowledgedAt);
        Assert.Equal(passage.AlarmRecoveredAt, updated.AlarmRecoveredAt);
    }

    [Fact]
    public void Failed_acknowledgement_persistence_keeps_the_runtime_latch()
    {
        var inner = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(new ThrowingAcknowledgementStore(inner));
        CreateAndClearAlarm(coordinator);

        var passageId = Assert.Single(inner.Records).PassageId;
        Assert.Throws<IOException>(() => coordinator.AcknowledgeAlarm(passageId, Start.AddMinutes(1)));

        var state = coordinator.States[0x01];
        Assert.True(state.HasUnacknowledgedAlarms);
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);
    }

    private static RfidRuntimeCoordinator CreateCoordinator(IPassageRecordStore store) =>
        new(
            new[]
            {
                new RfidStationConfig
                {
                    StationId = "RFID-560-01",
                    Name = "560 一号基站",
                    IpAddress = "127.0.0.1",
                    Port = 62001,
                    ProtocolAddress = 0x01
                }
            },
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            store,
            new RfidRuntimePolicy(TimeSpan.FromSeconds(5), 3, 2));

    private static void CreateAndClearAlarm(
        RfidRuntimeCoordinator coordinator,
        DateTimeOffset at = default,
        IReadOnlyList<ushort>? values = null)
    {
        if (at == default)
        {
            at = Start;
        }

        values ??= AlarmValues();
        coordinator.ProcessFrame(CreateFrame(0x01, at, values));
        coordinator.ProcessFrame(CreateFrame(0x01, at.AddSeconds(29.9), values));
        coordinator.Evaluate(at.AddSeconds(30));
        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, at.AddSeconds(30.2));
        coordinator.ProcessFrame(CreateFrame(0x01, at.AddSeconds(30.3), Array.Empty<ushort>()));
        coordinator.ProcessFrame(CreateFrame(0x01, at.AddSeconds(30.4), Array.Empty<ushort>()));
    }

    private static ushort[] AlarmValues(ushort head = 0x0001) =>
        new ushort[] { head, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019 };

    private static RfidStationFrame CreateFrame(
        byte address,
        DateTimeOffset at,
        IReadOnlyList<ushort> values,
        IPEndPoint? sourceEndpoint = null)
    {
        var slots = new ushort[14];
        values.Take(14).ToArray().CopyTo(slots, 0);
        return new RfidStationFrame
        {
            StationAddress = address,
            Mode = 0x04,
            HeadRfid = slots[0],
            WagonRfids = slots.Skip(1).Where(value => value != 0).ToArray(),
            RawRfidSlots = slots,
            ValidRfids = slots.Where(value => value != 0).ToArray(),
            ReportedCardCount = (byte)values.Count,
            ActualNonZeroSlotCount = values.Count,
            ReceivedAt = at,
            SourceEndpoint = sourceEndpoint ?? new IPEndPoint(IPAddress.Loopback, 62001)
        };
    }

    private sealed class ThrowingAcknowledgementStore : IPassageRecordStore
    {
        private readonly InMemoryPassageRecordStore _inner;

        public ThrowingAcknowledgementStore(InMemoryPassageRecordStore inner) => _inner = inner;

        public IReadOnlyList<PassageRecord> Records => _inner.Records;

        public void Save(PassageRecord record) => _inner.Save(record);

        public void Add(PassageRecord record) => _inner.Add(record);

        public void MarkCleared(Guid passageId, DateTimeOffset clearedAt) => _inner.MarkCleared(passageId, clearedAt);

        public void MarkAlarmAcknowledged(Guid passageId, DateTimeOffset acknowledgedAt) => throw new IOException("模拟确认持久化失败");

        public IReadOnlyList<PassageRecord> GetPendingClear() => _inner.GetPendingClear();

        public IReadOnlyList<PassageRecord> GetUnacknowledgedAlarms() => _inner.GetUnacknowledgedAlarms();

        public PassageQueryResult Query(PassageQuery query) => _inner.Query(query);

        public PassageRecord? GetDetails(Guid passageId) => _inner.GetDetails(passageId);

        public PassageStatistics GetStatistics(DateTimeOffset localNow) => _inner.GetStatistics(localNow);
    }
}
