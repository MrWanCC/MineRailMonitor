using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class AlarmForwardingTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Forwards_the_last_raw_packet_that_added_a_new_rfid()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var requests = CaptureRequests(coordinator);
        var firstRaw = CreateRaw(0x11);
        var repeatedRaw = CreateRaw(0x22);
        var emptyRaw = CreateRaw(0x33, empty: true);

        coordinator.ProcessFrame(CreateFrame(Start, new ushort[] { 0x0001, 0x0011 }, firstRaw));
        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(29.9), new ushort[] { 0x0001, 0x0011 }, repeatedRaw));
        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(29.95), Array.Empty<ushort>(), emptyRaw));
        coordinator.Evaluate(Start.AddSeconds(30));

        var request = Assert.Single(requests);
        Assert.Equal(PassageOutcome.UncouplingAlarm, Assert.Single(store.Records).Outcome);
        Assert.True(firstRaw.SequenceEqual(request.Payload));
        Assert.False(repeatedRaw.SequenceEqual(request.Payload));
        Assert.False(emptyRaw.SequenceEqual(request.Payload));
    }

    [Fact]
    public void Repeated_evaluation_and_clear_lifecycle_create_only_one_request()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var requests = CaptureRequests(coordinator);

        coordinator.ProcessFrame(CreateFrame(Start, new ushort[] { 0x0001, 0x0011 }, CreateRaw(0x41)));
        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(29.9), new ushort[] { 0x0001, 0x0011 }, CreateRaw(0x42)));
        coordinator.Evaluate(Start.AddSeconds(30));
        coordinator.Evaluate(Start.AddSeconds(31));
        coordinator.Evaluate(Start.AddSeconds(32));

        var passageId = Assert.Single(store.Records).PassageId;
        coordinator.AcknowledgeAlarm(passageId, Start.AddSeconds(33));
        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddSeconds(34));
        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(35), Array.Empty<ushort>(), CreateRaw(0x42, empty: true)));
        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(36), Array.Empty<ushort>(), CreateRaw(0x43, empty: true)));

        Assert.Single(requests);
    }

    [Fact]
    public void A_new_passage_after_reset_can_create_a_second_request()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var requests = CaptureRequests(coordinator);

        coordinator.ProcessFrame(CreateFrame(Start, new ushort[] { 0x0001, 0x0011 }, CreateRaw(0x51)));
        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(29.9), new ushort[] { 0x0001, 0x0011 }, CreateRaw(0x52)));
        coordinator.Evaluate(Start.AddSeconds(30));
        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddSeconds(31));
        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(32), Array.Empty<ushort>(), CreateRaw(0x52, empty: true)));
        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(33), Array.Empty<ushort>(), CreateRaw(0x53, empty: true)));

        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(40), new ushort[] { 0x0002, 0x0021 }, CreateRaw(0x61)));
        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(69.9), new ushort[] { 0x0002, 0x0021 }, CreateRaw(0x62)));
        coordinator.Evaluate(Start.AddSeconds(70));

        Assert.Equal(2, requests.Count);
        Assert.True(CreateRaw(0x51).SequenceEqual(requests[0].Payload));
        Assert.True(CreateRaw(0x61).SequenceEqual(requests[1].Payload));
    }

    [Fact]
    public void A_failed_save_does_not_request_forward_until_retry_succeeds()
    {
        var store = new FailFirstSaveStore();
        var coordinator = CreateCoordinator(store);
        var requests = CaptureRequests(coordinator);

        coordinator.ProcessFrame(CreateFrame(Start, new ushort[] { 0x0001, 0x0011 }, CreateRaw(0x71)));
        coordinator.ProcessFrame(CreateFrame(Start.AddSeconds(29.9), new ushort[] { 0x0001, 0x0011 }, CreateRaw(0x72)));
        coordinator.Evaluate(Start.AddSeconds(30));
        Assert.Empty(requests);
        Assert.Empty(store.Records);

        coordinator.Evaluate(Start.AddSeconds(30.1));

        Assert.Single(requests);
        Assert.Single(store.Records);
        Assert.Equal(PassageOutcome.UncouplingAlarm, store.Records[0].Outcome);
    }

    private static List<AlarmForwardRequest> CaptureRequests(RfidRuntimeCoordinator coordinator)
    {
        var requests = new List<AlarmForwardRequest>();
        coordinator.AlarmForwardRequested += requests.Add;
        return requests;
    }

    private static RfidRuntimeCoordinator CreateCoordinator(IPassageRecordStore store) => new(
        new[]
        {
            new RfidStationConfig
            {
                StationId = "RFID-560-01",
                Name = "560 一号基站",
                YardId = "560",
                IpAddress = "127.0.0.1",
                Port = 62001,
                ProtocolAddress = 0x01
            }
        },
        new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
        store,
        new RfidRuntimePolicy(TimeSpan.FromSeconds(5), 3, 2));

    private static RfidStationFrame CreateFrame(
        DateTimeOffset at,
        IReadOnlyList<ushort> values,
        byte[] raw)
    {
        var slots = new ushort[14];
        values.Take(14).ToArray().CopyTo(slots, 0);
        return new RfidStationFrame
        {
            StationAddress = 0x01,
            Mode = 0x04,
            RawRfidSlots = slots,
            ValidRfids = slots.Where(value => value != 0).ToArray(),
            ReportedCardCount = (byte)values.Count,
            ActualNonZeroSlotCount = values.Count,
            ReceivedAt = at,
            SourceEndpoint = new IPEndPoint(IPAddress.Loopback, 62001),
            RawData = raw
        };
    }

    private static byte[] CreateRaw(byte marker, bool empty = false)
    {
        var raw = new byte[40];
        raw[0] = 0xB0;
        raw[1] = 0xB0;
        raw[2] = 0x01;
        raw[3] = 0x04;
        raw[7] = empty ? (byte)0 : (byte)2;
        raw[8] = empty ? (byte)0 : marker;
        raw[38] = 0xAA;
        raw[39] = 0xAA;
        return raw;
    }

    private sealed class FailFirstSaveStore : IPassageRecordStore
    {
        private readonly InMemoryPassageRecordStore _inner = new();
        private bool _failed;

        public IReadOnlyList<PassageRecord> Records => _inner.Records;

        public void Save(PassageRecord record)
        {
            if (!_failed)
            {
                _failed = true;
                throw new IOException("simulated first persistence failure");
            }

            _inner.Save(record);
        }

        public void Add(PassageRecord record) => _inner.Add(record);
        public void MarkCleared(Guid passageId, DateTimeOffset clearedAt) => _inner.MarkCleared(passageId, clearedAt);
        public void MarkAlarmAcknowledged(Guid passageId, DateTimeOffset acknowledgedAt) => _inner.MarkAlarmAcknowledged(passageId, acknowledgedAt);
        public IReadOnlyList<PassageRecord> GetPendingClear() => _inner.GetPendingClear();
        public IReadOnlyList<PassageRecord> GetUnacknowledgedAlarms() => _inner.GetUnacknowledgedAlarms();
        public PassageQueryResult Query(PassageQuery query) => _inner.Query(query);
        public PassageRecord? GetDetails(Guid passageId) => _inner.GetDetails(passageId);
        public PassageStatistics GetStatistics(DateTimeOffset localNow) => _inner.GetStatistics(localNow);
    }
}
