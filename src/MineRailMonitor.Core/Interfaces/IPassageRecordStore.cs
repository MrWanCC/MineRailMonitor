using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Interfaces;

public interface IPassageRecordStore
{
    IReadOnlyList<PassageRecord> Records { get; }

    void Save(PassageRecord record);

    void Add(PassageRecord record);

    void MarkCleared(Guid passageId, DateTimeOffset clearedAt);

    void MarkAlarmAcknowledged(Guid passageId, DateTimeOffset acknowledgedAt);

    IReadOnlyList<PassageRecord> GetPendingClear();

    IReadOnlyList<PassageRecord> GetUnacknowledgedAlarms();

    PassageQueryResult Query(PassageQuery query);

    PassageRecord? GetDetails(Guid passageId);

    PassageStatistics GetStatistics(DateTimeOffset localNow);
}
