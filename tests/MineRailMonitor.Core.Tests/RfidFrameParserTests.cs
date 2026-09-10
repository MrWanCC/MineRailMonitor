using System.Net;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidFrameParserTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 8, 14, 13, 30, TimeSpan.FromHours(8));
    private static readonly IPEndPoint SourceEndpoint = new(IPAddress.Loopback, 62001);

    [Fact]
    public void Parse_NormalTenWagons_PreservesSlotsAndMetadata()
    {
        var buffer = CreateFrame(10, 0x12, 0x34);
        var parser = new RfidFrameParser(new RfidFrameParserOptions { EmptyRfidValue = 0 });

        var parsed = parser.TryParse(buffer, SourceEndpoint, ReceivedAt, out var frame);

        Assert.True(parsed);
        Assert.NotNull(frame);
        Assert.Equal((byte)3, frame.StationAddress);
        Assert.Equal((byte)4, frame.Mode);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x0B }, frame.CommandBytes);
        Assert.Equal((byte)0x0B, frame.ReportedCardCount);
        Assert.Equal(11, frame.ActualNonZeroSlotCount);
        Assert.False(frame.ProtocolDataWarning);
        Assert.Equal((ushort)0x0001, frame.HeadRfid);
        Assert.Equal(new ushort[] { 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019, 0x001A }, frame.WagonRfids);
        Assert.Equal(new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019, 0x001A }, frame.ValidRfids);
        Assert.Equal((byte)0x12, frame.CrcHigh);
        Assert.Equal((byte)0x34, frame.CrcLow);
        Assert.Equal(new ushort[] { 0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019, 0x001A, 0, 0, 0 }, frame.RawRfidSlots);
        Assert.Equal(ReceivedAt, frame.ReceivedAt);
        Assert.Equal(SourceEndpoint, frame.SourceEndpoint);
        Assert.Equal(buffer, frame.RawData);
    }

    [Fact]
    public void Parse_SixWagons_FiltersRemainingEmptySlots()
    {
        var parser = new RfidFrameParser();

        Assert.True(parser.TryParse(CreateFrame(6), SourceEndpoint, ReceivedAt, out var frame));
        Assert.NotNull(frame);
        Assert.Equal((ushort)0x0001, frame.HeadRfid);
        Assert.Equal((byte)0x07, frame.ReportedCardCount);
        Assert.Equal(7, frame.ActualNonZeroSlotCount);
        Assert.Equal(new ushort[] { 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016 }, frame.WagonRfids);
        Assert.Equal(7, frame.RawRfidSlots.Count(value => value != 0));
        Assert.All(frame.RawRfidSlots.Skip(7), value => Assert.Equal((ushort)0, value));
    }

    [Fact]
    public void Parse_ReadsAllFourteenSlots_AndWarnsWhenReportedCountDiffers()
    {
        var buffer = CreateFrame(0);
        WriteLittleEndian(buffer, 8, 0x0000);
        buffer[7] = 0x01;
        WriteLittleEndian(buffer, 8 + 12 * 2, 0x0021);
        WriteLittleEndian(buffer, 8 + 13 * 2, 0x001D);

        Assert.True(new RfidFrameParser().TryParse(buffer, SourceEndpoint, ReceivedAt, out var frame));
        Assert.NotNull(frame);
        Assert.Equal(new ushort[] { 0x0021, 0x001D }, frame!.ValidRfids);
        Assert.Equal((ushort)0x001D, frame.RawRfidSlots[13]);
        Assert.Equal((byte)0x01, frame.ReportedCardCount);
        Assert.Equal(2, frame.ActualNonZeroSlotCount);
        Assert.True(frame.ProtocolDataWarning);
    }

    [Fact]
    public void Parse_RealHardwareSlotFixtures_ReadsTheCompleteFourteenSlotSnapshot()
    {
        var fixtures = new[]
        {
            new ushort[] { },
            new ushort[] { 0x001A, 0x001F },
            new ushort[] { 0x001A, 0x001F, 0x0021, 0x000E, 0x001D },
            new ushort[] { 0x001A, 0x000E, 0x0021, 0x001D, 0x0017, 0x001F },
            new ushort[] { 0x001A, 0x0021, 0x001D, 0x000E, 0x0017, 0x001F, 0x000C, 0x0022, 0x0016, 0x000D, 0x0018, 0x000B, 0x0014, 0x001E }
        };

        foreach (var fixture in fixtures)
        {
            var buffer = CreateFrameWithSlots(fixture);
            Assert.True(new RfidFrameParser().TryParse(buffer, SourceEndpoint, ReceivedAt, out var frame));
            Assert.NotNull(frame);
            Assert.Equal(fixture.Length, frame!.ReportedCardCount);
            Assert.Equal(fixture.Length, frame.ActualNonZeroSlotCount);
            Assert.Equal(fixture, frame.ValidRfids);
            Assert.Equal(14, frame.RawRfidSlots.Count);
        }
    }

    [Theory]
    [InlineData(39)]
    [InlineData(41)]
    public void Parse_InvalidLength_ReturnsFalse(int length)
    {
        var parser = new RfidFrameParser();

        Assert.False(parser.TryParse(new byte[length], SourceEndpoint, ReceivedAt, out var frame));
        Assert.Null(frame);
    }

    [Fact]
    public void Parse_InvalidHeader_ReturnsFalse()
    {
        var buffer = CreateFrame(10);
        buffer[0] = 0x00;

        Assert.False(new RfidFrameParser().TryParse(buffer, SourceEndpoint, ReceivedAt, out _));
    }

    [Fact]
    public void Parse_InvalidTail_ReturnsFalse()
    {
        var buffer = CreateFrame(10);
        buffer[39] = 0x00;

        Assert.False(new RfidFrameParser().TryParse(buffer, SourceEndpoint, ReceivedAt, out _));
    }

    [Fact]
    public void Parse_NonRfidMode_ReturnsFalseWithoutBusinessFrame()
    {
        var buffer = CreateFrame(10);
        buffer[3] = 0x05;

        Assert.False(new RfidFrameParser().TryParse(buffer, SourceEndpoint, ReceivedAt, out var frame));
        Assert.Null(frame);
    }

    private static byte[] CreateFrame(int wagonCount, byte crcHigh = 0, byte crcLow = 0)
    {
        var buffer = new byte[40];
        buffer[0] = 0xB0;
        buffer[1] = 0xB0;
        buffer[2] = 0x03;
        buffer[3] = 0x04;
        buffer[4] = 0x11;
        buffer[5] = 0x22;
        buffer[6] = 0x33;
        buffer[7] = (byte)(wagonCount + 1);
        WriteLittleEndian(buffer, 8, 0x0001);
        for (var wagon = 1; wagon <= wagonCount; wagon++)
        {
            WriteLittleEndian(buffer, 8 + wagon * 2, 0x0010 + wagon);
        }

        buffer[36] = crcHigh;
        buffer[37] = crcLow;
        buffer[38] = 0xAA;
        buffer[39] = 0xAA;
        return buffer;
    }

    private static byte[] CreateFrameWithSlots(IReadOnlyList<ushort> values)
    {
        var buffer = new byte[40];
        buffer[0] = 0xB0;
        buffer[1] = 0xB0;
        buffer[2] = 0x01;
        buffer[3] = 0x04;
        buffer[7] = (byte)values.Count;
        for (var index = 0; index < values.Count; index++)
        {
            WriteLittleEndian(buffer, 8 + index * 2, values[index]);
        }

        buffer[38] = 0xAA;
        buffer[39] = 0xAA;
        return buffer;
    }

    private static void WriteLittleEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)(value >> 8);
    }
}
