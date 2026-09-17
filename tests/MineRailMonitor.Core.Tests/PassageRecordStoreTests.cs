using System.Net;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class PassageRecordStoreTests
{
    private static readonly DateTimeOffset Today = new(2026, 9, 9, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Save_is_idempotent_and_mark_cleared_preserves_rfid_details()
    {
        var store = new InMemoryPassageRecordStore();
        var record = CreateRecord(Guid.Parse("11111111-1111-1111-1111-111111111111"), 0x01, Today);

        store.Save(record);
        store.Save(record);

        Assert.Single(store.Records);
        Assert.Equal(2, store.GetDetails(record.PassageId)!.RfidObservations.Count);
        Assert.Equal(PassageClearState.PendingClear, store.Records[0].ClearState);

        var clearedAt = Today.AddMinutes(1);
        store.MarkCleared(record.PassageId, clearedAt);

        var cleared = Assert.Single(store.Records);
        Assert.Equal(PassageClearState.Cleared, cleared.ClearState);
        Assert.Equal(clearedAt, cleared.ClearedAt);
        Assert.Equal(2, cleared.RfidObservations.Count);
    }

    [Fact]
    public void Pending_clear_returns_only_uncleared_passages()
    {
        var store = new InMemoryPassageRecordStore();
        var pending = CreateRecord(Guid.Parse("22222222-2222-2222-2222-222222222222"), 0x01, Today);
        var cleared = CreateRecord(Guid.Parse("33333333-3333-3333-3333-333333333333"), 0x02, Today);
        store.Save(pending);
        store.Save(cleared);
        store.MarkCleared(cleared.PassageId, Today.AddMinutes(1));

        var result = store.GetPendingClear();

        var item = Assert.Single(result);
        Assert.Equal(pending.PassageId, item.PassageId);
    }

    [Fact]
    public void Query_filters_sorts_and_pages_records_while_returning_total_count()
    {
        var store = new InMemoryPassageRecordStore();
        store.Save(CreateRecord(Guid.Parse("44444444-4444-4444-4444-444444444444"), 0x01, Today.AddMinutes(-3)));
        store.Save(CreateRecord(Guid.Parse("55555555-5555-5555-5555-555555555555"), 0x01, Today.AddMinutes(-2)));
        store.Save(CreateRecord(Guid.Parse("66666666-6666-6666-6666-666666666666"), 0x02, Today.AddMinutes(-1), PassageOutcome.UncouplingAlarm));

        var result = store.Query(new PassageQuery
        {
            From = Today.AddHours(-1),
            To = Today.AddHours(1),
            StationAddress = 0x01,
            Outcome = PassageOutcome.Completed,
            PageIndex = 1,
            PageSize = 1
        });

        Assert.Equal(2, result.TotalCount);
        var item = Assert.Single(result.Items);
        Assert.Equal(Guid.Parse("44444444-4444-4444-4444-444444444444"), item.PassageId);
        Assert.Equal(1, result.PageIndex);
        Assert.Equal(1, result.PageSize);
    }

    [Fact]
    public void Statistics_use_passage_count_and_group_by_station_for_the_requested_day()
    {
        var store = new InMemoryPassageRecordStore();
        store.Save(CreateRecord(Guid.Parse("77777777-7777-7777-7777-777777777777"), 0x01, Today.AddMinutes(-2)));
        store.Save(CreateRecord(Guid.Parse("88888888-8888-8888-8888-888888888888"), 0x01, Today.AddMinutes(-1), PassageOutcome.UncouplingAlarm));
        store.Save(CreateRecord(Guid.Parse("99999999-9999-9999-9999-999999999999"), 0x02, Today.AddDays(-1)));

        var statistics = store.GetStatistics(Today);

        Assert.Equal(2, statistics.TodayPassageCount);
        Assert.Equal(1, statistics.TodayNormalCount);
        Assert.Equal(1, statistics.TodayAlarmCount);
        Assert.Equal(2, statistics.ByStation["RFID-01"]);
        Assert.DoesNotContain("RFID-02", statistics.ByStation.Keys);
    }

    [Fact]
    public void StationId_is_the_query_and_statistics_identity_when_protocol_addresses_repeat()
    {
        var store = new InMemoryPassageRecordStore();
        store.Save(CreateRecordWithStation(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "RFID-01",
            0x01,
            Today.AddMinutes(-2)));
        store.Save(CreateRecordWithStation(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "RFID-02",
            0x01,
            Today.AddMinutes(-1)));

        var firstStation = store.Query(new PassageQuery
        {
            StationId = "RFID-01",
            PageSize = 20
        });
        var secondStation = store.Query(new PassageQuery
        {
            StationId = "RFID-02",
            PageSize = 20
        });
        var statistics = store.GetStatistics(Today);

        Assert.Equal("RFID-01", Assert.Single(firstStation.Items).StationId);
        Assert.Equal("RFID-02", Assert.Single(secondStation.Items).StationId);
        Assert.Equal(1, statistics.ByStation["RFID-01"]);
        Assert.Equal(1, statistics.ByStation["RFID-02"]);
    }

    [Fact]
    public void Query_can_filter_multiple_station_ids_for_a_yard_scope()
    {
        var store = new InMemoryPassageRecordStore();
        store.Save(CreateRecordWithStation(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            "RFID-01",
            0x01,
            Today.AddMinutes(-3)));
        store.Save(CreateRecordWithStation(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "RFID-02",
            0x02,
            Today.AddMinutes(-2)));
        store.Save(CreateRecordWithStation(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            "RFID-03",
            0x03,
            Today.AddMinutes(-1)));

        var result = store.Query(new PassageQuery
        {
            StationIds = new[] { "RFID-01", "RFID-03" },
            PageSize = 20
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new[] { "RFID-03", "RFID-01" }, result.Items.Select(record => record.StationId));
    }

    [Fact]
    public void Query_can_include_warning_records_without_including_normal_passages()
    {
        var store = new InMemoryPassageRecordStore();
        var normal = CreateRecordWithStation(
            Guid.Parse("b1111111-1111-1111-1111-111111111111"),
            "RFID-01",
            0x01,
            Today.AddMinutes(-3));
        var warning = CreateRecordWithStation(
            Guid.Parse("b2222222-2222-2222-2222-222222222222"),
            "RFID-01",
            0x01,
            Today.AddMinutes(-2),
            warningMessages: new[] { "识别不完整：已识别10/11，缺少1个RFID" });
        var alarm = CreateRecord(
            Guid.Parse("b3333333-3333-3333-3333-333333333333"),
            0x01,
            Today.AddMinutes(-1),
            PassageOutcome.UncouplingAlarm);

        store.Save(normal);
        store.Save(warning);
        store.Save(alarm);

        var result = store.Query(new PassageQuery { IncludeWarnings = true, PageSize = 20 });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new[] { alarm.PassageId, warning.PassageId }, result.Items.Select(record => record.PassageId));
    }

    private static PassageRecord CreateRecord(
        Guid passageId,
        byte stationAddress,
        DateTimeOffset completedAt,
        PassageOutcome outcome = PassageOutcome.Completed)
    {
        var startedAt = completedAt.AddSeconds(-10);
        var details = new[]
        {
            new PassageRfidObservation(1, 0x0003, startedAt, 1),
            new PassageRfidObservation(2, 0x001D, completedAt, 2)
        };
        return new PassageRecord(
            passageId,
            $"RFID-{stationAddress:X2}",
            stationAddress,
            0x0003,
            details.Select(item => item.RfidValue),
            11,
            outcome,
            startedAt,
            completedAt,
            outcome == PassageOutcome.Completed ? Array.Empty<string>() : new[] { "脱节报警" },
            outcome == PassageOutcome.Completed ? null : "脱节报警",
            details);
    }

    private static PassageRecord CreateRecordWithStation(
        Guid passageId,
        string stationId,
        byte stationAddress,
        DateTimeOffset completedAt,
        IEnumerable<string>? warningMessages = null)
    {
        var startedAt = completedAt.AddSeconds(-10);
        var details = new[]
        {
            new PassageRfidObservation(1, 0x0003, startedAt, 1)
        };
        return new PassageRecord(
            passageId,
            stationId,
            stationAddress,
            0x0003,
            details.Select(item => item.RfidValue),
            11,
            PassageOutcome.Completed,
            startedAt,
            completedAt,
            warningMessages: warningMessages,
            rfidObservations: details);
    }
}
