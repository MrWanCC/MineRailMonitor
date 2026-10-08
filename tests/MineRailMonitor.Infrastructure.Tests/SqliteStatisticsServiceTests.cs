using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Infrastructure.Persistence;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class SqliteStatisticsServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset End = Start.AddHours(1);

    [Fact]
    public void GetOverview_counts_normal_and_uncoupling_passages_and_excludes_end_boundary()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddMinutes(1), PassageOutcome.Completed, "K001", "一号矿车"));
        database.Store.Save(CreateRecord(Start.AddMinutes(2), PassageOutcome.Completed, "K002", "二号矿车"));
        database.Store.Save(CreateRecord(Start.AddMinutes(3), PassageOutcome.UncouplingAlarm, "K001", "一号矿车"));
        database.Store.Save(CreateRecord(End, PassageOutcome.Completed, "K001", "一号矿车"));

        var result = database.Store.GetOverview(Start, End);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.NormalCount);
        Assert.Equal(1, result.AlarmCount);
        Assert.Equal(0, result.UnverifiedCount);
        Assert.Equal(3, result.VerifiableCount);
        Assert.Equal(100d / 3d, result.AlarmRatePercent, 5);
        Assert.Equal(5d, result.AverageDurationSeconds, 5);
    }

    [Fact]
    public void Overview_counts_unverified_records_but_excludes_them_from_alarm_rate_denominator()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddMinutes(1), PassageOutcome.Completed, "K001", "一号矿车"));
        database.Store.Save(CreateRecord(Start.AddMinutes(2), PassageOutcome.Completed, "K001", "一号矿车"));
        database.Store.Save(CreateRecord(Start.AddMinutes(3), PassageOutcome.UncouplingAlarm, "K001", "一号矿车"));
        database.Store.Save(CreateRecord(Start.AddMinutes(4), PassageOutcome.Unverified, "K001", "一号矿车", expectedCount: null));
        database.Store.Save(CreateRecord(Start.AddMinutes(5), PassageOutcome.Unverified, null, null, expectedCount: null));
        database.Store.Save(CreateRecord(Start.AddMinutes(6), PassageOutcome.Unverified, null, null, expectedCount: null));

        var result = database.Store.GetOverview(Start, End);

        Assert.Equal(6, result.TotalCount);
        Assert.Equal(2, result.NormalCount);
        Assert.Equal(1, result.AlarmCount);
        Assert.Equal(3, result.UnverifiedCount);
        Assert.Equal(3, result.VerifiableCount);
        Assert.Equal(100d / 3d, result.AlarmRatePercent, 5);
    }

    [Fact]
    public void Overview_alarm_rate_has_no_value_when_there_are_no_verifiable_passages()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddMinutes(1), PassageOutcome.Unverified, null, null, expectedCount: null));

        var result = database.Store.GetOverview(Start, End);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.UnverifiedCount);
        Assert.Equal(0, result.VerifiableCount);
        Assert.False(result.HasVerifiablePassages);
    }

    [Fact]
    public void Overview_reports_no_average_duration_when_all_completed_durations_are_invalid()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(new PassageRecord(
            Guid.NewGuid(),
            "RFID-01",
            1,
            1,
            new ushort[] { 1 },
            1,
            PassageOutcome.Completed,
            Start.AddMinutes(2),
            Start.AddMinutes(1)));

        var result = database.Store.GetOverview(Start, End);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(0d, result.AverageDurationSeconds);
        Assert.False(result.HasAverageDurationSamples);
    }

    [Fact]
    public void Ten_passage_acceptance_batch_reports_eight_normal_two_alarms_and_station_scopes_do_not_mix()
    {
        using var database = new TemporaryDatabase();
        var at = Start.AddMinutes(1);
        for (var index = 0; index < 5; index++)
        {
            database.Store.Save(CreateRecord(at.AddMinutes(index), PassageOutcome.Completed, "K001", "一号矿车", stationId: "RFID-560-01"));
        }
        for (var index = 0; index < 2; index++)
        {
            database.Store.Save(CreateRecord(at.AddMinutes(5 + index), PassageOutcome.UncouplingAlarm, "K001", "一号矿车", stationId: "RFID-560-01"));
        }
        for (var index = 0; index < 3; index++)
        {
            database.Store.Save(CreateRecord(at.AddMinutes(7 + index), PassageOutcome.Completed, "K002", "二号矿车", stationId: "RFID-620-01"));
        }

        var global = database.Store.GetOverview(Start, End, stationIds: null);
        var firstStation = database.Store.GetOverview(Start, End, new[] { "RFID-560-01" });
        var secondStation = database.Store.GetOverview(Start, End, new[] { "RFID-620-01" });
        var empty = database.Store.GetOverview(Start, End, Array.Empty<string>());

        Assert.Equal(10, global.TotalCount);
        Assert.Equal(8, global.NormalCount);
        Assert.Equal(2, global.AlarmCount);
        Assert.Equal(20d, global.AlarmRatePercent, 5);
        Assert.Equal(7, firstStation.TotalCount);
        Assert.Equal(2, firstStation.AlarmCount);
        Assert.Equal(3, secondStation.TotalCount);
        Assert.Equal(0, secondStation.AlarmCount);
        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(database.Store.GetVehicleStatistics(Start, End, Array.Empty<string>()));
    }

    [Fact]
    public void Statistics_with_null_station_scope_include_all_stations()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddMinutes(1), PassageOutcome.Completed, "K001", "一号矿车", stationId: "RFID-01"));
        database.Store.Save(CreateRecord(Start.AddMinutes(2), PassageOutcome.UncouplingAlarm, "K002", "二号矿车", stationId: "RFID-02"));

        var overview = database.Store.GetOverview(Start, End, stationIds: null);
        var vehicles = database.Store.GetVehicleStatistics(Start, End, stationIds: null);
        var alarms = database.Store.GetAlarmStatistics(Start, End, stationIds: null);

        Assert.Equal(2, overview.TotalCount);
        Assert.Equal(2, vehicles.Sum(item => item.TotalCount));
        Assert.Equal(1, Assert.Single(alarms).Count);
    }

    [Fact]
    public void Statistics_with_empty_station_scope_return_zero_without_leaking_global_records()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddMinutes(1), PassageOutcome.Completed, "K001", "一号矿车", stationId: "RFID-01"));
        database.Store.Save(CreateRecord(Start.AddMinutes(2), PassageOutcome.UncouplingAlarm, "K002", "二号矿车", stationId: "RFID-02"));

        var overview = database.Store.GetOverview(Start, End, Array.Empty<string>());
        var vehicles = database.Store.GetVehicleStatistics(Start, End, Array.Empty<string>());
        var alarms = database.Store.GetAlarmStatistics(Start, End, Array.Empty<string>());

        Assert.Equal(0, overview.TotalCount);
        Assert.Equal(0, overview.NormalCount);
        Assert.Equal(0, overview.AlarmCount);
        Assert.Equal(0d, overview.AlarmRatePercent);
        Assert.False(overview.HasVerifiablePassages);
        Assert.Empty(vehicles);
        Assert.Empty(alarms);
    }

    [Fact]
    public void Yard_without_bound_rfid_stations_does_not_fall_back_to_global_statistics()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(
            Start.AddMinutes(1),
            PassageOutcome.Completed,
            "K620",
            "620站矿车",
            stationId: "RFID-620"));
        var resolver = new YardRfidStationResolver(
            new[]
            {
                new StationConfig
                {
                    Id = "560",
                    Devices = new[]
                    {
                        new DeviceConfig
                        {
                            Id = "map-rfid-missing",
                            Type = DeviceType.RfidStation,
                            RfidStationId = "RFID-MISSING"
                        }
                    }
                }
            },
            new[]
            {
                new RfidStationConfig
                {
                    StationId = "RFID-620",
                    YardId = "620",
                    IpAddress = "127.0.0.1",
                    Port = 62001,
                    ProtocolAddress = 1
                }
            });

        var scope = resolver.Resolve("560");
        var stationIds = scope.IsGlobal ? null : scope.RfidStationIds;
        var overview = database.Store.GetOverview(Start, End, stationIds);

        Assert.False(scope.IsGlobal);
        Assert.Empty(scope.RfidStationIds);
        Assert.Equal(0, overview.TotalCount);
    }

    [Fact]
    public void Statistics_with_single_station_scope_exclude_other_stations()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddMinutes(1), PassageOutcome.Completed, "K001", "一号矿车", stationId: "RFID-01"));
        database.Store.Save(CreateRecord(Start.AddMinutes(2), PassageOutcome.UncouplingAlarm, "K002", "二号矿车", stationId: "RFID-02"));

        var overview = database.Store.GetOverview(Start, End, new[] { "RFID-01" });
        var vehicles = database.Store.GetVehicleStatistics(Start, End, new[] { "RFID-01" });

        Assert.Equal(1, overview.TotalCount);
        Assert.Equal("K001", Assert.Single(vehicles).VehicleNo);
    }

    [Fact]
    public void Statistics_with_multiple_station_scope_merge_only_selected_stations()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddMinutes(1), PassageOutcome.Completed, "K001", "一号矿车", stationId: "RFID-01"));
        database.Store.Save(CreateRecord(Start.AddMinutes(2), PassageOutcome.UncouplingAlarm, "K002", "二号矿车", stationId: "RFID-02"));
        database.Store.Save(CreateRecord(Start.AddMinutes(3), PassageOutcome.Completed, "K003", "三号矿车", stationId: "RFID-03"));

        var overview = database.Store.GetOverview(Start, End, new[] { "RFID-01", "RFID-02" });
        var vehicles = database.Store.GetVehicleStatistics(Start, End, new[] { "RFID-01", "RFID-02" });

        Assert.Equal(2, overview.TotalCount);
        Assert.Equal(2, vehicles.Sum(item => item.TotalCount));
        Assert.DoesNotContain(vehicles, item => item.VehicleNo == "K003");
    }

    [Fact]
    public void Statistics_with_unknown_station_scope_return_empty_results()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddMinutes(1), PassageOutcome.Completed, "K001", "一号矿车", stationId: "RFID-01"));

        var overview = database.Store.GetOverview(Start, End, new[] { "RFID-UNKNOWN" });

        Assert.Equal(0, overview.TotalCount);
        Assert.Empty(database.Store.GetVehicleStatistics(Start, End, new[] { "RFID-UNKNOWN" }));
        Assert.Empty(database.Store.GetAlarmStatistics(Start, End, new[] { "RFID-UNKNOWN" }));
    }

    [Fact]
    public void GetVehicleStatistics_groups_by_vehicle_snapshot_and_includes_unconfigured_passages()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddMinutes(1), PassageOutcome.Completed, "K001", "一号矿车"));
        database.Store.Save(CreateRecord(Start.AddMinutes(2), PassageOutcome.UncouplingAlarm, "K001", "一号矿车"));
        database.Store.Save(CreateRecord(Start.AddMinutes(3), PassageOutcome.Completed, "K001", "一矿车"));
        database.Store.Save(CreateRecord(Start.AddMinutes(4), PassageOutcome.Completed, "K002", "二号矿车"));
        database.Store.Save(CreateRecord(Start.AddMinutes(5), PassageOutcome.Completed, null, null));
        database.Store.Save(CreateRecord(Start.AddMinutes(6), PassageOutcome.Unverified, "K001", "一号矿车", expectedCount: null));
        database.Store.Save(CreateRecord(Start.AddMinutes(7), PassageOutcome.Unverified, null, null, expectedCount: null));

        var result = database.Store.GetVehicleStatistics(Start, End);

        Assert.Equal(3, result.Count);
        var firstVehicle = Assert.Single(result, item => item.VehicleNo == "K001");
        Assert.Equal(3, firstVehicle.TotalCount);
        Assert.Equal(2, firstVehicle.NormalCount);
        Assert.Equal(1, firstVehicle.AlarmCount);
        Assert.Equal(100d / 3d, firstVehicle.AlarmRatePercent, 5);

        var secondVehicle = Assert.Single(result, item => item.VehicleNo == "K002");
        Assert.Equal(1, secondVehicle.TotalCount);
        Assert.Equal(0d, secondVehicle.AlarmRatePercent);
        Assert.Single(result, item => item.VehicleNo is null && item.VehicleName is null);
    }

    [Fact]
    public void GetAlarmStatistics_groups_uncoupling_alarms_by_derived_status()
    {
        using var database = new TemporaryDatabase();
        database.Store.Save(CreateRecord(Start.AddMinutes(1), PassageOutcome.UncouplingAlarm, "K001", "一号矿车"));
        database.Store.Save(CreateRecord(Start.AddMinutes(2), PassageOutcome.UncouplingAlarm, "K002", "二号矿车", Start.AddMinutes(5)));
        database.Store.Save(CreateRecord(Start.AddMinutes(3), PassageOutcome.UncouplingAlarm, "K003", "三号矿车", Start.AddMinutes(6), Start.AddMinutes(7)));
        database.Store.Save(CreateRecord(Start.AddMinutes(4), PassageOutcome.Completed, "K001", "一号矿车"));

        var result = database.Store.GetAlarmStatistics(Start, End).ToDictionary(item => item.Status, item => item.Count);

        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[AlarmStatus.Unconfirmed]);
        Assert.Equal(1, result[AlarmStatus.Acknowledged]);
        Assert.Equal(1, result[AlarmStatus.Recovered]);
    }

    [Fact]
    public void Empty_range_returns_zero_overview_and_empty_detail_statistics()
    {
        using var database = new TemporaryDatabase();

        var overview = database.Store.GetOverview(Start, End);

        Assert.Equal(0, overview.TotalCount);
        Assert.Equal(0, overview.NormalCount);
        Assert.Equal(0, overview.AlarmCount);
        Assert.Equal(0d, overview.AlarmRatePercent);
        Assert.Empty(database.Store.GetVehicleStatistics(Start, End));
        Assert.Empty(database.Store.GetAlarmStatistics(Start, End));
    }

    private static PassageRecord CreateRecord(
        DateTimeOffset completedAt,
        PassageOutcome outcome,
        string? vehicleNo,
        string? vehicleName,
        DateTimeOffset? acknowledgedAt = null,
        DateTimeOffset? recoveredAt = null,
        string stationId = "RFID-01",
        int? expectedCount = 3)
    {
        var startedAt = completedAt.AddSeconds(-5);
        return new PassageRecord(
            Guid.NewGuid(),
            stationId,
            1,
            1,
            new ushort[] { 1, 17 },
            expectedCount,
            outcome,
            startedAt,
            completedAt,
            outcome == PassageOutcome.UncouplingAlarm ? new[] { "脱节报警" } : Array.Empty<string>(),
            outcome == PassageOutcome.UncouplingAlarm ? "脱节报警" : null,
            alarmAcknowledgedAt: acknowledgedAt,
            alarmRecoveredAt: recoveredAt,
            vehicleName: vehicleName,
            vehicleHeadRfid: vehicleNo is null ? null : (ushort?)1,
            vehicleNo: vehicleNo);
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));

        public TemporaryDatabase()
        {
            Directory.CreateDirectory(_directory);
            Store = new SqlitePassageRecordStore(Path.Combine(_directory, "passages.db"));
        }

        public SqlitePassageRecordStore Store { get; }

        public void Dispose()
        {
            Store.Dispose();
            foreach (var file in Directory.EnumerateFiles(_directory))
            {
                File.Delete(file);
            }

            Directory.Delete(_directory);
        }
    }
}
