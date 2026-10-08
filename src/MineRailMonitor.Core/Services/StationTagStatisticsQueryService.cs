using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public sealed class StationTagStatisticsQueryService
{
    private const int QueryPageSize = 200;

    private readonly IPassageRecordStore _passageRecordStore;
    private readonly IStationTagStatisticsReader? _statisticsReader;

    public StationTagStatisticsQueryService(
        IPassageRecordStore passageRecordStore,
        IStationTagStatisticsReader? statisticsReader = null)
    {
        _passageRecordStore = passageRecordStore ?? throw new ArgumentNullException(nameof(passageRecordStore));
        _statisticsReader = statisticsReader;
    }

    public StationTagStatisticsQueryResult Query(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<string>? stationIds = null)
    {
        if (start > end)
        {
            throw new ArgumentException("查询开始时间不能晚于结束时间。", nameof(start));
        }

        var scope = stationIds?
            .Where(stationId => !string.IsNullOrWhiteSpace(stationId))
            .Select(stationId => stationId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (scope is { Length: 0 } || start == end)
        {
            return EmptyResult();
        }

        if (_statisticsReader is not null)
        {
            return _statisticsReader.GetStationTagStatistics(start, end, scope);
        }

        var query = new PassageQuery
        {
            From = start,
            To = end,
            StationIds = scope,
            PageSize = QueryPageSize
        };
        var firstPage = _passageRecordStore.Query(query);
        var pageCount = (int)Math.Ceiling(firstPage.TotalCount / (double)QueryPageSize);
        var stations = new Dictionary<string, MutableStationAggregate>(StringComparer.OrdinalIgnoreCase);

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var page = pageIndex == 0
                ? firstPage
                : _passageRecordStore.Query(new PassageQuery
                {
                    From = start,
                    To = end,
                    StationIds = scope,
                    PageIndex = pageIndex,
                    PageSize = QueryPageSize
                });

            foreach (var passage in page.Items)
            {
                var stationId = passage.StationId.Trim();
                if (!stations.TryGetValue(stationId, out var station))
                {
                    station = new MutableStationAggregate(stationId);
                    stations.Add(stationId, station);
                }

                station.AddPassage(passage);
            }
        }

        var stationTagItems = stations.Values
            .SelectMany(station => station.Tags.Values)
            .Select(aggregate => aggregate.ToItem())
            .OrderBy(item => item.StationId, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.PassageCount)
            .ThenBy(item => item.RfidValue)
            .ToArray();

        var tagKindsByStation = stationTagItems
            .GroupBy(item => item.StationId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var tagOccurrencesByStation = stationTagItems
            .GroupBy(item => item.StationId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => (long)item.PassageCount), StringComparer.OrdinalIgnoreCase);
        var stationItems = stations.Values
            .Select(station => station.ToItem(
                tagKindsByStation.TryGetValue(station.StationId, out var tagKinds) ? tagKinds : 0,
                tagOccurrencesByStation.TryGetValue(station.StationId, out var tagOccurrences) ? tagOccurrences : 0))
            .OrderBy(item => item.StationId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.StationId, StringComparer.Ordinal)
            .ToArray();

        return new StationTagStatisticsQueryResult(stationItems, stationTagItems);
    }

    private static StationTagStatisticsQueryResult EmptyResult() =>
        new(Array.Empty<StationQualityStatisticsItem>(), Array.Empty<StationTagStatisticsItem>());

    private sealed class MutableStationAggregate
    {
        public MutableStationAggregate(string stationId)
        {
            StationId = stationId;
        }

        public string StationId { get; }

        public int PassageCount { get; private set; }

        public long ExpectedCount { get; private set; }

        public long DetectedCount { get; private set; }

        public long MissingCount { get; private set; }

        public int VerifiablePassageCount { get; private set; }

        public int UncouplingAlarmCount { get; private set; }

        public Dictionary<ushort, MutableTagAggregate> Tags { get; } = new();

        public void AddPassage(PassageRecord passage)
        {
            PassageCount++;
            if (passage.Outcome == PassageOutcome.UncouplingAlarm)
            {
                UncouplingAlarmCount++;
            }

            var distinctRfids = passage.ObservedRfids.Distinct().ToArray();
            if (passage.ExpectedVehicleCount.HasValue && passage.Outcome != PassageOutcome.Unverified)
            {
                VerifiablePassageCount++;
                ExpectedCount += passage.ExpectedVehicleCount.Value;
                DetectedCount += distinctRfids.Length;
                MissingCount += Math.Max(passage.ExpectedVehicleCount.Value - distinctRfids.Length, 0);
            }

            foreach (var tagObservations in passage.RfidObservations.GroupBy(observation => observation.RfidValue))
            {
                var tagFirstSeen = tagObservations.Min(observation => observation.FirstSeenAt);
                var tagLastSeen = tagObservations.Max(observation => observation.FirstSeenAt);
                if (!Tags.TryGetValue(tagObservations.Key, out var aggregate))
                {
                    aggregate = new MutableTagAggregate(StationId, tagObservations.Key, tagFirstSeen, tagLastSeen);
                    Tags.Add(tagObservations.Key, aggregate);
                }

                aggregate.AddPassage(tagFirstSeen, tagLastSeen);
            }
        }

        public StationQualityStatisticsItem ToItem(int tagKinds, long tagOccurrences)
        {
            var hasVerifiablePassages = VerifiablePassageCount > 0;
            return new StationQualityStatisticsItem(
                StationId,
                PassageCount,
                hasVerifiablePassages ? ExpectedCount : (long?)null,
                hasVerifiablePassages ? DetectedCount : (long?)null,
                hasVerifiablePassages ? MissingCount : (long?)null,
                hasVerifiablePassages && ExpectedCount > 0 ? DetectedCount * 100d / ExpectedCount : (double?)null,
                UncouplingAlarmCount,
                tagKinds,
                tagOccurrences);
        }
    }

    private sealed class MutableTagAggregate
    {
        public MutableTagAggregate(string stationId, ushort rfidValue, DateTimeOffset firstSeenAt, DateTimeOffset lastSeenAt)
        {
            StationId = stationId;
            RfidValue = rfidValue;
            FirstSeenAt = firstSeenAt;
            LastSeenAt = lastSeenAt;
        }

        public string StationId { get; }

        public ushort RfidValue { get; }

        public int PassageCount { get; private set; }

        public DateTimeOffset FirstSeenAt { get; private set; }

        public DateTimeOffset LastSeenAt { get; private set; }

        public void AddPassage(DateTimeOffset firstSeenAt, DateTimeOffset lastSeenAt)
        {
            PassageCount++;
            if (firstSeenAt < FirstSeenAt)
            {
                FirstSeenAt = firstSeenAt;
            }
            if (lastSeenAt > LastSeenAt)
            {
                LastSeenAt = lastSeenAt;
            }
        }

        public StationTagStatisticsItem ToItem() =>
            new(StationId, RfidValue, PassageCount, FirstSeenAt, LastSeenAt);
    }
}
