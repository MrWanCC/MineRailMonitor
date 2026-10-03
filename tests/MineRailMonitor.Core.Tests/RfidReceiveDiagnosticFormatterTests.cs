using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidReceiveDiagnosticFormatterTests
{
    [Fact]
    public void Format_includes_endpoint_station_address_counts_and_first_eight_tags()
    {
        var receivedAt = new DateTimeOffset(2026, 9, 30, 15, 30, 12, 345, TimeSpan.FromHours(8));
        var endpoint = new IPEndPoint(IPAddress.Parse("192.168.16.20"), 10007);
        var data = new byte[40];
        data[2] = 0x01;
        data[7] = 0x0B;
        var datagram = new RfidUdpDatagramEventArgs(data, endpoint, receivedAt);
        var frame = new RfidStationFrame
        {
            StationAddress = 0x01,
            ReportedCardCount = 11,
            ActualNonZeroSlotCount = 11,
            ValidRfids = new ushort[]
            {
                0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019, 0x001A
            }
        };

        var result = RfidReceiveDiagnosticFormatter.Format(
            "620",
            "RFID-620-01",
            datagram,
            frame);

        Assert.Equal(
            "[RFID RX] Time=2026-09-30 15:30:12.345 Yard=620 SourceIP=192.168.16.20 SourcePort=10007 StationId=RFID-620-01 Addr=01 Count=11 ActualNonEmpty=11 Tags=0001 0011 0012 0013 0014 0015 0016 0017",
            result);
    }
}
