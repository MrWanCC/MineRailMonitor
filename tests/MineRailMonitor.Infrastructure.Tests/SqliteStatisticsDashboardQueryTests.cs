using MineRailMonitor.Core.Models;
using MineRailMonitor.Infrastructure.Persistence;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class SqliteStatisticsDashboardQueryTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 0, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset End = Start.AddDays(3);

    [Fact]
    public void Trend_groups_passages_into_local_hour_buckets_and_excludes_unverified_from_rate()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddHours(10).AddMinutes(5), PassageOutcome.Completed));
        database.Store.Save(CreateRecord(Start.AddHours(10).AddMinutes(25), PassageOutcome.UncouplingAlarm));
        database.Store.Save(CreateRecord(Start.AddHours(10).AddMinutes(35), PassageOutcome.Unverified));

        var buckets = database.Store.GetTrend(
            Start,
            Start.AddDays(1),
            StatisticsBucketSize.Hour);

        var bucket = Assert.Single(buckets);
        Assert.Equal(Start.AddHours(10), bucket.Start);
        Assert.Equal(3, bucket.TotalCount);
        Assert.Equal(1, bucket.NormalCount);
        Assert.Equal(1, bucket.AlarmCount);
        Assert.Equal(1, bucket.UnverifiedCount);
        Assert.Equal(50d, bucket.AlarmRatePercent!.Value, 5);
    }

    [Fact]
    public void Trend_groups_passages_into_local_day_buckets_and_respects_station_scope()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddHours(2), PassageOutcome.Completed, "RFID-01"));
        database.Store.Save(CreateRecord(Start.AddDays(1).AddHours(2), PassageOutcome.UncouplingAlarm, "RFID-01"));
        database.Store.Save(CreateRecord(Start.AddDays(1).AddHours(3), PassageOutcome.Completed, "RFID-02"));

        var buckets = database.Store.GetTrend(
            Start,
            End,
            StatisticsBucketSize.Day,
            new[] { "RFID-01" });

        Assert.Equal(2, buckets.Count);
        Assert.Equal(Start, buckets[0].Start);
        Assert.Equal(1, buckets[0].TotalCount);
        Assert.Equal(Start.AddDays(1), buckets[1].Start);
        Assert.Equal(1, buckets[1].AlarmCount);
    }

    [Fact]
    public void Trend_alarm_rate_is_undefined_when_bucket_has_no_verifiable_passages()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddHours(10).AddMinutes(5), PassageOutcome.Unverified));

        var bucket = Assert.Single(database.Store.GetTrend(
            Start,
            Start.AddDays(1),
            StatisticsBucketSize.Hour));

        Assert.Null(bucket.AlarmRatePercent);
    }

    [Fact]
    public void Alarm_rankings_return_top_five_with_share_of_all_alarms()
    {
        using var database = new TemporaryDatabase();
        for (var index = 1; index <= 6; index++)
        {
            for (var repeat = 0; repeat <= 6 - index; repeat++)
            {
                database.Store.Save(CreateRecord(
                    Start.AddHours(index).AddMinutes(repeat),
                    PassageOutcome.UncouplingAlarm,
                    $"RFID-{index:00}",
                    $"V{index:00}"));
            }
        }

        var stations = database.Store.GetAlarmStationRanking(Start, End);
        var vehicles = database.Store.GetAlarmVehicleRanking(Start, End);

        Assert.Equal(5, stations.Count);
        Assert.Equal("RFID-01", stations[0].Key);
        Assert.Equal(6, stations[0].Count);
        Assert.Equal(6d / 21d * 100d, stations[0].SharePercent, 5);
        Assert.Equal(5, vehicles.Count);
        Assert.Equal("V01", vehicles[0].Key);
        Assert.Equal(6, vehicles[0].Count);
    }

    [Fact]
    public void Passage_vehicle_ranking_includes_unmatched_vehicle_and_all_ended_outcomes()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddHours(1), PassageOutcome.Completed, vehicleNo: "V01"));
        database.Store.Save(CreateRecord(Start.AddHours(2), PassageOutcome.UncouplingAlarm, vehicleNo: "V01"));
        database.Store.Save(CreateRecord(Start.AddHours(3), PassageOutcome.Unverified, vehicleNo: null));

        var rankings = database.Store.GetPassageVehicleRanking(Start, End);

        Assert.Equal(2, Assert.Single(rankings, item => item.Key == "V01").Count);
        Assert.Equal(1, Assert.Single(rankings, item => item.Key is null).Count);
    }

    [Fact]
    public void Empty_station_scope_returns_no_dashboard_query_data()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddHours(1), PassageOutcome.UncouplingAlarm));

        Assert.Empty(database.Store.GetTrend(Start, End, StatisticsBucketSize.Day, Array.Empty<string>()));
        Assert.Empty(database.Store.GetAlarmStationRanking(Start, End, Array.Empty<string>()));
        Assert.Empty(database.Store.GetAlarmVehicleRanking(Start, End, Array.Empty<string>()));
        Assert.Empty(database.Store.GetPassageVehicleRanking(Start, End, Array.Empty<string>()));
        Assert.Empty(database.Store.GetRecentAlarms(Start, End, Array.Empty<string>()));
        Assert.Empty(database.Store.GetRecentExceptions(Start, End, Array.Empty<string>()));
    }

    [Fact]
    public void Station_tag_statistics_count_each_tag_once_per_passage_and_exclude_unverified_from_quality_totals()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateTagRecord(
            "S560",
            Start.AddHours(1),
            new ushort[] { 0xA001, 0xA001, 0xA002 },
            expectedCount: 3,
            outcome: PassageOutcome.UncouplingAlarm));
        database.Store.Save(CreateTagRecord(
            "S560",
            Start.AddHours(2),
            new ushort[] { 0xA001 },
            expectedCount: 1));
        database.Store.Save(CreateTagRecord(
            "S560",
            Start.AddHours(3),
            new ushort[] { 0xA003, 0xA003 },
            expectedCount: 11,
            outcome: PassageOutcome.Unverified));
        database.Store.Save(CreateTagRecord(
            "S620",
            Start.AddHours(1),
            new ushort[] { 0xB001 },
            expectedCount: 1));
        database.Store.Save(CreateTagRecord(
            "S560",
            End.AddMinutes(1),
            new ushort[] { 0xA004 },
            expectedCount: 1));

        var result = database.Store.GetStationTagStatistics(
            Start,
            End,
            new[] { "S560" });

        Assert.Equal(1, result.StationCount);
        Assert.Equal(3, result.PassageCount);
        Assert.Equal(4, result.ExpectedCount);
        Assert.Equal(3, result.DetectedCount);
        Assert.Equal(1, result.MissingCount);
        Assert.Equal(75d, result.CompletenessPercent!.Value, 5);

        var station = Assert.Single(result.Stations);
        Assert.Equal("S560", station.StationId);
        Assert.Equal(3, station.PassageCount);
        Assert.Equal(4, station.ExpectedCount);
        Assert.Equal(3, station.DetectedCount);
        Assert.Equal(1, station.MissingCount);
        Assert.Equal(1, station.UncouplingAlarmCount);
        Assert.Equal(3, station.TagKinds);
        Assert.Equal(4, station.TagOccurrences);

        Assert.Collection(
            result.Tags.OrderBy(item => item.RfidValue),
            item =>
            {
                Assert.Equal((ushort)0xA001, item.RfidValue);
                Assert.Equal(2, item.PassageCount);
            },
            item =>
            {
                Assert.Equal((ushort)0xA002, item.RfidValue);
                Assert.Equal(1, item.PassageCount);
            },
            item =>
            {
                Assert.Equal((ushort)0xA003, item.RfidValue);
                Assert.Equal(1, item.PassageCount);
            });
    }

    [Fact]
    public void Station_tag_statistics_return_empty_for_empty_station_scope_and_reject_empty_range()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateTagRecord(
            "S560",
            Start.AddHours(1),
            new ushort[] { 0xA001 },
            expectedCount: 1));

        var emptyScope = database.Store.GetStationTagStatistics(Start, End, Array.Empty<string>());

        Assert.Empty(emptyScope.Stations);
        Assert.Empty(emptyScope.Tags);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            database.Store.GetStationTagStatistics(Start, Start));
    }

    [Fact]
    public void Recent_alarm_query_returns_only_the_three_latest_alarms_in_scope()
    {
        using var database = new TemporaryDatabase();
        for (var index = 0; index < 4; index++)
        {
            database.Store.Save(CreateRecord(Start.AddHours(index + 1), PassageOutcome.UncouplingAlarm));
        }
        database.Store.Save(CreateRecord(Start.AddHours(6), PassageOutcome.UncouplingAlarm, "RFID-02"));

        var result = database.Store.GetRecentAlarms(Start, End, new[] { "RFID-01" }, 3);

        Assert.Equal(3, result.Count);
        Assert.Equal(Start.AddHours(4), result[0].CompletedAt);
        Assert.All(result, item => Assert.Equal("RFID-01", item.StationId));
    }

    [Fact]
    public void Recent_exceptions_prioritize_unconfirmed_alarms_then_show_latest_other_exceptions()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddHours(1), PassageOutcome.Unverified));
        database.Store.Save(CreateRecord(Start.AddHours(2), PassageOutcome.UncouplingAlarm, acknowledgedAt: Start.AddHours(3)));
        database.Store.Save(CreateRecord(Start.AddHours(3), PassageOutcome.UncouplingAlarm));
        database.Store.Save(CreateRecord(Start.AddHours(4), PassageOutcome.Unverified));

        var result = database.Store.GetRecentExceptions(Start, End, limit: 4);

        Assert.Equal(4, result.Count);
        Assert.Equal(PassageOutcome.UncouplingAlarm, result[0].Outcome);
        Assert.Equal(AlarmStatus.Unconfirmed, result[0].AlarmStatus);
        Assert.Equal(Start.AddHours(4), result[1].CompletedAt);
        Assert.Equal(Start.AddHours(2), result[2].CompletedAt);
        Assert.Equal(Start.AddHours(1), result[3].CompletedAt);
    }

    private static PassageRecord CreateRecord(
        DateTimeOffset completedAt,
        PassageOutcome outcome,
        string stationId = "RFID-01",
        string? vehicleNo = "V01",
        DateTimeOffset? acknowledgedAt = null)
    {
        return new PassageRecord(
            Guid.NewGuid(),
            stationId,
            1,
            1,
            new ushort[] { 1, 17 },
            2,
            outcome,
            completedAt.AddSeconds(-5),
            completedAt,
            outcome == PassageOutcome.UncouplingAlarm ? new[] { "脱节报警" } : Array.Empty<string>(),
            outcome == PassageOutcome.UncouplingAlarm ? "脱节报警" : null,
            alarmAcknowledgedAt: acknowledgedAt,
            vehicleName: vehicleNo is null ? null : "测试车辆",
            vehicleHeadRfid: vehicleNo is null ? null : (ushort?)1,
            vehicleNo: vehicleNo);
    }

    private static PassageRecord CreateTagRecord(
        string stationId,
        DateTimeOffset completedAt,
        IReadOnlyList<ushort> rfids,
        int? expectedCount,
        PassageOutcome outcome = PassageOutcome.Completed)
    {
        var observations = rfids
            .Select((rfid, index) => new PassageRfidObservation(
                index + 1,
                rfid,
                completedAt.AddSeconds(-rfids.Count + index),
                1))
            .ToArray();
        return new PassageRecord(
            Guid.NewGuid(),
            stationId,
            1,
            rfids.FirstOrDefault(),
            rfids,
            expectedCount,
            outcome,
            completedAt.AddMinutes(-1),
            completedAt,
            rfidObservations: observations);
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));

        public TemporaryDatabase()
        {
            Directory.CreateDirectory(_directory);
            Store = new SqlitePassageRecordStore(Path.Combine(_directory, "passages.db"));
        }

        public SqlitePassageRecordStore Store { get; }

        public void Dispose()
        {
            Store.Dispose();
            foreach (var file in Directory.EnumerateFiles(_directory))
            {
                File.Delete(file);
            }

            Directory.Delete(_directory);
        }
    }
}
