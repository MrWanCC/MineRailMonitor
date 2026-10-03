namespace MineRailMonitor.Core.Models;

public sealed class OperationRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset Time { get; init; } = DateTimeOffset.Now;

    public string OperatorName { get; init; } = "管理员";

    public string OperationType { get; init; } = string.Empty;

    public string TargetType { get; init; } = string.Empty;

    public string TargetName { get; init; } = string.Empty;

    public string? BeforeValue { get; init; }

    public string? AfterValue { get; init; }

    public string Result { get; init; } = "成功";
}
