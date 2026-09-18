namespace MineRailMonitor.Core.Tests;

public sealed class HistoryPageMarkupTests
{
    [Fact]
    public void History_page_exposes_the_required_filters_pagination_and_popup_detail_action()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "HistoryPage.xaml"));

        Assert.Contains("FromDatePicker", markup, StringComparison.Ordinal);
        Assert.Contains("ToDatePicker", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBox x:Name=\"FromDate", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBox x:Name=\"ToDate", markup, StringComparison.Ordinal);
        Assert.Contains("StationFilter", markup, StringComparison.Ordinal);
        Assert.Contains("站场范围", markup, StringComparison.Ordinal);
        Assert.Contains("YardFilter", markup, StringComparison.Ordinal);
        Assert.Contains("HeadRfidFilter", markup, StringComparison.Ordinal);
        Assert.Contains("OutcomeFilter", markup, StringComparison.Ordinal);
        Assert.Contains("PreviousPageButton", markup, StringComparison.Ordinal);
        Assert.Contains("NextPageButton", markup, StringComparison.Ordinal);
        Assert.Contains("标准节数", markup, StringComparison.Ordinal);
        Assert.Contains("实际节数", markup, StringComparison.Ordinal);
        Assert.Contains("OnHistoryDetailsClick", markup, StringComparison.Ordinal);
        Assert.Contains("HistoryActionButtonStyle", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_initializes_sqlite_and_restores_pending_clear_before_polling()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("SqlitePassageRecordStore", markup, StringComparison.Ordinal);
        Assert.Contains("RestorePendingClear", markup, StringComparison.Ordinal);
        Assert.Contains("GetUnacknowledgedAlarms", markup, StringComparison.Ordinal);
        Assert.Contains("RestoreUnacknowledgedAlarms", markup, StringComparison.Ordinal);
        Assert.Contains("YardCommunicationManager", markup, StringComparison.Ordinal);
        Assert.Contains("GetDetails(passageId)", markup, StringComparison.Ordinal);
        Assert.Contains("MarkAlarmAcknowledged", markup, StringComparison.Ordinal);
        Assert.Contains("HasUnacknowledgedAlarm", markup, StringComparison.Ordinal);
        Assert.True(markup.IndexOf("RestorePendingClear", StringComparison.Ordinal) < markup.IndexOf("StartAllAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void History_page_data_grid_text_style_is_defined_in_shared_resources()
    {
        var sharedStyles = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Cards.xaml"));

        Assert.Contains("x:Key=\"DataGridTextElementStyle\"", sharedStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_data_grid_cells_keep_the_selected_row_readable_when_grid_loses_focus()
    {
        var sharedStyles = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Cards.xaml"));

        Assert.Contains("<Condition Property=\"Selector.IsSelectionActive\" Value=\"False\" />", sharedStyles, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"{StaticResource AccentButtonBrush}\" />", sharedStyles, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{StaticResource TextPrimaryBrush}\" />", sharedStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void History_page_station_filter_uses_stable_station_id_and_name()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "HistoryPage.xaml.cs"));

        Assert.Contains("Tag = station.StationId.Trim()", markup, StringComparison.Ordinal);
        Assert.Contains("Content = FormatStation(station.StationId)", markup, StringComparison.Ordinal);
        Assert.Contains("$\"{displayId} · {name}\"", markup, StringComparison.Ordinal);
        Assert.Contains("StationId = stationTag", markup, StringComparison.Ordinal);
        Assert.Contains("SetYardOptions", markup, StringComparison.Ordinal);
        Assert.Contains("RfidYardFilter", markup, StringComparison.Ordinal);
        Assert.Contains("RfidStationIdentity.GetDisplayId", markup, StringComparison.Ordinal);
        Assert.Contains("ResolveStationIds", markup, StringComparison.Ordinal);
        Assert.Contains("StationText = stationText", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("StationAddress = stationTag", markup, StringComparison.Ordinal);
        Assert.Contains("历史未识别基站", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Legacy / 历史未识别基站", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Legacy / 历史未识别基站", File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "HistoryPage.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("PassageDetailsDialog", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void History_page_uses_clickable_date_controls_with_full_day_query_bounds()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "HistoryPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "HistoryPage.xaml.cs"));

        Assert.Contains("HmiDatePickerStyle", markup, StringComparison.Ordinal);
        Assert.Contains("SelectedDateFormat=\"Short\"", markup, StringComparison.Ordinal);
        Assert.Contains("FromDatePicker.SelectedDate", code, StringComparison.Ordinal);
        Assert.Contains("ToDatePicker.SelectedDate", code, StringComparison.Ordinal);
        Assert.Contains("StartOfDay", code, StringComparison.Ordinal);
        Assert.Contains("AddSeconds(1)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Date_picker_uses_a_single_hmi_border_and_keeps_text_clear_of_the_calendar_button()
    {
        var styles = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Cards.xaml"));

        Assert.Contains("HmiDatePickerTextBoxStyle", styles, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource HmiDatePickerTextBoxStyle}\"", styles, StringComparison.Ordinal);
        Assert.Contains("Padding=\"8,0,38,0\"", styles, StringComparison.Ordinal);
        Assert.Contains("PART_ContentHost", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Date_picker_defers_opening_until_the_click_event_has_finished()
    {
        var historyCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "HistoryPage.xaml.cs"));
        var alarmCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "AlarmHistoryPage.xaml.cs"));

        Assert.Contains("Dispatcher.BeginInvoke", historyCode, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Input", historyCode, StringComparison.Ordinal);
        Assert.Contains("VisualTreeHelper.GetParent", historyCode, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke", alarmCode, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Input", alarmCode, StringComparison.Ordinal);
        Assert.Contains("VisualTreeHelper.GetParent", alarmCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Alarm_history_exposes_acknowledgement_and_recovery_status()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "AlarmHistoryPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "AlarmHistoryPage.xaml.cs"));
        var dialogMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "PassageDetailsDialog.xaml"));
        var dialogCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "PassageDetailsDialog.xaml.cs"));

        Assert.Contains("报警状态", markup, StringComparison.Ordinal);
        Assert.Contains("AcknowledgeButtonVisibility", markup, StringComparison.Ordinal);
        Assert.Contains("OnAlarmAcknowledgeClick", markup, StringComparison.Ordinal);
        Assert.Contains("AlarmStatusText", code, StringComparison.Ordinal);
        Assert.Contains("无需确认", code, StringComparison.Ordinal);
        Assert.Contains("AlarmAcknowledgedAt", code, StringComparison.Ordinal);
        Assert.Contains("AlarmRecoveredAt", code, StringComparison.Ordinal);
        Assert.Contains("AlarmStatusValue", dialogMarkup, StringComparison.Ordinal);
        Assert.Contains("AlarmAcknowledgedAtValue", dialogMarkup, StringComparison.Ordinal);
        Assert.Contains("AlarmRecoveredAtValue", dialogMarkup, StringComparison.Ordinal);
        Assert.Contains("AlarmAcknowledgedAt", dialogCode, StringComparison.Ordinal);
        Assert.Contains("AlarmRecoveredAt", dialogCode, StringComparison.Ordinal);
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
