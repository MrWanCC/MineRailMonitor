namespace MineRailMonitor.Core.Tests;

public sealed class AlarmHistoryPageMarkupTests
{
    [Fact]
    public void Alarm_page_exposes_alarm_filters_pagination_and_required_columns()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "AlarmHistoryPage.xaml"));

        Assert.Contains("FromDateTextBox", markup, StringComparison.Ordinal);
        Assert.Contains("ToDateTextBox", markup, StringComparison.Ordinal);
        Assert.Contains("StationFilter", markup, StringComparison.Ordinal);
        Assert.Contains("HeadRfidFilter", markup, StringComparison.Ordinal);
        Assert.Contains("QueryButton", markup, StringComparison.Ordinal);
        Assert.Contains("ResetButton", markup, StringComparison.Ordinal);
        Assert.Contains("PreviousPageButton", markup, StringComparison.Ordinal);
        Assert.Contains("NextPageButton", markup, StringComparison.Ordinal);
        Assert.Contains("报警时间", markup, StringComparison.Ordinal);
        Assert.Contains("识别进度", markup, StringComparison.Ordinal);
        Assert.Contains("缺少节数", markup, StringComparison.Ordinal);
        Assert.Contains("报警原因", markup, StringComparison.Ordinal);
        Assert.Contains("清除状态", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Alarm_page_queries_only_uncoupling_alarms_and_calculates_missing_count()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "AlarmHistoryPage.xaml.cs"));

        Assert.Contains("Outcome = PassageOutcome.UncouplingAlarm", code, StringComparison.Ordinal);
        Assert.Contains("Math.Max", code, StringComparison.Ordinal);
        Assert.Contains("record.ExpectedVehicleCount - record.DetectedVehicleCount", code, StringComparison.Ordinal);
        Assert.Contains("ToString(\"X4\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("已处理", code, StringComparison.Ordinal);
        Assert.DoesNotContain("未处理", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_routes_alarms_to_the_alarm_history_page()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("AlarmHistoryPage", code, StringComparison.Ordinal);
        Assert.Contains("_alarmHistoryPage.Refresh()", code, StringComparison.Ordinal);
        Assert.Contains("page == \"Alarms\"", code, StringComparison.Ordinal);
    }

    private static string Locate(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar.ToString(), parts));
    }
}
