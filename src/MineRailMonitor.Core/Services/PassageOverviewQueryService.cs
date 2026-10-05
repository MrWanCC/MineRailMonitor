using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public sealed class PassageOverviewQueryService
{
    private const int RecentPassageLimit = 10;
    private const int AlarmQueryPageSize = 100;
    private readonly IPassageRecordStore _passageRecordStore;

    public PassageOverviewQueryService(IPassageRecordStore passageRecordStore)
    {
        _passageRecordStore = passageRecordStore ?? throw new ArgumentNullException(nameof(passageRecordStore));
    }

    public PassageOverviewQueryResult QueryToday(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<string>? stationIds)
    {
        var scope = stationIds?.ToArray();
        var recentPassages = _passageRecordStore.Query(new PassageQuery
        {
            From = from,
            To = to,
            StationIds = scope,
            PageSize = RecentPassageLimit
        }).Items;

        var alarmQuery = new PassageQuery
        {
            From = from,
            To = to,
            StationIds = scope,
            Outcome = PassageOutcome.UncouplingAlarm,
            PageSize = AlarmQueryPageSize
        };
        var firstPage = _passageRecordStore.Query(alarmQuery);
        var unconfirmed = 0;
        var acknowledged = 0;
        var recovered = 0;
        CountAlarmStates(firstPage.Items, ref unconfirmed, ref acknowledged, ref recovered);

        var pageCount = (int)Math.Ceiling(firstPage.TotalCount / (double)AlarmQueryPageSize);
        for (var pageIndex = 1; pageIndex < pageCount; pageIndex++)
        {
            alarmQuery.PageIndex = pageIndex;
            var page = _passageRecordStore.Query(alarmQuery);
            CountAlarmStates(page.Items, ref unconfirmed, ref acknowledged, ref recovered);
        }

        return new PassageOverviewQueryResult(
            recentPassages,
            firstPage.TotalCount,
            unconfirmed,
            acknowledged,
            recovered);
    }

    private static void CountAlarmStates(
        IEnumerable<PassageRecord> records,
        ref int unconfirmed,
        ref int acknowledged,
        ref int recovered)
    {
        foreach (var record in records)
        {
            switch (record.AlarmStatus)
            {
                case AlarmStatus.Unconfirmed:
                    unconfirmed++;
                    break;
                case AlarmStatus.Acknowledged:
                    acknowledged++;
                    break;
                case AlarmStatus.Recovered:
                    recovered++;
                    break;
            }
        }
    }
}
