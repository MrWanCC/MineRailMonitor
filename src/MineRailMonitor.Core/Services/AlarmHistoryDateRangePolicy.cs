namespace MineRailMonitor.Core.Services;

public sealed class AlarmHistoryDateRangePolicy
{
    private const int DefaultRangeDays = 30;
    private bool _followsDefaultRange = true;

    public bool ShouldRefreshFor(DateTime? from, DateTime? to, DateTime today)
    {
        if (!_followsDefaultRange)
        {
            return false;
        }

        var expectedTo = today.Date;
        var expectedFrom = expectedTo.AddDays(-(DefaultRangeDays - 1));
        return from?.Date != expectedFrom || to?.Date != expectedTo;
    }

    public void RecordUserSelection(DateTime? from, DateTime? to, DateTime today)
    {
        var expectedTo = today.Date;
        var expectedFrom = expectedTo.AddDays(-(DefaultRangeDays - 1));
        _followsDefaultRange = from?.Date == expectedFrom && to?.Date == expectedTo;
    }

    public void Reset() => _followsDefaultRange = true;
}
