namespace MineRailMonitor.Core.Services;

public sealed class AlarmHistoryDateRangePolicy
{
    private bool _followsToday = true;

    public bool ShouldRefreshFor(DateTime? from, DateTime? to, DateTime today)
    {
        if (!_followsToday)
        {
            return false;
        }

        var currentDate = today.Date;
        return from?.Date != currentDate || to?.Date != currentDate;
    }

    public void RecordUserSelection(DateTime? from, DateTime? to, DateTime today)
    {
        var currentDate = today.Date;
        _followsToday = from?.Date == currentDate && to?.Date == currentDate;
    }

    public void Reset() => _followsToday = true;
}
