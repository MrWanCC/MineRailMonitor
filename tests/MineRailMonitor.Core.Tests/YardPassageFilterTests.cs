using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class YardPassageFilterTests
{
    private static readonly DateTimeOffset Today = new(2026, 9, 15, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Filter_keeps_only_records_from_current_yard_station_ids()
    {
        var records = new[]
        {
            CreateRecord("RFID-01", 0x01),
            CreateRecord("RFID-02", 0x02),
            CreateRecord("RFID-03", 0x03)
        };

        var filtered = YardPassageFilter.Filter(records, new[] { "RFID-01", "RFID-03" });

        Assert.Equal(new[] { "RFID-01", "RFID-03" }, filtered.Select(record => record.StationId));
    }

    [Fact]
    public void Filter_returns_no_records_for_a_yard_without_bound_stations()
    {
        var records = new[] { CreateRecord("RFID-01", 0x01) };

        var filtered = YardPassageFilter.Filter(records, Array.Empty<string>());

        Assert.Empty(filtered);
    }

    [Fact]
    public void BuildStatistics_counts_only_records_from_current_yard()
    {
        var records = new[]
        {
            CreateRecord("RFID-01", 0x01, PassageOutcome.Completed),
            CreateRecord("RFID-01", 0x01, PassageOutcome.UncouplingAlarm, Today.AddMinutes(1)),
            CreateRecord("RFID-02", 0x02, PassageOutcome.Completed, Today.AddMinutes(2))
        };

        var statistics = YardPassageFilter.BuildStatistics(records, Today, new[] { "RFID-01" });

        Assert.Equal(2, statistics.TodayPassageCount);
        Assert.Equal(1, statistics.TodayNormalCount);
        Assert.Equal(1, statistics.TodayAlarmCount);
        Assert.Equal(2, statistics.ByStation["RFID-01"]);
        Assert.DoesNotContain("RFID-02", statistics.ByStation.Keys);
    }

    private static PassageRecord CreateRecord(
        string stationId,
        byte stationAddress,
        PassageOutcome outcome = PassageOutcome.Completed,
        DateTimeOffset? completedAt = null)
    {
        var completed = completedAt ?? Today;
        var started = completed.AddSeconds(-10);
        return new PassageRecord(
            Guid.NewGuid(),
            stationId,
            stationAddress,
            0x0001,
            new ushort[] { 0x0001 },
            11,
            outcome,
            started,
            completed,
            outcome == PassageOutcome.Completed ? Array.Empty<string>() : new[] { "脱节报警" },
            outcome == PassageOutcome.Completed ? null : "脱节报警");
    }
}
