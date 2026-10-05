using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class PassageOverviewQueryServiceTests
{
    [Fact]
    public void Query_returns_recent_today_passages_and_alarm_status_counts_in_scope()
    {
        var store = new InMemoryPassageRecordStore();
        var today = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.FromHours(8));
        store.Save(CreateRecord("S1", today.AddHours(8), PassageOutcome.Completed, vehicleNo: "K001", vehicleName: "矿车A"));
        store.Save(CreateRecord("S1", today.AddHours(9), PassageOutcome.UncouplingAlarm));
        store.Save(CreateRecord("S1", today.AddHours(10), PassageOutcome.UncouplingAlarm, alarmAcknowledgedAt: today.AddHours(10).AddMinutes(1)));
        store.Save(CreateRecord(
            "S1",
            today.AddHours(11),
            PassageOutcome.UncouplingAlarm,
            alarmAcknowledgedAt: today.AddHours(11).AddMinutes(1),
            alarmRecoveredAt: today.AddHours(11).AddMinutes(2)));
        store.Save(CreateRecord("S2", today.AddHours(12), PassageOutcome.UncouplingAlarm));
        store.Save(CreateRecord("S1", today.AddDays(-1), PassageOutcome.UncouplingAlarm));

        var result = new PassageOverviewQueryService(store).QueryToday(
            today,
            today.AddDays(1),
            new[] { "S1" });

        Assert.Equal(3, result.AlarmCount);
        Assert.Equal(1, result.UnconfirmedAlarmCount);
        Assert.Equal(1, result.AcknowledgedAlarmCount);
        Assert.Equal(1, result.RecoveredAlarmCount);
        Assert.Equal(4, result.RecentPassages.Count);
        Assert.Equal(today.AddHours(11), result.RecentPassages[0].CompletedAt);
        Assert.Equal("K001", result.RecentPassages[3].VehicleNo);
    }

    [Fact]
    public void Query_with_empty_station_scope_returns_empty_values()
    {
        var store = new InMemoryPassageRecordStore();
        var today = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.FromHours(8));
        store.Save(CreateRecord("S1", today.AddHours(8), PassageOutcome.UncouplingAlarm));

        var result = new PassageOverviewQueryService(store).QueryToday(
            today,
            today.AddDays(1),
            Array.Empty<string>());

        Assert.Empty(result.RecentPassages);
        Assert.Equal(0, result.AlarmCount);
        Assert.Equal(0, result.UnconfirmedAlarmCount);
        Assert.Equal(0, result.AcknowledgedAlarmCount);
        Assert.Equal(0, result.RecoveredAlarmCount);
    }

    [Fact]
    public void Query_counts_alarm_states_across_all_pages_and_limits_recent_passages()
    {
        var store = new InMemoryPassageRecordStore();
        var today = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.FromHours(8));
        for (var index = 0; index < 105; index++)
        {
            var completedAt = today.AddMinutes(index);
            var state = index % 3;
            store.Save(CreateRecord(
                "S1",
                completedAt,
                PassageOutcome.UncouplingAlarm,
                alarmAcknowledgedAt: state == 0 ? null : completedAt.AddSeconds(1),
                alarmRecoveredAt: state == 2 ? completedAt.AddSeconds(2) : null));
        }

        var result = new PassageOverviewQueryService(store).QueryToday(today, today.AddDays(1), null);

        Assert.Equal(105, result.AlarmCount);
        Assert.Equal(35, result.UnconfirmedAlarmCount);
        Assert.Equal(35, result.AcknowledgedAlarmCount);
        Assert.Equal(35, result.RecoveredAlarmCount);
        Assert.Equal(10, result.RecentPassages.Count);
    }

    private static PassageRecord CreateRecord(
        string stationId,
        DateTimeOffset completedAt,
        PassageOutcome outcome,
        string? vehicleNo = null,
        string? vehicleName = null,
        DateTimeOffset? alarmAcknowledgedAt = null,
        DateTimeOffset? alarmRecoveredAt = null)
    {
        var startedAt = completedAt.AddSeconds(-12);
        return new PassageRecord(
            Guid.NewGuid(),
            stationId,
            1,
            1,
            new ushort[] { 1, 17, 18 },
            3,
            outcome,
            startedAt,
            completedAt,
            alarmMessage: outcome == PassageOutcome.UncouplingAlarm ? "脱节报警" : null,
            alarmAcknowledgedAt: alarmAcknowledgedAt,
            alarmRecoveredAt: alarmRecoveredAt,
            vehicleName: vehicleName,
            vehicleNo: vehicleNo);
    }
}
