namespace MineRailMonitor.Core.Models;

public sealed class StationTagStatisticsItem
{
    public StationTagStatisticsItem(
        string stationId,
        ushort rfidValue,
        int passageCount,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt)
    {
        StationId = string.IsNullOrWhiteSpace(stationId)
            ? throw new ArgumentException("基站编号不能为空。", nameof(stationId))
            : stationId.Trim();
        if (passageCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(passageCount));
        }
        if (firstSeenAt > lastSeenAt)
        {
            throw new ArgumentException("首次观察时间不能晚于最近观察时间。", nameof(firstSeenAt));
        }

        RfidValue = rfidValue;
        PassageCount = passageCount;
        FirstSeenAt = firstSeenAt;
        LastSeenAt = lastSeenAt;
    }

    public string StationId { get; }

    public ushort RfidValue { get; }

    public int PassageCount { get; }

    public DateTimeOffset FirstSeenAt { get; }

    public DateTimeOffset LastSeenAt { get; }
}
