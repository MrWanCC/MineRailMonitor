using System.Net;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Recognition;

namespace MineRailMonitor.Core.Tests;

public sealed class StationRecognitionSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Head_only_starts_timeout_and_enters_uncoupling_state_after_timeout()
    {
        var session = CreateSession(timeout: TimeSpan.FromSeconds(1));

        session.Apply(CreateFrame(new ushort[] { 0x01 }));
        Assert.Equal(StationRecognitionState.Recognizing, session.State);
        Assert.Equal(Start, session.FirstVehicleAt);
        Assert.Equal(Start, session.LastNewVehicleAt);

        session.Evaluate(Start.AddSeconds(30));

        Assert.Equal(1, session.DetectedVehicleCount);
        Assert.Equal(StationRecognitionState.UncouplingAlarm, session.State);
    }

    [Fact]
    public void First_rfid_starts_timeout_and_later_unique_rfids_refresh_it()
    {
        var session = CreateSession();

        session.Apply(CreateFrame(new ushort[] { 0x01 }));
        session.Apply(CreateFrameAt(Start.AddSeconds(3), new ushort[] { 0x01, 0x11 }));

        Assert.Equal(Start, session.FirstVehicleAt);
        Assert.Equal(Start.AddSeconds(3), session.LastNewVehicleAt);
        Assert.Equal(StationRecognitionState.Recognizing, session.State);
    }

    [Fact]
    public void Multiple_tags_after_one_head_complete_at_expected_count()
    {
        var session = CreateSession(expectedVehicleCount: 3);
        session.Apply(CreateFrame(new ushort[] { 0x01 }));
        session.Apply(CreateFrameAt(Start.AddSeconds(1), new ushort[] { 0x01, 0x11 }));
        session.Apply(CreateFrameAt(Start.AddSeconds(2), new ushort[] { 0x01, 0x11, 0x12 }));

        Assert.Equal(3, session.DetectedVehicleCount);
        Assert.Equal(StationRecognitionState.Completed, session.State);
    }

    [Fact]
    public void Repeated_snapshot_does_not_reset_timer_or_count()
    {
        var session = CreateSession();
        session.Apply(CreateFrame(new ushort[] { 0x01, 0x11 }));
        session.Apply(CreateFrameAt(Start.AddMilliseconds(500), new ushort[] { 0x01, 0x11, 0x11 }));

        Assert.Equal(2, session.DetectedVehicleCount);
        Assert.Equal(Start, session.LastNewVehicleAt);
    }

    [Fact]
    public void Observed_sequence_uses_first_seen_time_not_hardware_slot_position()
    {
        var session = CreateSession();
        session.Apply(CreateFrame(new ushort[] { 0x00, 0x001A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }));
        session.Apply(CreateFrameAt(Start.AddSeconds(1), new ushort[] { 0x01, 0x001A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }));

        Assert.Equal(new ushort[] { 0x001A, 0x0001 }, session.ObservedVehicleSequence);
        Assert.Equal((ushort)0x001A, session.FirstValidRfid);
        Assert.Contains(RfidHeadWarning.FirstTagIsNotHead, session.HeadTagWarnings);
    }

    [Fact]
    public void One_frame_with_multiple_new_tags_adds_every_tag_and_starts_once()
    {
        var session = CreateSession();
        session.Apply(CreateFrame(new ushort[] { 0x01, 0x0011, 0x0012, 0x0013 }));

        Assert.Equal(4, session.DetectedVehicleCount);
        Assert.Equal(new ushort[] { 0x01, 0x0011, 0x0012, 0x0013 }, session.ObservedVehicleSequence);
        Assert.Equal(new ushort[] { 0x01, 0x0011, 0x0012, 0x0013 }, session.ObservedVehicleBatches[0]);
        Assert.Equal(Start, session.LastNewVehicleAt);
    }

    [Fact]
    public void Only_position_zero_values_are_heads_even_when_other_tags_are_in_legacy_range()
    {
        var session = CreateSession();
        session.Apply(CreateFrame(new ushort[] { 0x01, 0x03, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13 }));

        Assert.Equal(11, session.DetectedVehicleCount);
        Assert.Equal(StationRecognitionState.Completed, session.State);
        Assert.False(session.HeadTagWarning);
        Assert.Equal(1, session.HeadTagCount);
        Assert.Equal(new ushort[] { 0x01 }, session.HeadTagRfids);
    }

    [Fact]
    public void Eleven_unique_values_without_head_complete_with_missing_head_warning()
    {
        var session = CreateSession();
        session.Apply(CreateFrame(new ushort[] { 0x00, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15 }));

        Assert.Equal(11, session.DetectedVehicleCount);
        Assert.Equal(StationRecognitionState.Completed, session.State);
        Assert.Contains(RfidHeadWarning.MissingHeadTag, session.HeadTagWarnings);
        Assert.Contains(RfidHeadWarning.FirstTagIsNotHead, session.HeadTagWarnings);
    }

    [Fact]
    public void Position_zero_is_head_even_when_its_rfid_is_above_legacy_range()
    {
        var session = CreateSession();
        session.Apply(CreateFrame(new ushort[] { 0x1001, 0x0002, 0x1101, 0x1102, 0x1103, 0x1104, 0x1105, 0x1106, 0x1107, 0x1108, 0x1109 }));

        Assert.Equal(11, session.DetectedVehicleCount);
        Assert.Equal(StationRecognitionState.Completed, session.State);
        Assert.Equal(new ushort[] { 0x1001 }, session.HeadTagRfids);
        Assert.Empty(session.HeadTagWarnings);
    }

    [Fact]
    public void Different_rfid_in_position_one_does_not_create_multiple_head_warning()
    {
        var session = CreateSession();
        session.Apply(CreateFrame(new ushort[] { 0x1001, 0x0002, 0x1101 }));

        Assert.Equal(new ushort[] { 0x1001 }, session.HeadTagRfids);
        Assert.Equal(1, session.HeadTagCount);
        Assert.DoesNotContain(RfidHeadWarning.MultipleHeadTags, session.HeadTagWarnings);
    }

    [Fact]
    public void Ten_unique_values_alarm_after_timeout()
    {
        var session = CreateSession(timeout: TimeSpan.FromSeconds(30));
        session.Apply(CreateFrame(new ushort[] { 0x01, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19 }));

        session.Evaluate(Start.AddSeconds(29.9));
        Assert.Equal(StationRecognitionState.Recognizing, session.State);
        session.Evaluate(Start.AddSeconds(30));

        Assert.Equal(StationRecognitionState.UncouplingAlarm, session.State);
    }

    [Fact]
    public void Eleventh_unique_value_at_29_9_seconds_completes_without_alarm()
    {
        var session = CreateSession(timeout: TimeSpan.FromSeconds(30));
        session.Apply(CreateFrame(new ushort[] { 0x01, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19 }));
        session.Apply(CreateFrameAt(Start.AddSeconds(29.9), new ushort[] { 0x01, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A }));

        session.Evaluate(Start.AddSeconds(60));

        Assert.Equal(11, session.DetectedVehicleCount);
        Assert.Equal(StationRecognitionState.Completed, session.State);
    }

    [Fact]
    public void Tracker_keeps_station_sessions_independent()
    {
        var tracker = new StationRecognitionTracker(11, TimeSpan.FromSeconds(30));

        tracker.Apply(CreateFrame(0x01, Start, new ushort[] { 0x01, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19 }));
        tracker.Apply(CreateFrame(0x02, Start, new ushort[] { 0x01, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A }));
        tracker.Evaluate(Start.AddSeconds(30));

        Assert.Equal(StationRecognitionState.UncouplingAlarm, tracker.Sessions[0x01].State);
        Assert.Equal(StationRecognitionState.Completed, tracker.Sessions[0x02].State);
    }

    private static StationRecognitionSession CreateSession(TimeSpan? timeout = null, int expectedVehicleCount = 11) =>
        new(0x01, expectedVehicleCount, timeout ?? TimeSpan.FromSeconds(30));

    private static RfidStationFrame CreateFrame(IReadOnlyList<ushort> slots) => CreateFrame(0x01, Start, slots);

    private static RfidStationFrame CreateFrameAt(DateTimeOffset at, IReadOnlyList<ushort> slots) => CreateFrame(0x01, at, slots);

    private static RfidStationFrame CreateFrame(byte address, DateTimeOffset at, IReadOnlyList<ushort> slots)
    {
        var rawSlots = new ushort[14];
        slots.ToArray().CopyTo(rawSlots, 0);
        return new RfidStationFrame
        {
            StationAddress = address,
            Mode = 0x04,
            HeadRfid = rawSlots[0],
            WagonRfids = rawSlots.Skip(1).Where(value => value != 0).ToArray(),
            RawRfidSlots = rawSlots,
            ReceivedAt = at,
            SourceEndpoint = new IPEndPoint(IPAddress.Loopback, 62001)
        };
    }
}
