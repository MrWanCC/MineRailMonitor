using System.Globalization;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Communication;

/// <summary>
/// Formats the parsed fields needed to diagnose an incoming RFID datagram.
/// This is intentionally presentation-only and does not participate in frame processing.
/// </summary>
public static class RfidReceiveDiagnosticFormatter
{
    public static string Format(
        string yardId,
        string? stationId,
        RfidUdpDatagramEventArgs datagram,
        RfidStationFrame? parsedFrame)
    {
        if (yardId is null) throw new ArgumentNullException(nameof(yardId));
        if (datagram is null) throw new ArgumentNullException(nameof(datagram));

        var data = datagram.Data;
        var address = data.Length >= 3
            ? data[2].ToString("X2", CultureInfo.InvariantCulture)
            : "--";
        var reportedCount = data.Length >= 8
            ? data[7].ToString(CultureInfo.InvariantCulture)
            : "--";
        var actualCount = parsedFrame?.ActualNonZeroSlotCount.ToString(CultureInfo.InvariantCulture) ?? "--";
        var tags = parsedFrame is null
            ? "--"
            : string.Join(
                " ",
                parsedFrame.ValidRfids
                    .Take(8)
                    .Select(value => value.ToString("X4", CultureInfo.InvariantCulture)));

        return $"[RFID RX] Time={datagram.ReceivedAt:yyyy-MM-dd HH:mm:ss.fff} " +
               $"Yard={yardId} " +
               $"SourceIP={datagram.RemoteEndPoint.Address} " +
               $"SourcePort={datagram.RemoteEndPoint.Port} " +
               $"StationId={stationId ?? "未匹配"} " +
               $"Addr={address} " +
               $"Count={reportedCount} " +
               $"ActualNonEmpty={actualCount} " +
               $"Tags={tags}";
    }
}
