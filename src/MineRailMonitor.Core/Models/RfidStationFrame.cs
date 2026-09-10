using System.Net;

namespace MineRailMonitor.Core.Models;

public sealed class RfidStationFrame
{
    public byte StationAddress { get; set; }

    public byte Mode { get; set; }

    public IReadOnlyList<byte> CommandBytes { get; set; } = new byte[4];

    public ushort HeadRfid { get; set; }

    public IReadOnlyList<ushort> WagonRfids { get; set; } = new List<ushort>();

    public IReadOnlyList<ushort> RawRfidSlots { get; set; } = new ushort[14];

    /// <summary>
    /// All non-empty values from the complete 14-slot snapshot, in slot order.
    /// The hardware may place a tag in any slot; this list is not a vehicle-order claim.
    /// </summary>
    public IReadOnlyList<ushort> ValidRfids { get; set; } = Array.Empty<ushort>();

    /// <summary>Byte7 reported by the station. Real hardware uses it as the non-empty slot count.</summary>
    public byte ReportedCardCount { get; set; }

    public int ActualNonZeroSlotCount { get; set; }

    public bool ProtocolDataWarning { get; set; }

    public byte CrcHigh { get; set; }

    public byte CrcLow { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public IPEndPoint SourceEndpoint { get; set; } = new(IPAddress.None, 0);

    public byte[] RawData { get; set; } = Array.Empty<byte>();
}
