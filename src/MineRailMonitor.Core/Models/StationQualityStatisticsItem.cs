namespace MineRailMonitor.Core.Models;

public sealed class StationQualityStatisticsItem
{
    public StationQualityStatisticsItem(
        string stationId,
        int passageCount,
        long? expectedCount,
        long? detectedCount,
        long? missingCount,
        double? completenessPercent,
        int uncouplingAlarmCount,
        int tagKinds,
        long tagOccurrences)
    {
        StationId = string.IsNullOrWhiteSpace(stationId)
            ? throw new ArgumentException("基站编号不能为空。", nameof(stationId))
            : stationId.Trim();
        if (passageCount < 1) throw new ArgumentOutOfRangeException(nameof(passageCount));
        if (uncouplingAlarmCount < 0) throw new ArgumentOutOfRangeException(nameof(uncouplingAlarmCount));
        if (tagKinds < 0) throw new ArgumentOutOfRangeException(nameof(tagKinds));
        if (tagOccurrences < 0) throw new ArgumentOutOfRangeException(nameof(tagOccurrences));

        PassageCount = passageCount;
        ExpectedCount = expectedCount;
        DetectedCount = detectedCount;
        MissingCount = missingCount;
        CompletenessPercent = completenessPercent;
        UncouplingAlarmCount = uncouplingAlarmCount;
        TagKinds = tagKinds;
        TagOccurrences = tagOccurrences;
    }

    public string StationId { get; }

    public int PassageCount { get; }

    public long? ExpectedCount { get; }

    public long? DetectedCount { get; }

    public long? MissingCount { get; }

    public double? CompletenessPercent { get; }

    public int UncouplingAlarmCount { get; }

    public int TagKinds { get; }

    public long TagOccurrences { get; }

    public bool RequiresAttention =>
        UncouplingAlarmCount > 0 ||
        MissingCount is > 0 ||
        CompletenessPercent is < 100d;
}
