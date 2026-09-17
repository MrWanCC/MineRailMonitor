namespace MineRailMonitor.Core.Models;

public sealed class PassageQuery
{
    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? To { get; set; }

    public string? StationId { get; set; }

    public IReadOnlyList<string>? StationIds { get; set; }

    public byte? StationAddress { get; set; }

    public ushort? HeadRfid { get; set; }

    public PassageOutcome? Outcome { get; set; }

    /// <summary>
    /// Includes both uncoupling alarms and completed passages that contain recognition warnings.
    /// </summary>
    public bool IncludeWarnings { get; set; }

    public int PageIndex { get; set; }

    public int PageSize { get; set; } = 20;

    public void Validate()
    {
        if (From.HasValue && To.HasValue && From.Value > To.Value)
        {
            throw new ArgumentException("查询开始时间不能晚于结束时间。", nameof(From));
        }
        if (PageIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PageIndex));
        }
        if (PageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(PageSize));
        }
    }
}
