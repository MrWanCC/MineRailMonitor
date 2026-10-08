namespace MineRailMonitor.Core.Models;

public sealed class StationTagStatisticsQueryResult
{
    public StationTagStatisticsQueryResult(
        IReadOnlyList<StationQualityStatisticsItem> stations,
        IReadOnlyList<StationTagStatisticsItem> tags)
    {
        Stations = stations ?? throw new ArgumentNullException(nameof(stations));
        Tags = tags ?? throw new ArgumentNullException(nameof(tags));
        StationCount = stations.Count;
        PassageCount = stations.Sum(item => (long)item.PassageCount);
        var verifiableStations = stations.Where(item => item.ExpectedCount.HasValue).ToArray();
        ExpectedCount = verifiableStations.Length == 0
            ? null
            : verifiableStations.Sum(item => item.ExpectedCount!.Value);
        DetectedCount = verifiableStations.Length == 0
            ? null
            : verifiableStations.Sum(item => item.DetectedCount!.Value);
        MissingCount = verifiableStations.Length == 0
            ? null
            : verifiableStations.Sum(item => item.MissingCount!.Value);
        CompletenessPercent = ExpectedCount is > 0
            ? DetectedCount!.Value * 100d / ExpectedCount.Value
            : null;
        AttentionStationId = stations
            .Where(item => item.RequiresAttention)
            .OrderBy(item => item.CompletenessPercent ?? double.MaxValue)
            .ThenByDescending(item => item.MissingCount ?? 0)
            .ThenByDescending(item => item.UncouplingAlarmCount)
            .ThenByDescending(item => item.PassageCount)
            .ThenBy(item => item.StationId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.StationId, StringComparer.Ordinal)
            .Select(item => item.StationId)
            .FirstOrDefault();
    }

    public int StationCount { get; }

    public long PassageCount { get; }

    public long? ExpectedCount { get; }

    public long? DetectedCount { get; }

    public long? MissingCount { get; }

    public double? CompletenessPercent { get; }

    public string? AttentionStationId { get; }

    public IReadOnlyList<StationQualityStatisticsItem> Stations { get; }

    public IReadOnlyList<StationTagStatisticsItem> Tags { get; }
}
