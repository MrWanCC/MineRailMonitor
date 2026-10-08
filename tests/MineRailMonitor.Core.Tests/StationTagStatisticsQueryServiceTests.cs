using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class StationTagStatisticsQueryServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 0, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Query_uses_the_aggregate_reader_when_available()
    {
        var store = new InMemoryPassageRecordStore();
        var expected = new StationTagStatisticsQueryResult(
            new[] { new StationQualityStatisticsItem("S560", 1, 1, 1, 0, 100, 0, 1, 1) },
            new[] { new StationTagStatisticsItem("S560", 0xA001, 1, At(8), At(8)) });
        var reader = new RecordingStatisticsReader(expected);

        var result = new StationTagStatisticsQueryService(store, reader)
            .Query(Start, Start.AddDays(1), new[] { " S560 ", "S560" });

        Assert.Same(expected, result);
        Assert.Equal(Start, reader.Start);
        Assert.Equal(Start.AddDays(1), reader.End);
        Assert.Equal(new[] { "S560" }, reader.StationIds);
    }

    [Fact]
    public void Query_counts_each_tag_once_per_passage_and_respects_station_and_time_scope()
    {
        var store = new InMemoryPassageRecordStore();
        store.Save(CreateRecord("S560", At(9), new ushort[] { 0xA001, 0xA002, 0xA001 },
            At(8, 1), expectedCount: 3));
        store.Save(CreateRecord("S560", At(10), new ushort[] { 0xA001 }, At(9, 40), expectedCount: 1));
        store.Save(CreateRecord("S620", At(11), new ushort[] { 0xA001 }, At(10, 30), expectedCount: 1));
        store.Save(CreateRecord("S560", AtPreviousDay(23, 59), new ushort[] { 0xA003 }, AtPreviousDay(23, 58), expectedCount: 1));

        var result = new StationTagStatisticsQueryService(store).Query(Start, Start.AddDays(1), new[] { "S560" });

        Assert.Collection(
            result.Tags,
            item =>
            {
                Assert.Equal("S560", item.StationId);
                Assert.Equal((ushort)0xA001, item.RfidValue);
                Assert.Equal(2, item.PassageCount);
                Assert.Equal(At(8, 1), item.FirstSeenAt);
                Assert.Equal(At(9, 40), item.LastSeenAt);
            },
            item =>
            {
                Assert.Equal("S560", item.StationId);
                Assert.Equal((ushort)0xA002, item.RfidValue);
                Assert.Equal(1, item.PassageCount);
                Assert.Equal(At(8, 1).AddSeconds(1), item.FirstSeenAt);
                Assert.Equal(item.FirstSeenAt, item.LastSeenAt);
            });
        var station = Assert.Single(result.Stations);
        Assert.Equal(2, station.PassageCount);
        Assert.Equal(2, station.TagKinds);
        Assert.Equal(3, station.TagOccurrences);
    }

    [Fact]
    public void Query_aggregates_quality_totals_and_excludes_unverified_passages()
    {
        var store = new InMemoryPassageRecordStore();
        store.Save(CreateRecord("S560", At(9), new ushort[] { 1, 2, 3, 4, 5, 6 }, At(8), expectedCount: 11,
            outcome: PassageOutcome.UncouplingAlarm));
        store.Save(CreateRecord("S560", At(10), new ushort[] { 1, 2, 3, 4, 5, 6, 7 }, At(9), expectedCount: 7));
        store.Save(CreateRecord("S620", At(11), new ushort[] { 1, 2, 3, 4 }, At(10), expectedCount: 14,
            outcome: PassageOutcome.Unverified));

        var result = new StationTagStatisticsQueryService(store).Query(Start, Start.AddDays(1));

        Assert.Equal(2, result.StationCount);
        Assert.Equal(3, result.PassageCount);
        Assert.Equal(18, result.ExpectedCount);
        Assert.Equal(13, result.DetectedCount);
        Assert.Equal(5, result.MissingCount);
        Assert.Equal(13d / 18d * 100d, result.CompletenessPercent!.Value, 5);
        var s560 = result.Stations.Single(item => item.StationId == "S560");
        Assert.Equal(2, s560.PassageCount);
        Assert.Equal(18, s560.ExpectedCount);
        Assert.Equal(13, s560.DetectedCount);
        Assert.Equal(5, s560.MissingCount);
        Assert.Equal(13d / 18d * 100d, s560.CompletenessPercent!.Value, 5);
        Assert.Equal(1, s560.UncouplingAlarmCount);
        var s620 = result.Stations.Single(item => item.StationId == "S620");
        Assert.Equal(1, s620.PassageCount);
        Assert.Null(s620.ExpectedCount);
        Assert.Null(s620.DetectedCount);
        Assert.Null(s620.MissingCount);
        Assert.Null(s620.CompletenessPercent);
        Assert.Equal("S560", result.AttentionStationId);
    }

    [Fact]
    public void Query_uses_distinct_observed_rfids_and_clamps_missing_count_at_zero()
    {
        var store = new InMemoryPassageRecordStore();
        store.Save(CreateRecord("S560", At(9), new ushort[] { 1, 1, 2, 3 }, At(8), expectedCount: 2));

        var result = new StationTagStatisticsQueryService(store).Query(Start, Start.AddDays(1));

        Assert.Equal(3, result.DetectedCount);
        Assert.Equal(0, result.MissingCount);
        Assert.Equal(3, result.Tags.Count);
        Assert.All(result.Tags, item => Assert.Equal(1, item.PassageCount));
        Assert.Equal(3, Assert.Single(result.Stations).TagOccurrences);
    }

    [Fact]
    public void Query_selects_lowest_completeness_then_more_missing_then_more_passages_then_station_id()
    {
        var store = new InMemoryPassageRecordStore();
        store.Save(CreateRecord("S-A", At(9), Enumerable.Range(1, 8).Select(value => (ushort)value).ToArray(), At(8), expectedCount: 10));
        store.Save(CreateRecord("S-B", At(10), Enumerable.Range(1, 7).Select(value => (ushort)value).ToArray(), At(9), expectedCount: 10));
        store.Save(CreateRecord("S-C", At(11), Enumerable.Range(1, 9).Select(value => (ushort)value).ToArray(), At(10), expectedCount: 14));

        var result = new StationTagStatisticsQueryService(store).Query(Start, Start.AddDays(1));

        Assert.Equal("S-C", result.AttentionStationId);
    }

    [Fact]
    public void Query_prefers_more_passage_batches_when_completeness_and_missing_are_equal()
    {
        var store = new InMemoryPassageRecordStore();
        store.Save(CreateRecord("S-A", At(9), Enumerable.Range(1, 7).Select(value => (ushort)value).ToArray(), At(8), expectedCount: 10));
        store.Save(CreateRecord("S-Z", At(10), Enumerable.Range(1, 7).Select(value => (ushort)value).ToArray(), At(9), expectedCount: 10));
        store.Save(CreateRecord("S-Z", At(11), new ushort[] { 99 }, At(10), expectedCount: null, outcome: PassageOutcome.Unverified));

        var result = new StationTagStatisticsQueryService(store).Query(Start, Start.AddDays(1));

        Assert.Equal("S-Z", result.AttentionStationId);
    }

    [Fact]
    public void Query_uses_stable_station_id_order_when_attention_candidates_are_equal()
    {
        var store = new InMemoryPassageRecordStore();
        foreach (var stationId in new[] { "Z-02", "A-02" })
        {
            store.Save(CreateRecord(stationId, At(9), Enumerable.Range(1, 7).Select(value => (ushort)value).ToArray(), At(8), expectedCount: 10));
        }

        var result = new StationTagStatisticsQueryService(store).Query(Start, Start.AddDays(1));

        Assert.Equal("A-02", result.AttentionStationId);
    }

    [Fact]
    public void Query_returns_null_quality_values_when_no_passage_is_verifiable()
    {
        var store = new InMemoryPassageRecordStore();
        store.Save(CreateRecord("S560", At(9), new ushort[] { 1, 2 }, At(8), expectedCount: 11,
            outcome: PassageOutcome.Unverified));

        var result = new StationTagStatisticsQueryService(store).Query(Start, Start.AddDays(1));

        Assert.Equal(1, result.PassageCount);
        Assert.Null(result.ExpectedCount);
        Assert.Null(result.DetectedCount);
        Assert.Null(result.MissingCount);
        Assert.Null(result.CompletenessPercent);
        Assert.Null(result.AttentionStationId);
    }

    [Fact]
    public void Query_returns_empty_result_for_an_empty_station_scope()
    {
        var store = new InMemoryPassageRecordStore();
        store.Save(CreateRecord("S560", At(9), new ushort[] { 0xA001 }, At(8), expectedCount: 1));

        var result = new StationTagStatisticsQueryService(store).Query(Start, Start.AddDays(1), Array.Empty<string>());

        Assert.Empty(result.Tags);
        Assert.Empty(result.Stations);
        Assert.Equal(0, result.StationCount);
        Assert.Equal(0, result.PassageCount);
        Assert.Null(result.ExpectedCount);
    }

    [Fact]
    public void Query_aggregates_tags_across_all_result_pages()
    {
        var store = new InMemoryPassageRecordStore();
        for (var index = 0; index < 201; index++)
        {
            var firstSeenAt = Start.AddMinutes(index);
            store.Save(CreateRecord("S560", firstSeenAt.AddMinutes(1), new ushort[] { 0xA001 }, firstSeenAt, expectedCount: 1));
        }

        var result = new StationTagStatisticsQueryService(store).Query(Start, Start.AddDays(1));

        var item = Assert.Single(result.Tags);
        Assert.Equal(201, item.PassageCount);
        Assert.Equal(Start, item.FirstSeenAt);
        Assert.Equal(Start.AddMinutes(200), item.LastSeenAt);
        Assert.Equal(201, result.PassageCount);
        Assert.Equal(201, result.Stations.Single().TagOccurrences);
    }

    private static PassageRecord CreateRecord(
        string stationId,
        DateTimeOffset completedAt,
        IReadOnlyList<ushort> rfids,
        DateTimeOffset firstSeenAt,
        int? expectedCount,
        PassageOutcome outcome = PassageOutcome.Completed)
    {
        var observations = rfids
            .Select((rfid, index) => new PassageRfidObservation(index + 1, rfid, firstSeenAt.AddSeconds(index), 1))
            .ToArray();
        return new PassageRecord(
            Guid.NewGuid(), stationId, 1, rfids.FirstOrDefault(), rfids, expectedCount, outcome,
            completedAt.AddMinutes(-2), completedAt, rfidObservations: observations);
    }

    private static DateTimeOffset At(int hour, int minute = 0) => Start.AddHours(hour).AddMinutes(minute);

    private static DateTimeOffset AtPreviousDay(int hour, int minute) => Start.AddDays(-1).AddHours(hour).AddMinutes(minute);

    private sealed class RecordingStatisticsReader : IStationTagStatisticsReader
    {
        private readonly StationTagStatisticsQueryResult _result;

        public RecordingStatisticsReader(StationTagStatisticsQueryResult result)
        {
            _result = result;
        }

        public DateTimeOffset? Start { get; private set; }

        public DateTimeOffset? End { get; private set; }

        public IReadOnlyList<string>? StationIds { get; private set; }

        public StationTagStatisticsQueryResult GetStationTagStatistics(
            DateTimeOffset start,
            DateTimeOffset end,
            IEnumerable<string>? stationIds = null)
        {
            Start = start;
            End = end;
            StationIds = stationIds?.ToArray();
            return _result;
        }
    }
}
