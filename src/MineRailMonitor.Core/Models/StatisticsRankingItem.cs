namespace MineRailMonitor.Core.Models;

public sealed class StatisticsRankingItem
{
    public StatisticsRankingItem(string? key, string? name, int count, double sharePercent)
    {
        Key = key;
        Name = name;
        Count = count;
        SharePercent = sharePercent;
    }

    public string? Key { get; }

    public string? Name { get; }

    public int Count { get; }

    public double SharePercent { get; }
}
