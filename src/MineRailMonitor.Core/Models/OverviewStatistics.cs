namespace MineRailMonitor.Core.Models;

public sealed class OverviewStatistics
{
    public OverviewStatistics(
        int totalCount,
        int normalCount,
        int alarmCount,
        double averageDurationSeconds = 0d,
        int unverifiedCount = 0,
        int averageDurationSampleCount = -1)
    {
        TotalCount = totalCount;
        NormalCount = normalCount;
        AlarmCount = alarmCount;
        UnverifiedCount = unverifiedCount;
        VerifiableCount = normalCount + alarmCount;
        AverageDurationSampleCount = averageDurationSampleCount < 0 ? totalCount : averageDurationSampleCount;
        AlarmRatePercent = VerifiableCount == 0 ? 0d : alarmCount * 100d / VerifiableCount;
        AverageDurationSeconds = Math.Max(0d, averageDurationSeconds);
    }

    public int TotalCount { get; }

    public int NormalCount { get; }

    public int AlarmCount { get; }

    public int UnverifiedCount { get; }

    public int VerifiableCount { get; }

    public bool HasVerifiablePassages => VerifiableCount > 0;

    public double AlarmRatePercent { get; }

    public double AverageDurationSeconds { get; }

    public int AverageDurationSampleCount { get; }

    public bool HasAverageDurationSamples => AverageDurationSampleCount > 0;
}
