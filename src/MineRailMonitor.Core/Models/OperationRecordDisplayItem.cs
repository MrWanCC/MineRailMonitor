namespace MineRailMonitor.Core.Models;

public sealed class OperationRecordDisplayItem
{
    public Guid Id { get; init; }

    public string TimeText { get; init; } = string.Empty;

    public string OperatorName { get; init; } = string.Empty;

    public string OperationType { get; init; } = string.Empty;

    public string TargetType { get; init; } = string.Empty;

    public string TargetName { get; init; } = string.Empty;

    public string TargetText { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string? BeforeValue { get; init; }

    public string? AfterValue { get; init; }

    public string Result { get; init; } = string.Empty;
}
