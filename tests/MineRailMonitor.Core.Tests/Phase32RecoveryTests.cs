using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class Phase32RecoveryTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Three_save_failures_enter_persistence_fault_and_preserve_the_passage_snapshot()
    {
        var store = new TestPassageRecordStore { FailSave = true };
        var vehicle = CreateVehicleConfig(7);
        var coordinator = CreateCoordinator(
            store,
            vehicleConfigurations: new[] { vehicle },
            offlineTimeout: TimeSpan.FromMinutes(1));
        var values = Enumerable.Range(0, 6).Select(index => (ushort)(index == 0 ? 0x0001 : 0x0011 + index - 1)).ToArray();

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        coordinator.Evaluate(Start.AddSeconds(31));
        coordinator.Evaluate(Start.AddSeconds(32));
        coordinator.Evaluate(Start.AddSeconds(33));

        var state = coordinator.States[0x01];
        Assert.NotEqual(PassageLifecycleState.Finalizing, state.LifecycleState);
        Assert.Equal(PassagePersistenceState.PersistenceFault, state.PersistenceState);
        Assert.Equal(3, store.SaveAttempts);
        Assert.True(state.PersistenceWarning);
        Assert.NotNull(state.LastPassageRecord);
        Assert.True(state.PendingClear);
        Assert.Equal(RfidPollCommand.Clear, coordinator.GetCommand(0x01));
        Assert.Equal(3, state.PersistenceAttemptCount);
        Assert.Empty(store.Records);
        var fault = Assert.Single(state.PendingPersistenceFaults);
        Assert.Equal(state.LastPassageRecord.PassageId, fault.Record.PassageId);
        Assert.Equal(6, fault.Record.DetectedVehicleCount);
        Assert.Equal(7, fault.Record.ExpectedVehicleCount);
        Assert.Equal("K001", fault.Record.VehicleNo);
        Assert.Equal("一号矿车", fault.Record.VehicleName);
        Assert.Equal((ushort)0x0001, fault.Record.HeadRfid);
        Assert.Equal(PassageOutcome.UncouplingAlarm, fault.Record.Outcome);
        Assert.Equal(AlarmStatus.Unconfirmed, fault.Record.AlarmStatus);
        Assert.Equal(values, fault.Record.ObservedRfids);
        Assert.Equal(values, fault.Record.RfidObservations.Select(item => item.RfidValue));
        Assert.Equal(3, fault.FailureCount);
        Assert.Contains("模拟SQLite保存失败", fault.LastException);
        Assert.Contains(fault.Record.PassageId.ToString(), state.PersistenceErrorMessage);
    }

    [Fact]
    public void Transient_save_failure_is_retried_and_passage_is_saved()
    {
        var store = new TestPassageRecordStore { RemainingSaveFailures = 1 };
        var coordinator = CreateCoordinator(store);
        var values = Enumerable.Range(0, 11).Select(index => (ushort)(index == 0 ? 0x0001 : 0x0011 + index - 1)).ToArray();

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        coordinator.Evaluate(Start.AddSeconds(1));

        Assert.Single(store.Records);
        Assert.Equal(2, store.SaveAttempts);
        Assert.Equal(PassagePersistenceState.Healthy, coordinator.States[0x01].PersistenceState);
        Assert.Equal(PassageLifecycleState.Completed, coordinator.States[0x01].LifecycleState);
        Assert.False(coordinator.States[0x01].PersistenceWarning);
    }

    [Fact]
    public void Retry_pending_passages_saves_the_snapshot_after_database_recovers()
    {
        var store = new TestPassageRecordStore { FailSave = true };
        var logger = new RecordingPassageEventLogger();
        var coordinator = CreateCoordinator(store, logger);
        var values = Enumerable.Range(0, 11).Select(index => (ushort)(index == 0 ? 0x0001 : 0x0011 + index - 1)).ToArray();

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        coordinator.Evaluate(Start.AddSeconds(1));
        coordinator.Evaluate(Start.AddSeconds(2));
        var passageId = coordinator.States[0x01].LastPassageRecord!.PassageId;
        store.FailSave = false;

        var recovered = coordinator.RetryPendingPassages();

        Assert.Equal(1, recovered);
        var saved = Assert.Single(store.Records);
        Assert.Equal(passageId, saved.PassageId);
        Assert.Equal(PassageOutcome.Completed, saved.Outcome);
        Assert.Equal(PassagePersistenceState.Healthy, coordinator.States[0x01].PersistenceState);
        Assert.Empty(coordinator.States[0x01].PendingPersistenceFaults);
        var recoveryEvent = Assert.Single(logger.Events, item => item.EventType == PassageEventTypes.PersistenceRecovered);
        Assert.Equal(passageId, recoveryEvent.PassageId);
        Assert.Equal("RFID-01", recoveryEvent.StationId);
    }

    [Fact]
    public void Failed_persistence_recovery_keeps_fault_pending_and_logs_failure_details()
    {
        var store = new TestPassageRecordStore { FailSave = true };
        var logger = new RecordingPassageEventLogger();
        var coordinator = CreateCoordinator(store, logger);
        var values = Enumerable.Range(0, 11).Select(index => (ushort)(index == 0 ? 0x0001 : 0x0011 + index - 1)).ToArray();

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        coordinator.Evaluate(Start.AddSeconds(1));
        coordinator.Evaluate(Start.AddSeconds(2));
        var passageId = coordinator.States[0x01].LastPassageRecord!.PassageId;

        Assert.Equal(0, coordinator.RetryPendingPassages());

        var state = coordinator.States[0x01];
        Assert.Equal(PassagePersistenceState.PersistenceFault, state.PersistenceState);
        Assert.Single(state.PendingPersistenceFaults);
        Assert.Equal(4, state.PendingPersistenceFaults[0].FailureCount);
        Assert.Equal(4, store.SaveAttempts);
        var failureEvent = logger.Events.Last(item => item.EventType == PassageEventTypes.PersistenceFailed);
        Assert.Equal(passageId, failureEvent.PassageId);
        Assert.Equal("RFID-01", failureEvent.StationId);
        Assert.Equal(4, failureEvent.FailureCount);
        Assert.Contains("模拟SQLite保存失败", failureEvent.Reason);
    }

    [Fact]
    public void Persistence_fault_does_not_block_a_new_session_after_existing_clear_confirmation()
    {
        var store = new TestPassageRecordStore { FailSave = true };
        var coordinator = CreateCoordinator(store);
        var values = Enumerable.Range(0, 11).Select(index => (ushort)(index == 0 ? 0x0001 : 0x0011 + index - 1)).ToArray();

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        coordinator.Evaluate(Start.AddSeconds(1));
        coordinator.Evaluate(Start.AddSeconds(2));
        var state = coordinator.States[0x01];
        var firstPassageId = Assert.Single(state.PendingPersistenceFaults).Record.PassageId;

        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddSeconds(3));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(4), Array.Empty<ushort>()));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(5), Array.Empty<ushort>()));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(6), new ushort[] { 0x0101, 0x1101 }));

        Assert.Equal(PassagePersistenceState.PersistenceFault, state.PersistenceState);
        Assert.Equal(PassageLifecycleState.Recognizing, state.LifecycleState);
        Assert.NotNull(state.RecognitionSession);
        Assert.NotEqual(firstPassageId, state.RecognitionSession!.PassageId);
        Assert.Single(state.PendingPersistenceFaults);
        Assert.Equal(firstPassageId, state.PendingPersistenceFaults[0].Record.PassageId);
        Assert.Equal(new ushort[] { 0x0101, 0x1101 }, state.ObservedVehicleSequence);
    }

    [Fact]
    public void Persistence_fault_event_records_station_passage_attempts_exception_and_time()
    {
        var store = new TestPassageRecordStore { FailSave = true };
        var logger = new RecordingPassageEventLogger();
        var coordinator = CreateCoordinator(store, logger);
        var values = Enumerable.Range(0, 11).Select(index => (ushort)(index == 0 ? 0x0001 : 0x0011 + index - 1)).ToArray();

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        coordinator.Evaluate(Start.AddSeconds(1));
        coordinator.Evaluate(Start.AddSeconds(2));

        var failureEvent = Assert.Single(logger.Events, item => item.EventType == PassageEventTypes.PersistenceFailed);
        Assert.Equal(coordinator.States[0x01].LastPassageRecord!.PassageId, failureEvent.PassageId);
        Assert.Equal("RFID-01", failureEvent.StationId);
        Assert.Equal(3, failureEvent.FailureCount);
        Assert.False(string.IsNullOrWhiteSpace(failureEvent.Reason));
        Assert.True(failureEvent.OccurredAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public void Pending_clear_is_restored_without_creating_a_new_session_and_marks_cleared_after_two_empty_reads()
    {
        var sourceStore = new InMemoryPassageRecordStore();
        var record = CreateRecord(Guid.Parse("12345678-1234-1234-1234-123456789012"), 0x01);
        sourceStore.Save(record);

        var coordinator = CreateCoordinator(sourceStore);
        coordinator.RestorePendingClear(sourceStore.GetPendingClear());

        var state = coordinator.States[0x01];
        Assert.Equal(PassageLifecycleState.Clearing, state.LifecycleState);
        Assert.True(state.PendingClear);
        Assert.Null(state.RecognitionSession);
        Assert.Equal(record.PassageId, state.LastPassageRecord!.PassageId);

        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start);
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(1), Array.Empty<ushort>()));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(2), Array.Empty<ushort>()));

        Assert.Equal(PassageLifecycleState.Idle, state.LifecycleState);
        Assert.Equal(PassageClearState.Cleared, sourceStore.GetDetails(record.PassageId)!.ClearState);
    }

    [Fact]
    public void Mark_cleared_failure_holds_the_passage_and_retries_without_creating_a_duplicate()
    {
        var store = new TestPassageRecordStore { FailMarkClearedCount = 1 };
        var coordinator = CreateCoordinator(store);
        var values = Enumerable.Range(0, 11).Select(index => (ushort)(index == 0 ? 0x0001 : 0x0011 + index - 1)).ToArray();

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddMilliseconds(200));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(1), Array.Empty<ushort>()));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(2), Array.Empty<ushort>()));

        Assert.Equal(PassageLifecycleState.WaitForEmpty, coordinator.States[0x01].LifecycleState);
        Assert.Single(store.Records);
        Assert.Equal(PassageClearState.PendingClear, store.Records[0].ClearState);

        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(3), Array.Empty<ushort>()));

        Assert.Equal(PassageLifecycleState.Idle, coordinator.States[0x01].LifecycleState);
        Assert.Single(store.Records);
        Assert.Equal(PassageClearState.Cleared, store.Records[0].ClearState);
    }

    [Fact]
    public void Save_failure_on_one_station_does_not_block_another_station()
    {
        var store = new TestPassageRecordStore { FailSaveFor = record => record.StationAddress == 0x01 };
        var coordinator = new RfidRuntimeCoordinator(
            new byte[] { 0x01, 0x02 },
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            store,
            new RfidRuntimePolicy(TimeSpan.FromSeconds(5), 3, 2, 3));
        var values = Enumerable.Range(0, 11).Select(index => (ushort)(index == 0 ? 0x0001 : 0x0011 + index - 1)).ToArray();

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        coordinator.ProcessFrame(CreateFrame(0x02, Start, values));

        Assert.Equal(PassageLifecycleState.Finalizing, coordinator.States[0x01].LifecycleState);
        Assert.Equal(PassagePersistenceState.Healthy, coordinator.States[0x01].PersistenceState);
        Assert.Equal(PassageLifecycleState.Completed, coordinator.States[0x02].LifecycleState);
        Assert.True(coordinator.States[0x02].PendingClear);
        Assert.Single(store.Records);
        Assert.Equal((byte)0x02, store.Records[0].StationAddress);
    }

    private static RfidRuntimeCoordinator CreateCoordinator(
        TestPassageRecordStore store,
        IPassageEventLogger? logger = null,
        IEnumerable<VehicleConfig>? vehicleConfigurations = null,
        TimeSpan? offlineTimeout = null) =>
        new(
            new byte[] { 0x01 },
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            store,
            new RfidRuntimePolicy(offlineTimeout ?? TimeSpan.FromSeconds(5), maxClearAttempts: 3, emptyConfirmationReads: 2, maxPersistenceAttempts: 3),
            vehicleConfigurations: vehicleConfigurations,
            passageEventLogger: logger);

    private static VehicleConfig CreateVehicleConfig(int count) => new()
    {
        VehicleNo = "K001",
        VehicleName = "一号矿车",
        HeadRfid = 0x0001,
        Tags = Enumerable.Range(0, count)
            .Select(index => new VehicleTagConfig
            {
                Position = index,
                Rfid = index == 0 ? (ushort)0x0001 : (ushort)(0x0011 + index - 1),
                Type = index == 0 ? VehicleTagType.Head : VehicleTagType.Carriage
            })
            .ToList()
    };

    private static RfidRuntimeCoordinator CreateCoordinator(InMemoryPassageRecordStore store) =>
        new(
            new byte[] { 0x01 },
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            store,
            new RfidRuntimePolicy(TimeSpan.FromSeconds(5), maxClearAttempts: 3, emptyConfirmationReads: 2, maxPersistenceAttempts: 3));

    private static PassageRecord CreateRecord(Guid passageId, byte address) =>
        new(
            passageId,
            $"RFID-{address:X2}",
            address,
            0x0001,
            new ushort[] { 0x0001, 0x0011 },
            11,
            PassageOutcome.Completed,
            Start,
            Start.AddSeconds(1));

    private static RfidStationFrame CreateFrame(byte address, DateTimeOffset at, IReadOnlyList<ushort> values)
    {
        var slots = new ushort[14];
        values.Take(14).ToArray().CopyTo(slots, 0);
        return new RfidStationFrame
        {
            StationAddress = address,
            Mode = 0x04,
            HeadRfid = slots[0],
            RawRfidSlots = slots,
            ValidRfids = slots.Where(value => value != 0).ToArray(),
            ReportedCardCount = (byte)values.Count,
            ActualNonZeroSlotCount = values.Count,
            ReceivedAt = at,
            SourceEndpoint = new IPEndPoint(IPAddress.Loopback, 62001)
        };
    }

    private sealed class TestPassageRecordStore : IPassageRecordStore
    {
        private readonly InMemoryPassageRecordStore _inner = new();

        public bool FailSave { get; set; }

        public Func<PassageRecord, bool>? FailSaveFor { get; set; }

        public int FailMarkClearedCount { get; set; }

        public int RemainingSaveFailures { get; set; }

        public int SaveAttempts { get; private set; }

        public IReadOnlyList<PassageRecord> Records => _inner.Records;

        public void Save(PassageRecord record)
        {
            SaveAttempts++;
            if (RemainingSaveFailures > 0)
            {
                RemainingSaveFailures--;
                throw new IOException("模拟SQLite保存失败");
            }
            if (FailSave || FailSaveFor?.Invoke(record) == true) throw new IOException("模拟SQLite保存失败");
            _inner.Save(record);
        }

        public void Add(PassageRecord record) => Save(record);

        public void MarkCleared(Guid passageId, DateTimeOffset clearedAt)
        {
            if (FailMarkClearedCount > 0)
            {
                FailMarkClearedCount--;
                throw new IOException("模拟SQLite更新清除状态失败");
            }
            _inner.MarkCleared(passageId, clearedAt);
        }

        public void MarkAlarmAcknowledged(Guid passageId, DateTimeOffset acknowledgedAt) =>
            _inner.MarkAlarmAcknowledged(passageId, acknowledgedAt);

        public IReadOnlyList<PassageRecord> GetPendingClear() => _inner.GetPendingClear();

        public IReadOnlyList<PassageRecord> GetUnacknowledgedAlarms() => _inner.GetUnacknowledgedAlarms();

        public PassageQueryResult Query(PassageQuery query) => _inner.Query(query);

        public PassageRecord? GetDetails(Guid passageId) => _inner.GetDetails(passageId);

        public PassageStatistics GetStatistics(DateTimeOffset localNow, IEnumerable<string>? stationIds = null) => _inner.GetStatistics(localNow, stationIds);
    }

    private sealed class RecordingPassageEventLogger : IPassageEventLogger
    {
        public List<PassageEvent> Events { get; } = new();

        public void Write(PassageEvent passageEvent) => Events.Add(passageEvent);
    }
}
