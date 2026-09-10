namespace MineRailMonitor.Core.Models;

public sealed class PassageQueryResult
{
    public PassageQueryResult(
        IReadOnlyList<PassageRecord> items,
        int totalCount,
        int pageIndex,
        int pageSize)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        TotalCount = totalCount;
        PageIndex = pageIndex;
        PageSize = pageSize;
    }

    public IReadOnlyList<PassageRecord> Items { get; }

    public int TotalCount { get; }

    public int PageIndex { get; }

    public int PageSize { get; }
}
