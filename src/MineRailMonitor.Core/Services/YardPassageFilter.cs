using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public static class YardPassageFilter
{
    public static IReadOnlyList<PassageRecord> Filter(
        IEnumerable<PassageRecord> records,
        IEnumerable<string>? stationIds)
    {
        if (records is null) throw new ArgumentNullException(nameof(records));

        var source = records.Where(record => record is not null);
        if (stationIds is null)
        {
            return source.ToArray();
        }

        var allowedIds = new HashSet<string>(
            stationIds
                .Where(stationId => !string.IsNullOrWhiteSpace(stationId))
                .Select(stationId => stationId.Trim()),
            StringComparer.OrdinalIgnoreCase);
        return source
            .Where(record => allowedIds.Contains(record.StationId.Trim()))
            .ToArray();
    }

    public static PassageStatistics BuildStatistics(
        IEnumerable<PassageRecord> records,
        DateTimeOffset localNow,
        IEnumerable<string>? stationIds)
    {
        var dayStart = new DateTimeOffset(localNow.Date, localNow.Offset);
        var dayEnd = dayStart.AddDays(1);
        var today = Filter(records, stationIds)
            .Where(record => record.CompletedAt >= dayStart && record.CompletedAt < dayEnd)
            .ToArray();

        return new PassageStatistics(
            today.Length,
            today.Count(record => record.Outcome == PassageOutcome.Completed),
            today.Count(record => record.Outcome == PassageOutcome.UncouplingAlarm),
            today
                .GroupBy(record => NormalizeStationId(record.StationId), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeStationId(string? stationId) =>
        string.IsNullOrWhiteSpace(stationId) ? PassageRecord.LegacyStationId : stationId!.Trim();
}
