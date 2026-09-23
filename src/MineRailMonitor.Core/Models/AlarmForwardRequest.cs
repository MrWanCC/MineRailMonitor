namespace MineRailMonitor.Core.Models;

public sealed class AlarmForwardRequest
{
    public AlarmForwardRequest(
        Guid passageId,
        string stationId,
        byte stationAddress,
        byte[] payload,
        DateTimeOffset requestedAt)
    {
        if (passageId == Guid.Empty) throw new ArgumentException("PassageId must not be empty.", nameof(passageId));
        if (string.IsNullOrWhiteSpace(stationId)) throw new ArgumentException("StationId must not be empty.", nameof(stationId));
        Payload = payload is null ? throw new ArgumentNullException(nameof(payload)) : (byte[])payload.Clone();
        PassageId = passageId;
        StationId = stationId;
        StationAddress = stationAddress;
        RequestedAt = requestedAt;
    }

    public Guid PassageId { get; }

    public string StationId { get; }

    public byte StationAddress { get; }

    public byte[] Payload { get; }

    public DateTimeOffset RequestedAt { get; }

    public bool HasPayload => Payload.Length > 0;
}
