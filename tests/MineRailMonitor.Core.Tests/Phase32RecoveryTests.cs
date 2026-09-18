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
    public void Save_failure_keeps_frozen_passage_and_never_requests_clear()
    {
        var store = new TestPassageRecordStore { FailSave = true };
        var coordinator = CreateCoordinator(store);
        var values = Enumerable.Range(0, 11).Select(index => (ushort)(index == 0 ? 0x0001 : 0x0011 + index - 1)).ToArray();

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        coordinator.Evaluate(Start.AddSeconds(1));
        coordinator.Evaluate(Start.AddSeconds(2));

        var state = coordinator.States[0x01];
        Assert.Equal(PassageLifecycleState.Finalizing, state.LifecycleState);
        Assert.True(state.PersistenceWarning);
        Assert.NotNull(state.LastPassageRecord);
        Assert.False(state.PendingClear);
        Assert.Equal(RfidPollCommand.Read, coordinator.GetCommand(0x01));
        Assert.Equal(3, state.PersistenceAttemptCount);
        Assert.Empty(store.Records);
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
        Assert.Equal(PassageLifecycleState.Completed, coordinator.States[0x02].LifecycleState);
        Assert.True(coordinator.States[0x02].PendingClear);
        Assert.Single(store.Records);
        Assert.Equal((byte)0x02, store.Records[0].StationAddress);
    }

    private static RfidRuntimeCoordinator CreateCoordinator(TestPassageRecordStore store) =>
        new(
            new byte[] { 0x01 },
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            store,
            new RfidRuntimePolicy(TimeSpan.FromSeconds(5), maxClearAttempts: 3, emptyConfirmationReads: 2, maxPersistenceAttempts: 3));

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

        public IReadOnlyList<PassageRecord> Records => _inner.Records;

        public void Save(PassageRecord record)
        {
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

        public PassageStatistics GetStatistics(DateTimeOffset localNow) => _inner.GetStatistics(localNow);
    }
}
