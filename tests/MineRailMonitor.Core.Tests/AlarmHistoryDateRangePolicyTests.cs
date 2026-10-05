using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class AlarmHistoryDateRangePolicyTests
{
    [Fact]
    public void Default_today_range_is_refreshed_after_midnight()
    {
        var policy = new AlarmHistoryDateRangePolicy();
        var yesterday = new DateTime(2026, 10, 4);

        Assert.True(policy.ShouldRefreshFor(yesterday, yesterday, new DateTime(2026, 10, 5)));
    }

    [Fact]
    public void A_manually_selected_historical_range_is_preserved_after_midnight()
    {
        var policy = new AlarmHistoryDateRangePolicy();
        var yesterday = new DateTime(2026, 10, 4);
        var today = new DateTime(2026, 10, 5);

        policy.RecordUserSelection(yesterday, yesterday, today);

        Assert.False(policy.ShouldRefreshFor(yesterday, yesterday, today.AddDays(1)));
    }

    [Fact]
    public void Reset_reenables_default_today_refresh()
    {
        var policy = new AlarmHistoryDateRangePolicy();
        var yesterday = new DateTime(2026, 10, 4);

        policy.RecordUserSelection(yesterday, yesterday, yesterday.AddDays(1));
        policy.Reset();

        Assert.True(policy.ShouldRefreshFor(yesterday, yesterday, yesterday.AddDays(1)));
    }
}
