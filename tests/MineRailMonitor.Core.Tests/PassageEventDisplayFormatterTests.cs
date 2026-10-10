using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class PassageEventDisplayFormatterTests
{
    [Theory]
    [InlineData(PassageEventTypes.Start, "开始识别", "进行中", "InProgress", "车头RFID:01 理论7节")]
    [InlineData(PassageEventTypes.Progress, "识别进度", "进行中", "InProgress", "当前5/7")]
    [InlineData(PassageEventTypes.Complete, "通过完成", "完成", "Complete", "实际7/7，通行完成")]
    [InlineData(PassageEventTypes.Alarm, "脱节报警", "报警", "Alarm", "实际6/7 缺少1节")]
    [InlineData(PassageEventTypes.Clear, "清除完成", "已清除", "Clear", "清除完成")]
    public void Formats_passage_lifecycle_events_for_the_run_log(
        string eventType,
        string expectedEventTypeText,
        string expectedStatus,
        string expectedStatusKind,
        string expectedContent)
    {
        var detectedCount = eventType switch
        {
            PassageEventTypes.Start => 1,
            PassageEventTypes.Progress => 5,
            PassageEventTypes.Alarm => 6,
            _ => 7
        };
        var passageEvent = new PassageEvent
        {
            PassageId = Guid.Parse("86c053d8-fcb7-4e58-bd6d-f6db4c76c57a"),
            EventType = eventType,
            OccurredAt = new DateTimeOffset(2026, 10, 2, 10, 30, 35, TimeSpan.FromHours(8)),
            VehicleName = "1号矿车",
            VehicleNo = "K001",
            VehicleHeadRfid = 1,
            ExpectedCount = 7,
            DetectedCount = detectedCount,
            MissingCount = 7 - detectedCount,
            Reason = eventType == PassageEventTypes.Alarm ? "识别超时" : null
        };

        var display = PassageEventDisplayFormatter.Format(passageEvent);

        Assert.Equal(expectedEventTypeText, display.EventTypeText);
        Assert.Equal(expectedStatus, display.Status);
        Assert.Equal(expectedStatusKind, display.StatusKind);
        Assert.Equal(expectedContent, display.Content);
        Assert.Equal("2026-10-02 10:30:35", display.TimeText);
        Assert.Equal("1号矿车", display.VehicleName);
        Assert.Equal("K001", display.VehicleNo);
        Assert.Equal("K001", display.VehicleNumber);
        Assert.Equal("01", display.HeadRfidText);
        Assert.Equal("7节", display.ExpectedCountText);
        Assert.Equal($"{detectedCount}节", display.ActualCountText);
        Assert.Equal($"{7 - detectedCount}节", display.MissingCountText);
        Assert.Equal("86c053d8-fcb7-4e58-bd6d-f6db4c76c57a", display.PassageIdText);
    }

    [Fact]
    public void Displays_unconfigured_vehicle_when_event_has_no_vehicle_match()
    {
        var display = PassageEventDisplayFormatter.Format(new PassageEvent
        {
            PassageId = Guid.NewGuid(),
            EventType = PassageEventTypes.Start,
            OccurredAt = DateTimeOffset.UtcNow,
            VehicleHeadRfid = 2
        });

        Assert.Equal("未配置车辆", display.VehicleNo);
        Assert.Equal("未配置车辆", display.VehicleName);
    }

    [Fact]
    public void Displays_persistence_failure_with_passage_identity_and_recovery_state()
    {
        var passageId = Guid.Parse("86c053d8-fcb7-4e58-bd6d-f6db4c76c57a");
        var failure = PassageEventDisplayFormatter.Format(new PassageEvent
        {
            PassageId = passageId,
            EventType = PassageEventTypes.PersistenceFailed,
            OccurredAt = new DateTimeOffset(2026, 10, 4, 10, 30, 35, TimeSpan.FromHours(8)),
            StationId = "RFID-560-01",
            VehicleNo = "K001",
            FailureCount = 3,
            Reason = "IOException: 数据库暂不可用"
        });
        var recovered = PassageEventDisplayFormatter.Format(new PassageEvent
        {
            PassageId = passageId,
            EventType = PassageEventTypes.PersistenceRecovered,
            OccurredAt = new DateTimeOffset(2026, 10, 4, 10, 31, 35, TimeSpan.FromHours(8)),
            StationId = "RFID-560-01",
            VehicleNo = "K001",
            FailureCount = 3
        });

        Assert.Equal("数据库保存失败", failure.EventTypeText);
        Assert.Equal("待恢复", failure.Status);
        Assert.Contains("失败（3次）", failure.Content, StringComparison.Ordinal);
        Assert.Equal(passageId.ToString("D"), failure.PassageIdText);
        Assert.Equal("RFID-560-01", failure.StationIdText);
        Assert.Equal("数据库保存恢复", recovered.EventTypeText);
        Assert.Equal("已恢复", recovered.Status);
        Assert.Contains("此前失败3次", recovered.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Formats_unverified_terminal_event_with_station_passage_count_and_reason()
    {
        var passageId = Guid.NewGuid();
        var passageEvent = new PassageEvent
        {
            PassageId = passageId,
            EventType = PassageEventTypes.Unverified,
            OccurredAt = new DateTimeOffset(2026, 10, 3, 10, 0, 2, TimeSpan.FromHours(8)),
            StationId = "RFID-560-01",
            DetectedCount = 3,
            Reason = "车头未匹配车辆配置，完整性未判定"
        };

        var item = PassageEventDisplayFormatter.Format(passageEvent);
        var details = PassageEventDisplayFormatter.FormatPassage(new[] { passageEvent });

        Assert.Equal("完整性未判定", item.EventTypeText);
        Assert.Equal("完整性未判定", item.Status);
        Assert.Equal("Unverified", item.StatusKind);
        Assert.Contains("已识别3个RFID", item.Content, StringComparison.Ordinal);
        Assert.Equal("RFID-560-01", item.StationIdText);
        Assert.Equal(passageId.ToString("D"), item.PassageIdText);
        Assert.NotNull(details);
        Assert.Equal("RFID-560-01", details!.StationIdText);
        Assert.Equal("完整性未判定", details.CurrentStatusText);
        Assert.Equal("车头未匹配车辆配置，完整性未判定", details.OutcomeReasonText);
        Assert.Equal("3节", details.ActualCountText);
        Assert.Equal(new[]
        {
            "已识别: 3个唯一RFID",
            "结果: 完整性未判定",
            "原因: 车头未匹配车辆配置，完整性未判定"
        }, details.Timeline[0].Details);
    }

    [Fact]
    public void Formats_run_details_and_an_ascending_timeline_for_only_one_passage()
    {
        var passageId = Guid.Parse("86c053d8-fcb7-4e58-bd6d-f6db4c76c57a");
        var otherPassage = Guid.NewGuid();
        var alarm = CreatePassageEvent(passageId, PassageEventTypes.Alarm, 35, 6, 1, "脱节报警");
        var unrelated = CreatePassageEvent(otherPassage, PassageEventTypes.Complete, 2, 2, 0, null);
        var progress = CreatePassageEvent(passageId, PassageEventTypes.Progress, 5, 5, 2, null);
        var start = CreatePassageEvent(passageId, PassageEventTypes.Start, 1, 1, 6, null);

        var details = PassageEventDisplayFormatter.FormatPassage(new[] { alarm, unrelated, progress, start });

        Assert.NotNull(details);
        Assert.Equal("K001", details.VehicleNo);
        Assert.Equal("K001", details.VehicleNumber);
        Assert.Equal("1号矿车", details.VehicleName);
        Assert.Equal("01", details.HeadRfidText);
        Assert.Equal("7节", details.ExpectedCountText);
        Assert.Equal("6节", details.ActualCountText);
        Assert.Equal("1节", details.MissingCountText);
        Assert.Equal("脱节报警", details.CurrentStatusText);
        Assert.Equal("Alarm", details.StatusKind);
        Assert.Equal(passageId.ToString("D"), details.PassageIdText);
        Assert.Equal(
            new[] { "10:30:01", "10:30:05", "10:30:35" },
            details.Timeline.Select(item => item.TimeText));
        Assert.Equal(new[] { "车头标签: 01", "理论编组: 7节" }, details.Timeline[0].Details);
        Assert.Equal(new[] { "当前: 5 / 7" }, details.Timeline[1].Details);
        Assert.Equal(
            new[] { "实际: 6节", "缺少: 1节", "原因: 识别超时未达到理论节数" },
            details.Timeline[2].Details);
    }

    [Fact]
    public void Run_details_include_alarm_occurrence_acknowledgement_and_recovery_times()
    {
        var passageId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 10, 2, 10, 30, 35, TimeSpan.FromHours(8));
        var acknowledgedAt = occurredAt.AddMinutes(1);
        var recoveredAt = occurredAt.AddMinutes(2);
        var passageEvents = new[] { CreatePassageEvent(passageId, PassageEventTypes.Alarm, 35, 6, 1, "脱节报警") };
        var record = new PassageRecord(
            passageId, "RFID-01", 1, 1, new ushort[] { 1, 17, 18, 19, 20, 21 }, 7,
            PassageOutcome.UncouplingAlarm, occurredAt.AddSeconds(-30), occurredAt,
            alarmAcknowledgedAt: acknowledgedAt,
            alarmRecoveredAt: recoveredAt);

        var details = PassageEventDisplayFormatter.FormatPassage(passageEvents, record);

        Assert.NotNull(details);
        Assert.Equal("2026-10-02 10:30:35", details!.AlarmOccurredAtText);
        Assert.Equal("2026-10-02 10:31:35", details.AlarmAcknowledgedAtText);
        Assert.Equal("2026-10-02 10:32:35", details.AlarmRecoveredAtText);
    }

    private static PassageEvent CreatePassageEvent(
        Guid passageId,
        string eventType,
        int second,
        int detectedCount,
        int missingCount,
        string? reason) => new()
    {
        PassageId = passageId,
        EventType = eventType,
        OccurredAt = new DateTimeOffset(2026, 10, 2, 10, 30, second, TimeSpan.FromHours(8)),
        VehicleName = "1号矿车",
        VehicleNo = "K001",
        VehicleHeadRfid = 1,
        ExpectedCount = 7,
        DetectedCount = detectedCount,
        MissingCount = missingCount,
        Reason = reason
    };
}
