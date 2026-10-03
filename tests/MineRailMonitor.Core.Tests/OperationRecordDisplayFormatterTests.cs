using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class OperationRecordDisplayFormatterTests
{
    [Fact]
    public void Operation_record_displays_summary_and_retains_details()
    {
        var record = new OperationRecord
        {
            Id = Guid.Parse("e3400c91-642e-4ff5-aa0c-3c0db7a9b043"),
            Time = new DateTimeOffset(2026, 10, 2, 9, 8, 7, TimeSpan.Zero),
            OperatorName = "管理员",
            OperationType = "修改车辆配置",
            TargetType = "车辆",
            TargetName = "K001",
            BeforeValue = "总节数: 6节\n车头RFID: 01",
            AfterValue = "总节数: 7节\n车头RFID: 01",
            Result = "成功"
        };

        var display = OperationRecordDisplayFormatter.Format(record);

        Assert.Equal("2026-10-02 09:08:07", display.TimeText);
        Assert.Equal("车辆：K001", display.TargetText);
        Assert.Equal("修改前：总节数: 6节；车头RFID: 01；修改后：总节数: 7节；车头RFID: 01", display.Content);
        Assert.Equal(record.BeforeValue, display.BeforeValue);
        Assert.Equal(record.AfterValue, display.AfterValue);
    }
}
