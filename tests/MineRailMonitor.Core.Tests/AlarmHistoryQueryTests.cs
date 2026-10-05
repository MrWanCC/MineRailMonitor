using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class AlarmHistoryQueryTests
{
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(8);

    [Fact]
    public void One_alarm_today_is_returned_in_the_current_query_and_list()
    {
        var today = new DateTime(2026, 10, 5);
        var store = CreateStore(
            CreateAlarm(new DateTimeOffset(today.AddHours(10), LocalOffset)));

        var result = store.Query(CreateAlarmQuery(today));

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
    }

    [Fact]
    public void Querying_yesterday_counts_only_yesterdays_alarms()
    {
        var today = new DateTime(2026, 10, 5);
        var store = CreateStore(
            CreateAlarm(new DateTimeOffset(today.AddDays(-1).AddHours(23), LocalOffset)),
            CreateAlarm(new DateTimeOffset(today.AddHours(1), LocalOffset)));

        var result = store.Query(CreateAlarmQuery(today.AddDays(-1)));

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(today.AddDays(-1), result.Items[0].CompletedAt.Date);
    }

    [Fact]
    public void Querying_a_date_range_without_alarms_returns_zero_rows()
    {
        var today = new DateTime(2026, 10, 5);
        var store = CreateStore(
            CreateAlarm(new DateTimeOffset(today.AddHours(10), LocalOffset)));

        var result = store.Query(CreateAlarmQuery(today.AddDays(-2)));

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    private static InMemoryPassageRecordStore CreateStore(params PassageRecord[] records)
    {
        var store = new InMemoryPassageRecordStore();
        foreach (var record in records)
        {
            store.Add(record);
        }

        return store;
    }

    private static PassageQuery CreateAlarmQuery(DateTime date)
    {
        var start = new DateTimeOffset(date.Date, LocalOffset);
        return new PassageQuery
        {
            From = start,
            To = start.AddDays(1),
            IncludeWarnings = true,
            PageIndex = 0,
            PageSize = 20
        };
    }

    private static PassageRecord CreateAlarm(DateTimeOffset completedAt) => new(
        Guid.NewGuid(),
        "S1",
        1,
        0xA001,
        new ushort[] { 0xA001 },
        11,
        PassageOutcome.UncouplingAlarm,
        completedAt.AddMinutes(-1),
        completedAt,
        alarmMessage: "脱节报警");
}
