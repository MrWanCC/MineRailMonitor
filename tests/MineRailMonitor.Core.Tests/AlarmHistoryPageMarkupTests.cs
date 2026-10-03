namespace MineRailMonitor.Core.Tests;

public sealed class AlarmHistoryPageMarkupTests
{
    [Fact]
    public void Alarm_page_exposes_alarm_filters_pagination_and_required_columns()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "AlarmHistoryPage.xaml"));

        Assert.Contains("FromDatePicker", markup, StringComparison.Ordinal);
        Assert.Contains("ToDatePicker", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBox x:Name=\"FromDate", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBox x:Name=\"ToDate", markup, StringComparison.Ordinal);
        Assert.Contains("StationFilter", markup, StringComparison.Ordinal);
        Assert.Contains("站场范围", markup, StringComparison.Ordinal);
        Assert.Contains("YardFilter", markup, StringComparison.Ordinal);
        Assert.Contains("HeadRfidFilter", markup, StringComparison.Ordinal);
        Assert.Contains("QueryButton", markup, StringComparison.Ordinal);
        Assert.Contains("ResetButton", markup, StringComparison.Ordinal);
        Assert.Contains("PreviousPageButton", markup, StringComparison.Ordinal);
        Assert.Contains("NextPageButton", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"时间\"", markup, StringComparison.Ordinal);
        Assert.Contains("车辆编号", markup, StringComparison.Ordinal);
        Assert.Contains("报警类型", markup, StringComparison.Ordinal);
        Assert.Contains("理论节数", markup, StringComparison.Ordinal);
        Assert.Contains("实际节数", markup, StringComparison.Ordinal);
        Assert.Contains("缺少节数", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"状态\"", markup, StringComparison.Ordinal);
        var detailsMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "PassageDetailsDialog.xaml"));
        var detailsCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "PassageDetailsDialog.xaml.cs"));
        Assert.Contains("确认报警", detailsMarkup, StringComparison.Ordinal);
        Assert.Contains("AlarmAcknowledgedAtValue", detailsMarkup, StringComparison.Ordinal);
        Assert.Contains("AlarmRecoveredAtValue", detailsMarkup, StringComparison.Ordinal);
        Assert.Contains("VehicleNoValue", detailsMarkup, StringComparison.Ordinal);
        Assert.Contains("VehicleNameValue", detailsMarkup, StringComparison.Ordinal);
        Assert.Contains("OnConfirmAlarmClick", detailsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Legacy / 历史未识别基站", File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "AlarmHistoryPage.xaml.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Alert_page_queries_warnings_and_uncoupling_alarms_and_calculates_missing_count()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "AlarmHistoryPage.xaml.cs"));

        Assert.Contains("IncludeWarnings = true", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Outcome = PassageOutcome.UncouplingAlarm", code, StringComparison.Ordinal);
        Assert.Contains("Math.Max", code, StringComparison.Ordinal);
        Assert.Contains("record.ExpectedVehicleCount.GetValueOrDefault() - record.DetectedVehicleCount", code, StringComparison.Ordinal);
        Assert.Contains("VehicleNoText", code, StringComparison.Ordinal);
        Assert.DoesNotContain("已处理", code, StringComparison.Ordinal);
        Assert.DoesNotContain("未处理", code, StringComparison.Ordinal);
        Assert.Contains("FromDatePicker.SelectedDate", code, StringComparison.Ordinal);
        Assert.Contains("ToDatePicker.SelectedDate", code, StringComparison.Ordinal);
        Assert.Contains("StartOfDay", code, StringComparison.Ordinal);
        Assert.Contains("SetYardOptions", code, StringComparison.Ordinal);
        Assert.Contains("RfidYardFilter", code, StringComparison.Ordinal);
        Assert.Contains("RfidStationIdentity.GetDisplayId", code, StringComparison.Ordinal);
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
