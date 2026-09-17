using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public sealed class InMemoryPassageRecordStore : IPassageRecordStore
{
    private readonly object _syncRoot = new();
    private readonly List<PassageRecord> _records = new();

    public IReadOnlyList<PassageRecord> Records
    {
        get
        {
            lock (_syncRoot)
            {
                return _records.ToArray();
            }
        }
    }

    public void Save(PassageRecord record)
    {
        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        lock (_syncRoot)
        {
            if (_records.Any(existing => existing.PassageId == record.PassageId))
            {
                return;
            }

            _records.Add(record);
        }
    }

    public void Add(PassageRecord record) => Save(record);

    public void MarkCleared(Guid passageId, DateTimeOffset clearedAt)
    {
        lock (_syncRoot)
        {
            var index = _records.FindIndex(record => record.PassageId == passageId);
            if (index < 0)
            {
                throw new InvalidOperationException($"PassageRecord not found: {passageId}");
            }

            var record = _records[index];
            if (record.ClearState == PassageClearState.Cleared)
            {
                return;
            }

            _records[index] = CopyRecord(record, PassageClearState.Cleared, clearedAt);
        }
    }

    public IReadOnlyList<PassageRecord> GetPendingClear()
    {
        lock (_syncRoot)
        {
            return _records.Where(record => record.ClearState == PassageClearState.PendingClear).ToArray();
        }
    }

    public PassageQueryResult Query(PassageQuery query)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        query.Validate();

        lock (_syncRoot)
        {
            var filtered = ApplyFilters(_records, query)
                .OrderByDescending(record => record.CompletedAt)
                .ThenByDescending(record => record.CreatedAt)
                .ToArray();
            return new PassageQueryResult(
                filtered.Skip(query.PageIndex * query.PageSize).Take(query.PageSize).ToArray(),
                filtered.Length,
                query.PageIndex,
                query.PageSize);
        }
    }

    public PassageRecord? GetDetails(Guid passageId)
    {
        lock (_syncRoot)
        {
            return _records.FirstOrDefault(record => record.PassageId == passageId);
        }
    }

    public PassageStatistics GetStatistics(DateTimeOffset localNow)
    {
        var localDate = localNow.Date;
        var dayStart = new DateTimeOffset(localDate, localNow.Offset);
        var dayEnd = dayStart.AddDays(1);
        lock (_syncRoot)
        {
            var today = _records.Where(record => record.CompletedAt >= dayStart && record.CompletedAt < dayEnd).ToArray();
            return new PassageStatistics(
                today.Length,
                today.Count(record => record.Outcome == PassageOutcome.Completed),
                today.Count(record => record.Outcome == PassageOutcome.UncouplingAlarm),
                today
                    .GroupBy(record => NormalizeStationId(record.StationId), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase));
        }
    }

    private static IEnumerable<PassageRecord> ApplyFilters(IEnumerable<PassageRecord> records, PassageQuery query)
    {
        var filtered = records;
        if (query.From.HasValue) filtered = filtered.Where(record => record.CompletedAt >= query.From.Value);
        if (query.To.HasValue) filtered = filtered.Where(record => record.CompletedAt < query.To.Value);
        if (query.StationIds is not null)
        {
            var stationIds = new HashSet<string>(
                query.StationIds
                    .Where(stationId => !string.IsNullOrWhiteSpace(stationId))
                    .Select(NormalizeStationId),
                StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(record => stationIds.Contains(NormalizeStationId(record.StationId)));
        }
        if (!string.IsNullOrWhiteSpace(query.StationId))
        {
            var stationId = NormalizeStationId(query.StationId);
            filtered = filtered.Where(record => string.Equals(NormalizeStationId(record.StationId), stationId, StringComparison.OrdinalIgnoreCase));
        }
        else if (query.StationAddress.HasValue)
        {
            filtered = filtered.Where(record => record.StationAddress == query.StationAddress.Value);
        }
        if (query.HeadRfid.HasValue) filtered = filtered.Where(record => record.HeadRfid == query.HeadRfid.Value);
        if (query.Outcome.HasValue) filtered = filtered.Where(record => record.Outcome == query.Outcome.Value);
        if (query.IncludeWarnings)
        {
            filtered = filtered.Where(record => record.IsAlert);
        }
        return filtered;
    }

    private static string NormalizeStationId(string? stationId) =>
        string.IsNullOrWhiteSpace(stationId) ? PassageRecord.LegacyStationId : stationId!.Trim();

    private static PassageRecord CopyRecord(PassageRecord record, PassageClearState clearState, DateTimeOffset? clearedAt) =>
        new(
            record.PassageId,
            record.StationId,
            record.StationAddress,
            record.HeadRfid,
            record.ObservedRfids,
            record.ExpectedVehicleCount,
            record.Outcome,
            record.StartedAt,
            record.CompletedAt,
            record.WarningMessages,
            record.AlarmMessage,
            record.RfidObservations,
            clearState,
            clearedAt,
            record.CreatedAt);
}
