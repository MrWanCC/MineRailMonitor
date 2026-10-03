using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class AlarmOperationRecordFactoryTests
{
    [Fact]
    public void Creates_confirmation_and_recovery_records_for_the_vehicle()
    {
        var passageId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 10, 2, 10, 30, 0, TimeSpan.FromHours(8));
        var acknowledgedAt = occurredAt.AddMinutes(1);
        var recoveredAt = occurredAt.AddMinutes(2);
        var record = new PassageRecord(
            passageId,
            "RFID-01",
            1,
            1,
            new ushort[] { 1, 17, 18, 19, 20, 21 },
            7,
            PassageOutcome.UncouplingAlarm,
            occurredAt.AddSeconds(-30),
            occurredAt,
            vehicleName: "1号矿车",
            vehicleHeadRfid: 1,
            vehicleNo: "K001");

        var acknowledged = AlarmOperationRecordFactory.CreateAcknowledged(record, acknowledgedAt);
        var recovered = AlarmOperationRecordFactory.CreateRecovered(record, recoveredAt);

        Assert.Equal("确认报警", acknowledged.OperationType);
        Assert.Equal("车辆", acknowledged.TargetType);
        Assert.Equal("K001", acknowledged.TargetName);
        Assert.Equal("确认脱节报警", acknowledged.AfterValue);
        Assert.Equal(acknowledgedAt, acknowledged.Time);
        Assert.Equal("报警恢复", recovered.OperationType);
        Assert.Equal("车辆", recovered.TargetType);
        Assert.Equal("K001", recovered.TargetName);
        Assert.Equal("状态: 已恢复", recovered.AfterValue);
        Assert.Equal(recoveredAt, recovered.Time);
    }
}
