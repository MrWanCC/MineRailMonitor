using MineRailMonitor.Core.ExternalAlarm;

namespace MineRailMonitor.Core.Tests;

public sealed class ExternalAlarmLatchTests
{
    [Fact]
    public void Internal_recovery_cannot_replace_a_latched_alarm_with_normal()
    {
        var cache = new ExternalAlarmStatusCache();
        var alarmAt = DateTimeOffset.Parse("2026-09-30T10:00:00+08:00");

        cache.Update(
            "RFID-620-01",
            new ExternalAlarmStationSnapshot(
                stationAddress: 0x01,
                status: ExternalAlarmStatus.UncouplingAlarm,
                accumulatedVehicleCount: 6,
                stationId: "RFID-620-01"),
            alarmAt);

        cache.Update(
            "RFID-620-01",
            new ExternalAlarmStationSnapshot(
                stationAddress: 0x01,
                status: ExternalAlarmStatus.Normal,
                accumulatedVehicleCount: 6,
                stationId: "RFID-620-01"),
            alarmAt.AddSeconds(3));

        Assert.True(cache.TryGet("RFID-620-01", out var snapshot));
        Assert.Equal(ExternalAlarmStatus.UncouplingAlarm, snapshot.Status);
        Assert.True(snapshot.AlarmLatched);
        Assert.Equal("RFID-620-01", snapshot.StationId);
    }

    [Fact]
    public void Matching_external_clear_releases_alarm_then_normal_expires_to_no_vehicle()
    {
        var cache = new ExternalAlarmStatusCache();
        var alarmAt = DateTimeOffset.Parse("2026-09-30T10:00:00+08:00");
        cache.Update(
            "RFID-620-01",
            new ExternalAlarmStationSnapshot(
                stationAddress: 0x01,
                status: ExternalAlarmStatus.UncouplingAlarm,
                accumulatedVehicleCount: 6,
                stationId: "RFID-620-01"),
            alarmAt);

        Assert.True(cache.TryReleaseAlarm(0x01, alarmAt.AddSeconds(5), out var released));
        Assert.Equal(ExternalAlarmStatus.Normal, released.Status);
        Assert.False(released.AlarmLatched);
        Assert.Equal(6, released.AccumulatedVehicleCount);
        Assert.Equal("RFID-620-01", released.StationId);

        var afterRetention = cache.GetForSending(
            "RFID-620-01",
            alarmAt.AddSeconds(5).AddMinutes(2).AddSeconds(1));
        Assert.Equal(ExternalAlarmStatus.NoVehicle, afterRetention.Status);
        Assert.False(afterRetention.AlarmLatched);
    }

    [Fact]
    public void External_clear_for_another_address_does_not_release_latched_alarm()
    {
        var cache = new ExternalAlarmStatusCache();
        cache.Update(
            "RFID-620-01",
            new ExternalAlarmStationSnapshot(
                stationAddress: 0x01,
                status: ExternalAlarmStatus.UncouplingAlarm,
                accumulatedVehicleCount: 6,
                stationId: "RFID-620-01"));

        Assert.False(cache.TryReleaseAlarm(0x02, DateTimeOffset.UtcNow, out _));
        Assert.True(cache.TryGet("RFID-620-01", out var snapshot));
        Assert.Equal(ExternalAlarmStatus.UncouplingAlarm, snapshot.Status);
        Assert.True(snapshot.AlarmLatched);
    }

    [Fact]
    public void Internal_transition_after_external_clear_does_not_relatched_alarm()
    {
        var cache = new ExternalAlarmStatusCache();
        var alarmAt = DateTimeOffset.Parse("2026-09-30T10:00:00+08:00");
        cache.Update(
            "RFID-620-01",
            new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.UncouplingAlarm, 6),
            alarmAt);

        Assert.True(cache.TryReleaseAlarm(0x01, alarmAt.AddSeconds(5), out _));

        cache.Update(
            "RFID-620-01",
            new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.UncouplingAlarm, 6),
            alarmAt.AddSeconds(6));
        Assert.True(cache.TryGet("RFID-620-01", out var released));
        Assert.Equal(ExternalAlarmStatus.Normal, released.Status);
        Assert.False(released.AlarmLatched);

        cache.Update(
            "RFID-620-01",
            new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.Recognizing, 1),
            alarmAt.AddSeconds(7));
        cache.Update(
            "RFID-620-01",
            new ExternalAlarmStationSnapshot(0x01, ExternalAlarmStatus.UncouplingAlarm, 1),
            alarmAt.AddSeconds(8));

        Assert.True(cache.TryGet("RFID-620-01", out var nextAlarm));
        Assert.Equal(ExternalAlarmStatus.UncouplingAlarm, nextAlarm.Status);
        Assert.True(nextAlarm.AlarmLatched);
        Assert.Equal(alarmAt.AddSeconds(8), nextAlarm.AlarmTime);
    }
}
