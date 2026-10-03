namespace MineRailMonitor.Core.Models;

public sealed class PassageEventTimelineItem
{
    public string TimeText { get; init; } = string.Empty;

    public string EventTypeText { get; init; } = string.Empty;

    public string StatusKind { get; init; } = string.Empty;

    public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();
}
