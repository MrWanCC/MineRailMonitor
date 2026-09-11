using System.Collections;
using System.Reflection;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidTrendAggregationTests
{
    [Fact]
    public void Trend_uses_hour_buckets_today_and_date_buckets_for_longer_ranges()
    {
        var aggregatorType = Type.GetType("MineRailMonitor.Core.Statistics.RfidTrendAggregator, MineRailMonitor.Core");
        Assert.NotNull(aggregatorType);

        var build = aggregatorType!.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(build);

        var localNow = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.FromHours(8));
        var today = Invoke(build!, localNow, 1);
        var last7Days = Invoke(build, localNow, 7);
        var last30Days = Invoke(build, localNow, 30);

        Assert.Equal(24, today.Count);
        Assert.Equal("00:00", GetLabel(today[0]));
        Assert.Equal("23:00", GetLabel(today[today.Count - 1]));

        Assert.Equal(7, last7Days.Count);
        Assert.Equal("09-05", GetLabel(last7Days[0]));
        Assert.Equal("09-11", GetLabel(last7Days[last7Days.Count - 1]));

        Assert.Equal(30, last30Days.Count);
        Assert.Equal("08-13", GetLabel(last30Days[0]));
        Assert.Equal("09-11", GetLabel(last30Days[last30Days.Count - 1]));
    }

    private static IReadOnlyList<object> Invoke(MethodInfo build, DateTimeOffset localNow, int rangeDays)
    {
        var result = build.Invoke(null, new object[] { Array.Empty<PassageRecord>(), localNow, rangeDays });
        Assert.NotNull(result);
        return ((IEnumerable)result!).Cast<object>().ToArray();
    }

    private static string GetLabel(object point)
    {
        var property = point.GetType().GetProperty("Label");
        Assert.NotNull(property);
        return (string)property!.GetValue(point)!;
    }
}
