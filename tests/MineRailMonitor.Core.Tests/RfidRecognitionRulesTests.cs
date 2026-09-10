using System.Net;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Recognition;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidRecognitionRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(8));

    [Theory]
    [InlineData(0x0001, true)]
    [InlineData(0x000A, true)]
    [InlineData(0x000B, false)]
    public void Head_rfid_range_is_01_to_0A(ushort rfid, bool expected)
    {
        Assert.Equal(expected, RfidRecognitionRules.IsHeadRfid(rfid));
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
