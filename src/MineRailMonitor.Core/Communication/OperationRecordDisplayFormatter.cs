using System.Globalization;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Communication;

public static class OperationRecordDisplayFormatter
{
    public static OperationRecordDisplayItem Format(OperationRecord record)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));

        var before = Compact(record.BeforeValue);
        var after = Compact(record.AfterValue);
        var content = (before, after) switch
        {
            ("", "") => "无变更详情",
            ("", _) => $"新增：{after}",
            (_, "") => $"删除：{before}",
            _ => $"修改前：{before}；修改后：{after}"
        };

        return new OperationRecordDisplayItem
        {
            Id = record.Id,
            TimeText = record.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            OperatorName = record.OperatorName,
            OperationType = record.OperationType,
            TargetType = record.TargetType,
            TargetName = record.TargetName,
            TargetText = string.IsNullOrWhiteSpace(record.TargetType)
                ? record.TargetName
                : $"{record.TargetType}：{record.TargetName}",
            Content = content,
            BeforeValue = record.BeforeValue,
            AfterValue = record.AfterValue,
            Result = record.Result
        };
    }

    private static string Compact(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : string.Join("；", value!.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim()));
}
