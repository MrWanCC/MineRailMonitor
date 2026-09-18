using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class PassageLifecycleTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Normal_eleven_vehicle_passage_is_saved_before_clear_and_returns_idle_after_two_empty_reads()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var values = new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019, 0x001A };

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));

        var state = coordinator.States[0x01];
        Assert.Equal(PassageLifecycleState.Completed, state.LifecycleState);
        Assert.True(state.PendingClear);
        Assert.Single(store.Records);
        Assert.Equal(PassageOutcome.Completed, store.Records[0].Outcome);
        Assert.Equal(11, store.Records[0].DetectedVehicleCount);

        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddMilliseconds(200));
        Assert.Equal(PassageLifecycleState.Clearing, state.LifecycleState);
        Assert.False(state.PendingClear);

        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddMilliseconds(400), Array.Empty<ushort>()));
        Assert.Equal(PassageLifecycleState.WaitForEmpty, state.LifecycleState);
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddMilliseconds(600), Array.Empty<ushort>()));

        Assert.Equal(PassageLifecycleState.Idle, state.LifecycleState);
        Assert.Single(store.Records);
    }

    [Fact]
    public void Ten_vehicle_passage_becomes_alarm_after_timeout_and_is_saved_before_clear()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var values = new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019 };

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(29.9), values));
        coordinator.Evaluate(Start.AddSeconds(30));

        var state = coordinator.States[0x01];
        Assert.Equal(PassageLifecycleState.Alarm, state.LifecycleState);
        Assert.Single(store.Records);
        Assert.Equal(PassageOutcome.UncouplingAlarm, store.Records[0].Outcome);
        Assert.Equal("脱节报警", store.Records[0].AlarmMessage);
        Assert.True(state.PendingClear);
    }

    [Fact]
    public void Uncoupling_alarm_stays_red_until_the_alarm_passage_is_cleared()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var values = new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019 };

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(29.9), values));
        coordinator.Evaluate(Start.AddSeconds(30));

        var state = coordinator.States[0x01];
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);

        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddSeconds(30.1));
        coordinator.Evaluate(Start.AddSeconds(35.1));

        Assert.Equal(PassageLifecycleState.Clearing, state.LifecycleState);
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);
    }

    [Fact]
    public void Cleared_uncoupling_alarm_keeps_the_red_prompt_until_the_next_passage_starts()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var alarmValues = new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019 };

        coordinator.ProcessFrame(CreateFrame(0x01, Start, alarmValues));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(29.9), alarmValues));
        coordinator.Evaluate(Start.AddSeconds(30));
        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddSeconds(30.1));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(30.2), Array.Empty<ushort>()));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(30.4), Array.Empty<ushort>()));

        var state = coordinator.States[0x01];
        Assert.Equal(PassageLifecycleState.Idle, state.LifecycleState);
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);

        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(31), new ushort[] { 0x0002 }));

        Assert.Equal(PassageLifecycleState.Recognizing, state.LifecycleState);
        Assert.Equal(RfidStationVisualState.Alarm, state.VisualState);
    }

    [Fact]
    public void Waiting_for_empty_never_starts_a_second_passage_when_old_tags_reappear()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var values = new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019, 0x001A };

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddMilliseconds(200));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(1), values));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(2), Array.Empty<ushort>()));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(3), Array.Empty<ushort>()));

        Assert.Equal(PassageLifecycleState.Idle, coordinator.States[0x01].LifecycleState);
        Assert.Single(store.Records);
    }

    [Fact]
    public void The_next_passage_gets_a_new_passage_id_after_the_station_returns_idle()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var first = new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019, 0x001A };
        var second = new ushort[] { 0x0002, 0x0021, 0x0022, 0x0023, 0x0024, 0x0025, 0x0026, 0x0027, 0x0028, 0x0029, 0x002A };

        CompleteAndEmpty(coordinator, Start, first);
        CompleteAndEmpty(coordinator, Start.AddSeconds(3), second);

        Assert.Equal(2, store.Records.Count);
        Assert.NotEqual(store.Records[0].PassageId, store.Records[1].PassageId);
        Assert.Equal((ushort)0x0001, store.Records[0].HeadRfid);
        Assert.Equal((ushort)0x0002, store.Records[1].HeadRfid);
    }

    [Fact]
    public void Two_stations_keep_independent_sessions_and_alarm_states()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store, 0x01, 0x02);
        var ten = new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019 };
        var eleven = ten.Concat(new ushort[] { 0x001A }).ToArray();

        coordinator.ProcessFrame(CreateFrame(0x01, Start, ten));
        coordinator.ProcessFrame(CreateFrame(0x02, Start, eleven));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(29.9), ten));
        coordinator.ProcessFrame(CreateFrame(0x02, Start.AddSeconds(29.9), eleven));
        coordinator.Evaluate(Start.AddSeconds(30));

        Assert.Equal(PassageLifecycleState.Alarm, coordinator.States[0x01].LifecycleState);
        Assert.Equal(PassageLifecycleState.Completed, coordinator.States[0x02].LifecycleState);
        Assert.Equal(PassageOutcome.UncouplingAlarm, store.Records.Single(record => record.StationAddress == 0x01).Outcome);
        Assert.Equal(PassageOutcome.Completed, store.Records.Single(record => record.StationAddress == 0x02).Outcome);
    }

    [Fact]
    public void Passage_records_keep_stable_station_ids_for_same_protocol_at_distinct_endpoints()
    {
        var store = new InMemoryPassageRecordStore();
        var stations = new[]
        {
            new RfidStationConfig
            {
                StationId = "RFID-01",
                Name = "一号基站",
                IpAddress = "127.0.0.1",
                Port = 10001,
                ProtocolAddress = 0x01
            },
            new RfidStationConfig
            {
                StationId = "RFID-02",
                Name = "二号基站",
                IpAddress = "127.0.0.1",
                Port = 10002,
                ProtocolAddress = 0x01
            }
        };
        var coordinator = new RfidRuntimeCoordinator(
            stations,
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            store,
            new RfidRuntimePolicy(TimeSpan.FromSeconds(5), maxClearAttempts: 3, emptyConfirmationReads: 2));
        var values = new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019, 0x001A };

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values, new IPEndPoint(IPAddress.Loopback, 10001)));
        coordinator.ProcessFrame(CreateFrame(0x01, Start, values, new IPEndPoint(IPAddress.Loopback, 10002)));

        Assert.Equal(new[] { "RFID-01", "RFID-02" }, store.Records.Select(record => record.StationId).OrderBy(id => id));
    }

    [Fact]
    public void Head_warnings_do_not_turn_a_complete_passage_into_an_alarm()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var values = new ushort[] { 0x0001, 0x0003, 0x000B, 0x000C, 0x000D, 0x000E, 0x000F, 0x0010, 0x0011, 0x0012, 0x0013 };

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));

        var state = coordinator.States[0x01];
        Assert.Equal(PassageLifecycleState.Completed, state.LifecycleState);
        Assert.Equal(RfidStationVisualState.Warning, state.VisualState);
        Assert.Equal(PassageOutcome.Completed, store.Records[0].Outcome);
        Assert.Contains(state.WarningMessages, warning => warning.StartsWith("检测到多个车头标签", StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_change_only_affects_a_new_passage_session()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var firstSeven = new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016 };
        var nextEight = new ushort[] { 0x0002, 0x0021, 0x0022, 0x0023, 0x0024, 0x0025, 0x0026, 0x0027 };

        coordinator.ProcessFrame(CreateFrame(0x01, Start, firstSeven));
        coordinator.UpdateDefaults(new RfidSettings { ExpectedVehicleCount = 8, InterVehicleTimeoutSeconds = 30 });

        Assert.Equal(11, coordinator.States[0x01].ExpectedVehicleCount);
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(29.9), firstSeven));
        coordinator.Evaluate(Start.AddSeconds(30));
        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddSeconds(30.2));
        CompleteClearAndEmpty(coordinator, Start.AddSeconds(31));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(34), nextEight));

        Assert.Equal(8, coordinator.States[0x01].ExpectedVehicleCount);
        Assert.Equal(PassageLifecycleState.Completed, coordinator.States[0x01].LifecycleState);
        Assert.Equal(2, store.Records.Count);
    }

    [Fact]
    public void Offline_communication_does_not_create_an_uncoupling_alarm()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var values = new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019 };

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        coordinator.Evaluate(Start.AddSeconds(31));

        Assert.Equal(StationCommunicationState.Offline, coordinator.States[0x01].CommunicationState);
        Assert.Equal(PassageLifecycleState.Recognizing, coordinator.States[0x01].LifecycleState);
        Assert.Empty(store.Records);
    }

    [Fact]
    public void Clear_retries_stop_at_the_configured_limit_and_continue_reading()
    {
        var store = new InMemoryPassageRecordStore();
        var coordinator = CreateCoordinator(store);
        var values = new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019, 0x001A };

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.Equal(RfidPollCommand.Clear, coordinator.GetCommand(0x01));
            coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddSeconds(attempt));
            coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(attempt).AddMilliseconds(100), values));
        }

        var state = coordinator.States[0x01];
        Assert.Equal(3, state.ClearAttempts);
        Assert.False(state.PendingClear);
        Assert.Equal(RfidPollCommand.Read, coordinator.GetCommand(0x01));
        Assert.Contains(state.WarningMessages, warning => warning.IndexOf("清除重试已达3次", StringComparison.Ordinal) >= 0);
    }

    private static RfidRuntimeCoordinator CreateCoordinator(InMemoryPassageRecordStore store, params byte[] addresses) =>
        new(
            addresses.Length == 0 ? new byte[] { 0x01 } : addresses,
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            store,
            new RfidRuntimePolicy(TimeSpan.FromSeconds(5), maxClearAttempts: 3, emptyConfirmationReads: 2));

    private static void CompleteAndEmpty(RfidRuntimeCoordinator coordinator, DateTimeOffset at, IReadOnlyList<ushort> values)
    {
        coordinator.ProcessFrame(CreateFrame(0x01, at, values));
        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, at.AddMilliseconds(200));
        CompleteClearAndEmpty(coordinator, at.AddMilliseconds(400));
    }

    private static void CompleteClearAndEmpty(RfidRuntimeCoordinator coordinator, DateTimeOffset at)
    {
        coordinator.ProcessFrame(CreateFrame(0x01, at, Array.Empty<ushort>()));
        coordinator.ProcessFrame(CreateFrame(0x01, at.AddMilliseconds(200), Array.Empty<ushort>()));
    }

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
}
