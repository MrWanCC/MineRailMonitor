using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public static class AlarmOperationRecordFactory
{
    public static OperationRecord CreateAcknowledged(PassageRecord passage, DateTimeOffset acknowledgedAt)
    {
        if (passage is null) throw new ArgumentNullException(nameof(passage));

        return new OperationRecord
        {
            Time = acknowledgedAt,
            OperationType = "确认报警",
            TargetType = "车辆",
            TargetName = GetTargetName(passage),
            BeforeValue = $"状态: 未确认；PassageId: {passage.PassageId:D}",
            AfterValue = "确认脱节报警",
            Result = "成功"
        };
    }

    public static OperationRecord CreateRecovered(PassageRecord passage, DateTimeOffset recoveredAt)
    {
        if (passage is null) throw new ArgumentNullException(nameof(passage));

        return new OperationRecord
        {
            Time = recoveredAt,
            OperationType = "报警恢复",
            TargetType = "车辆",
            TargetName = GetTargetName(passage),
            BeforeValue = $"状态: 已确认；PassageId: {passage.PassageId:D}",
            AfterValue = "状态: 已恢复",
            Result = "成功"
        };
    }

    private static string GetTargetName(PassageRecord passage) =>
        !string.IsNullOrWhiteSpace(passage.VehicleNo)
            ? passage.VehicleNo!
            : !string.IsNullOrWhiteSpace(passage.VehicleName)
                ? passage.VehicleName!
                : passage.PassageId.ToString("D");
}
