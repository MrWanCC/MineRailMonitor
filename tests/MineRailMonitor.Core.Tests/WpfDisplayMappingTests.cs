using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class WpfDisplayMappingTests
{
    [Theory]
    [InlineData("ADMIN_LOGIN", "管理员登录")]
    [InlineData("ADMIN_LOGOUT", "管理员退出")]
    [InlineData("COMMUNICATION_CONFIG_CHANGED", "通信配置变更")]
    [InlineData("DATABASE_BACKUP", "数据库备份")]
    [InlineData("DATABASE_BACKUP_FAILED", "数据库备份失败")]
    public void Operation_record_type_displays_chinese_label(string operationType, string expected)
    {
        var display = OperationRecordDisplayFormatter.Format(new OperationRecord
        {
            OperationType = operationType
        });

        Assert.Equal(expected, display.OperationType);
    }

    [Fact]
    public void Existing_chinese_operation_type_is_preserved()
    {
        var display = OperationRecordDisplayFormatter.Format(new OperationRecord
        {
            OperationType = "修改车辆配置"
        });

        Assert.Equal("修改车辆配置", display.OperationType);
    }

    [Theory]
    [InlineData(AlarmStatus.Unconfirmed, "未确认")]
    [InlineData(AlarmStatus.Acknowledged, "已确认")]
    [InlineData(AlarmStatus.Recovered, "已恢复")]
    public void Alarm_status_displays_chinese_label(AlarmStatus status, string expected)
    {
        Assert.Equal(expected, AlarmStatusDisplayFormatter.Format(status));
    }

    [Fact]
    public void Communication_diagnostic_does_not_show_internal_lifecycle_enum_names()
    {
        var source = ReadSource("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml.cs");
        var method = ExtractMethod(source, "private static string FormatRuntimeState", "private sealed class ExternalAlarmRxLogRow");

        Assert.DoesNotContain("\"Idle ", method, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Recognizing ", method, StringComparison.Ordinal);
        Assert.DoesNotContain("\"WaitForEmpty", method, StringComparison.Ordinal);
        Assert.DoesNotContain("state.ToString()", method, StringComparison.Ordinal);
        Assert.Contains("空闲", method, StringComparison.Ordinal);
        Assert.Contains("等待清空", method, StringComparison.Ordinal);
    }

    [Fact]
    public void User_facing_field_labels_do_not_expose_internal_id_property_names()
    {
        var monitor = ReadSource("src", "MineRailMonitor", "Pages", "MonitorPage.xaml");
        var runLog = ReadSource("src", "MineRailMonitor", "Pages", "RunLogPage.xaml");
        var passageDetails = ReadSource("src", "MineRailMonitor", "Pages", "PassageDetailsDialog.xaml");

        Assert.Contains("Text=\"基站编号\"", monitor, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"StationId\"", monitor, StringComparison.Ordinal);
        Assert.Contains("Text=\"基站编号\"", runLog, StringComparison.Ordinal);
        Assert.Contains("Header=\"基站编号\"", runLog, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"StationId\"", runLog, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"StationId\"", runLog, StringComparison.Ordinal);
        Assert.Contains("Text=\"通行编号\"", passageDetails, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"PassageId\"", passageDetails, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method marker not found: {startMarker}");
        Assert.True(end > start, $"End marker not found: {endMarker}");
        return source.Substring(start, end - start);
    }

    private static string ReadSource(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = segments.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate source file: {Path.Combine(segments)}");
    }
}
