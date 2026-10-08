using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class StatisticsComparisonRangeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 14, 35, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Today_uses_the_selected_local_day_and_the_previous_full_day()
    {
        var range = StatisticsComparisonRange.ForPreset(StatisticsPeriod.Today, Now);

        Assert.Equal(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(8)), range.Current.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.FromHours(8)), range.Current.End);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.FromHours(8)), range.Previous.Start);
        Assert.Equal(range.Current.Start, range.Previous.End);
        Assert.Equal("今日", range.Label);
    }

    [Fact]
    public void Yesterday_compares_with_the_day_before_yesterday()
    {
        var range = StatisticsComparisonRange.ForPreset(StatisticsPeriod.Yesterday, Now);

        Assert.Equal(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.FromHours(8)), range.Current.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(8)), range.Current.End);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.FromHours(8)), range.Previous.Start);
        Assert.Equal(range.Current.Start, range.Previous.End);
    }

    [Fact]
    public void Last_seven_days_compares_with_the_preceding_seven_full_days()
    {
        var range = StatisticsComparisonRange.ForPreset(StatisticsPeriod.Last7Days, Now);

        Assert.Equal(new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.FromHours(8)), range.Current.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.FromHours(8)), range.Current.End);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.FromHours(8)), range.Previous.Start);
        Assert.Equal(range.Current.Start, range.Previous.End);
        Assert.Equal("最近7天", range.Label);
    }

    [Fact]
    public void Last_thirty_days_compares_with_the_preceding_thirty_full_days()
    {
        var range = StatisticsComparisonRange.ForPreset(StatisticsPeriod.Last30Days, Now);

        Assert.Equal(new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.FromHours(8)), range.Current.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.FromHours(8)), range.Current.End);
        Assert.Equal(new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.FromHours(8)), range.Previous.Start);
        Assert.Equal(range.Current.Start, range.Previous.End);
    }

    [Fact]
    public void Custom_range_preserves_an_explicit_previous_window()
    {
        var current = new StatisticsDateRange(
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.FromHours(8)));
        var previous = new StatisticsDateRange(
            new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(8)));

        var range = new StatisticsComparisonRange(current, previous, "自定义时间");

        Assert.Equal(current, range.Current);
        Assert.Equal(previous, range.Previous);
    }
}
