using System.Globalization;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Statistics;

public static class RfidTrendAggregator
{
    public static IReadOnlyList<RfidTrendPoint> Build(
        IEnumerable<PassageRecord> records,
        DateTimeOffset localNow,
        int rangeDays)
    {
        if (records is null) throw new ArgumentNullException(nameof(records));
        if (rangeDays is not (1 or 7 or 30))
        {
            throw new ArgumentOutOfRangeException(nameof(rangeDays));
        }

        var source = records.ToArray();
        if (rangeDays == 1)
        {
            var today = localNow.Date;
            return Enumerable.Range(0, 24)
                .Select(hour =>
                {
                    var recordsInHour = source.Where(record =>
                    {
                        var completedAt = record.CompletedAt.ToLocalTime();
                        return completedAt.Date == today && completedAt.Hour == hour;
                    });
                    return new RfidTrendPoint(
                        $"{hour:00}:00",
                        recordsInHour.Count(),
                        recordsInHour.Count(record => record.Outcome == PassageOutcome.Completed));
                })
                .ToArray();
        }

        var startDate = localNow.Date.AddDays(-(rangeDays - 1));
        return Enumerable.Range(0, rangeDays)
            .Select(offset =>
            {
                var date = startDate.AddDays(offset);
                var recordsOnDate = source.Where(record => record.CompletedAt.ToLocalTime().Date == date);
                return new RfidTrendPoint(
                    date.ToString("MM-dd", CultureInfo.InvariantCulture),
                    recordsOnDate.Count(),
                    recordsOnDate.Count(record => record.Outcome == PassageOutcome.Completed));
            })
            .ToArray();
    }
}

public sealed class RfidTrendPoint
{
    public RfidTrendPoint(string label, int totalCount, int normalCount)
    {
        Label = label;
        TotalCount = totalCount;
        NormalCount = normalCount;
    }

    public string Label { get; }

    public int TotalCount { get; }

    public int NormalCount { get; }
}
