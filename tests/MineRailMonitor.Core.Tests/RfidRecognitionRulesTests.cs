using System.Net;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Recognition;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidRecognitionRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Analysis_identifies_high_value_rfid_in_position_zero_as_head()
    {
        var frame = CreateFrame(new ushort[] { 0x1001, 0x1101, 0x1102 });
        frame.HeadRfid = 0x0002;

        var analysis = RfidRecognitionRules.Analyze(frame, emptyRfidValue: 0);

        Assert.Equal(new ushort[] { 0x1001 }, analysis.HeadRfids);
        Assert.Empty(analysis.HeadWarnings);
    }

    [Fact]
    public void Analysis_keeps_0001_as_head_when_it_is_in_position_zero()
    {
        var frame = CreateFrame(new ushort[] { 0x0001, 0x0011, 0x0012 });

        var analysis = RfidRecognitionRules.Analyze(frame, emptyRfidValue: 0);

        Assert.Equal(new ushort[] { 0x0001 }, analysis.HeadRfids);
        Assert.Empty(analysis.HeadWarnings);
    }

    [Fact]
    public void Analysis_does_not_treat_position_one_rfid_0002_as_head()
    {
        var frame = CreateFrame(new ushort[] { 0x0000, 0x0002 });

        var analysis = RfidRecognitionRules.Analyze(frame, emptyRfidValue: 0);

        Assert.Empty(analysis.HeadRfids);
        Assert.Contains(RfidHeadWarning.MissingHeadTag, analysis.HeadWarnings);
        Assert.Contains(RfidHeadWarning.FirstTagIsNotHead, analysis.HeadWarnings);
    }

    [Fact]
    public void Analysis_preserves_slot_order_and_counts_unique_non_empty_values()
    {
        var frame = CreateFrame(new ushort[] { 0x01, 0x11, 0x12, 0x11, 0x13, 0x00 });

        var analysis = RfidRecognitionRules.Analyze(frame, emptyRfidValue: 0);

        Assert.Equal(new ushort[] { 0x01, 0x11, 0x12, 0x13 }, analysis.UniqueRfids);
        Assert.Equal(new ushort[] { 0x01 }, analysis.HeadRfids);
        Assert.Equal((ushort)0x01, analysis.FirstValidRfid);
    }

    [Fact]
    public void Analysis_uses_all_valid_values_from_sparse_snapshot()
    {
        var frame = CreateFrame(new ushort[] { 0x00, 0x001A, 0x00, 0x001D });
        frame.ValidRfids = new ushort[] { 0x001A, 0x001D };

        var analysis = RfidRecognitionRules.Analyze(frame, emptyRfidValue: 0);

        Assert.Equal(new ushort[] { 0x001A, 0x001D }, analysis.UniqueRfids);
        Assert.Equal((ushort)0x001A, analysis.FirstValidRfid);
    }

    private static RfidStationFrame CreateFrame(IReadOnlyList<ushort> slots)
    {
        var rawSlots = new ushort[14];
        slots.ToArray().CopyTo(rawSlots, 0);
        return new RfidStationFrame
        {
            StationAddress = 0x01,
            HeadRfid = rawSlots[0],
            WagonRfids = rawSlots.Skip(1).Where(value => value != 0).ToArray(),
            RawRfidSlots = rawSlots,
            ReceivedAt = Start,
            SourceEndpoint = new IPEndPoint(IPAddress.Loopback, 62001)
        };
    }
}
