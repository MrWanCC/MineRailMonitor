namespace MineRailMonitor.Core.Tests;

public sealed class SystemStatusMarkupTests
{
    [Fact]
    public void Sidebar_places_yard_selection_above_page_navigation_without_global_overview()
    {
        var mainWindow = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml"));
        var yardSelectionIndex = mainWindow.IndexOf("Text=\"站场选择\"", StringComparison.Ordinal);
        var monitorNavigationIndex = mainWindow.IndexOf("Text=\"实时监控\"", StringComparison.Ordinal);
        var stationButtonsIndex = mainWindow.IndexOf("x:Name=\"StationButtonsPanel\"", StringComparison.Ordinal);

        Assert.True(yardSelectionIndex >= 0);
        Assert.True(stationButtonsIndex > yardSelectionIndex);
        Assert.True(monitorNavigationIndex > stationButtonsIndex);
        Assert.DoesNotContain("Tag=\"Overview\"", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"全局总览\"", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void System_status_card_has_room_for_its_content_and_is_top_aligned()
    {
        var monitor = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));

        Assert.Contains("<RowDefinition Height=\"*\" />", monitor);
        Assert.Contains("x:Name=\"SystemStatusCard\"", monitor);
        Assert.Contains("MinHeight=\"150\"", monitor);
        Assert.Contains("x:Name=\"SystemStatusContent\"", monitor);
        Assert.Contains("Visibility=\"Collapsed\"", monitor.Substring(monitor.IndexOf("x:Name=\"SystemStatusCard\"", StringComparison.Ordinal)));
        Assert.Contains("VerticalAlignment=\"Top\"", monitor);
    }

    [Fact]
    public void System_status_indicators_are_named_and_updated_from_runtime_health()
    {
        var monitor = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var mainWindow = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml"));
        var mainWindowCode = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("x:Name=\"HeaderSystemStatusDot\"", mainWindow);
        Assert.Contains("x:Name=\"HeaderRfidStatusDot\"", mainWindow);
        Assert.Contains("x:Name=\"HeaderExternalStatusDot\"", mainWindow);
        Assert.DoesNotContain("Text=\"系统正常\"", mainWindow);
        Assert.DoesNotContain("Text=\"外部接口正常\"", mainWindow);
        Assert.Contains("SystemDatabaseStatusDot", monitor);
        Assert.Contains("SystemRfidStatusDot", monitor);
        Assert.Contains("SystemExternalStatusDot", monitor);
        Assert.Contains("UpdateHeaderStatusIndicators", mainWindowCode);
        Assert.Contains("SetSystemRfidStatus", monitorCode);
    }

    [Fact]
    public void Monitor_layout_keeps_the_map_primary_and_details_compact()
    {
        var mainWindow = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml"));
        var monitor = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var cards = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Cards.xaml"));
        var colors = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Colors.xaml"));
        var pointLayer = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapPointLayer.xaml.cs"));
        var labelLayer = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapLabelLayer.xaml.cs"));

        Assert.Contains("<ColumnDefinition Width=\"200\" />", mainWindow);
        Assert.Contains("<RowDefinition Height=\"0\" />", mainWindow);
        Assert.Contains("Visibility=\"Collapsed\"", mainWindow);
        Assert.Contains("<RowDefinition x:Name=\"RfidTaskRowDefinition\" Height=\"176\" />", monitor);
        Assert.Contains("<ColumnDefinition Width=\"380\" />", monitor);
        Assert.Contains("x:Name=\"SelectedDeviceLatestRfid\"", monitor);
        Assert.Contains("x:Name=\"SelectedDeviceLastCommunication\"", monitor);
        Assert.Contains("x:Name=\"RecognitionSnapshotText\"", monitor);
        Assert.Contains(
            "Visibility=\"Collapsed\"",
            monitor.Substring(
                monitor.IndexOf("x:Name=\"RecognitionSnapshotText\"", StringComparison.Ordinal),
                monitor.IndexOf("/>", monitor.IndexOf("x:Name=\"RecognitionSnapshotText\"", StringComparison.Ordinal), StringComparison.Ordinal) -
                monitor.IndexOf("x:Name=\"RecognitionSnapshotText\"", StringComparison.Ordinal) + 2));
        Assert.Contains("BorderThickness\" Value=\"0\"", cards);
        Assert.Contains("MapPointMarkerBrush", colors);
        Assert.Contains("MapPointMarkerBrush", pointLayer);
        Assert.Contains("GetFontSize(label.TextHeight) - 2", labelLayer);
    }

    [Fact]
    public void Monitor_bottom_tasks_stay_left_of_the_right_alarm_rail()
    {
        var monitor = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));

        Assert.Contains("<Grid.ColumnDefinitions>\n            <ColumnDefinition Width=\"*\" />\n            <ColumnDefinition Width=\"380\" />\n        </Grid.ColumnDefinitions>", monitor);
        Assert.Contains("<Grid Grid.Row=\"0\" Grid.ColumnSpan=\"2\" Margin=\"0,0,0,8\">", monitor);
        Assert.Contains("<Border Grid.Row=\"1\" Grid.Column=\"0\"", monitor);
        Assert.Contains("Margin=\"0,0,10,0\"", monitor);
    }

    [Fact]
    public void Monitor_right_rail_contains_overview_detail_and_alarm_sections()
    {
        var monitorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("<ColumnDefinition Width=\"380\" />", monitorMarkup);
        Assert.Contains("x:Name=\"RfidOverviewCard\"", monitorMarkup);
        Assert.Contains("Text=\"RFID运行总览\"", monitorMarkup);
        Assert.Contains("x:Name=\"RfidStationStatusCard\"", monitorMarkup);
        Assert.DoesNotContain("x:Name=\"RfidRecognitionCard\"", monitorMarkup);
        Assert.DoesNotContain("Text=\"RFID识别\"", monitorMarkup);
        Assert.DoesNotContain("x:Name=\"SelectedRfidSequenceList\"", monitorMarkup);
        Assert.Contains("Text=\"未选择基站\"", monitorMarkup);
        Assert.Contains("ObservedVehicleSequence", monitorCode);
        Assert.DoesNotContain("SelectedRfidLatestText", monitorCode);
        Assert.DoesNotContain("Text=\"列车信息\"", monitorMarkup);

        var systemStatusIndex = monitorMarkup.IndexOf("x:Name=\"SystemStatusCard\"", StringComparison.Ordinal);
        Assert.True(systemStatusIndex >= 0);
        Assert.Contains("Visibility=\"Collapsed\"", monitorMarkup.Substring(systemStatusIndex));
    }

    [Fact]
    public void Monitor_page_uses_runtime_overview_task_cards_and_alarm_only_panel()
    {
        var monitorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("Text=\"RFID运行总览\"", monitorMarkup);
        Assert.Contains("x:Name=\"RfidOverviewCard\"", monitorMarkup);
        Assert.DoesNotContain("x:Name=\"RfidOverviewTabButton\"", monitorMarkup);
        Assert.DoesNotContain("x:Name=\"RfidDetailTabButton\"", monitorMarkup);
        Assert.Contains("x:Name=\"RfidTaskItemsControl\"", monitorMarkup);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", monitorMarkup);
        Assert.Contains("Value=\"{Binding ProgressValue, Mode=OneWay}\"", monitorMarkup);
        Assert.Contains("x:Name=\"RfidAlarmItemsControl\"", monitorMarkup);
        Assert.Contains("Text=\"报警信息\"", monitorMarkup);
        Assert.Contains("Text=\"连接状态\"", monitorMarkup);
        Assert.Contains("Text=\"RFID基站详情\"", monitorMarkup);
        Assert.DoesNotContain("待清空", monitorMarkup);
        Assert.DoesNotContain("Text=\"列车编号\"", monitorMarkup);
        Assert.Contains("RfidTaskItemsControl.ItemsSource", monitorCode);
        Assert.Contains("RfidAlarmItemsControl.ItemsSource", monitorCode);
        Assert.Contains("RfidStationIdentity.GetDisplayId", monitorCode);
        Assert.Contains("StationDisplayId", monitorMarkup);
    }

    [Fact]
    public void Monitor_right_rail_matches_the_overview_detail_and_alarm_layout()
    {
        var monitorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("Text=\"RFID运行总览\"", monitorMarkup);
        Assert.DoesNotContain("x:Name=\"RfidOverviewTabButton\"", monitorMarkup);
        Assert.DoesNotContain("x:Name=\"RfidDetailTabButton\"", monitorMarkup);
        Assert.Contains("Columns=\"2\" Rows=\"2\"", monitorMarkup);
        Assert.DoesNotContain("x:Name=\"OverviewRecognizingCountText\"", monitorMarkup);
        Assert.DoesNotContain("x:Name=\"OverviewClearingCountText\"", monitorMarkup);
        Assert.Contains("Text=\"连接状态\"", monitorMarkup);
        Assert.Contains("Text=\"RFID基站详情\"", monitorMarkup);
        Assert.Contains("x:Name=\"RfidOverviewContent\"", monitorMarkup);
        Assert.Contains("x:Name=\"RfidDetailView\"", monitorMarkup);
        Assert.Contains("x:Name=\"RfidAlarmItemsControl\"", monitorMarkup);
        Assert.Contains("x:Name=\"RfidAlarmEmptyText\"", monitorMarkup);
        Assert.Contains("Text=\"{Binding AlarmDescription}\"", monitorMarkup);
        Assert.DoesNotContain("Text=\"RFID实时识别\"", monitorMarkup);
        Assert.DoesNotContain("Text=\"{Binding Status}\"", monitorMarkup.Substring(monitorMarkup.IndexOf("x:Name=\"RfidAlarmItemsControl\"", StringComparison.Ordinal)));
        Assert.Contains("AlarmDescription", monitorCode);
        Assert.Contains("RfidAlarmEmptyText.Visibility", monitorCode);
    }

    [Fact]
    public void Monitor_alarm_panel_exposes_history_and_a_working_more_action()
    {
        var monitorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));
        var mainWindowCode = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"RfidAlarmMoreButton\"", monitorMarkup);
        Assert.Contains("Click=\"OnRfidAlarmMoreClick\"", monitorMarkup);
        Assert.Contains("AlarmMoreRequested", monitorCode);
        Assert.Contains("SetRecentAlarmRecords", monitorCode);
        Assert.Contains("SetRecentAlarmRecords", mainWindowCode);
        Assert.Contains("AlarmMoreRequested", mainWindowCode);
        Assert.Contains("PassageOutcome.UncouplingAlarm", mainWindowCode);
    }

    [Fact]
    public void Selected_rfid_detail_is_rendered_as_an_independent_card()
    {
        var monitorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("<Border x:Name=\"RfidDetailView\"", monitorMarkup);
        Assert.Contains("Margin=\"0,318,0,8\"", monitorMarkup);
        Assert.Contains("Style=\"{StaticResource RightInfoCardStyle}\"", monitorMarkup);
        Assert.Contains("<RowDefinition Height=\"42\" />", monitorMarkup);
        Assert.Contains("x:Name=\"RfidDetailStationBadge\"", monitorMarkup);
        Assert.Contains("x:Name=\"RfidDetailStationBadgeText\"", monitorMarkup);
        Assert.Contains("RfidDetailView.Margin = new Thickness(0, 318, 0, 8);", monitorCode);
        Assert.Contains("RfidDetailStationBadgeText.Text = displayStationId;", monitorCode);
        Assert.Contains("RfidDetailStationBadge.Visibility = Visibility.Collapsed;", monitorCode);
    }

    [Fact]
    public void Selecting_a_station_replaces_the_empty_state_in_place()
    {
        var monitorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("x:Name=\"RfidOverviewContent\"", monitorMarkup);
        Assert.Contains("x:Name=\"RfidDetailView\"", monitorMarkup);
        Assert.Contains("RfidOverviewContent.Visibility = Visibility.Visible;", monitorCode);
        Assert.Contains("OverviewEmptyState.Visibility = hasRfidSelection ? Visibility.Collapsed : Visibility.Visible;", monitorCode);
        Assert.Contains("RfidDetailView.Visibility = hasRfidSelection ? Visibility.Visible : Visibility.Collapsed;", monitorCode);
        var selectionStart = monitorCode.IndexOf("private void UpdateRfidSelectionLayout", StringComparison.Ordinal);
        var selectionEnd = monitorCode.IndexOf("private void ApplyDashboardSnapshot", selectionStart, StringComparison.Ordinal);
        Assert.True(selectionStart >= 0 && selectionEnd > selectionStart, "RFID selection layout method is missing.");
        Assert.DoesNotContain("RfidOverviewCard.Visibility", monitorCode.Substring(selectionStart, selectionEnd - selectionStart));
        Assert.Contains("Margin=\"0,318,0,8\"", monitorMarkup);
        Assert.Contains("Text=\"RFID基站详情\"", monitorMarkup);
        Assert.DoesNotContain("UpdateRfidTabStyles", monitorCode);
        Assert.Contains("ApplySelectedRfidRuntimeState();", monitorCode);
    }

    [Fact]
    public void Selected_rfid_details_fill_the_inline_detail_region()
    {
        var monitorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));

        Assert.Contains("x:Name=\"SelectedDeviceInfo\"", monitorMarkup);
        Assert.Contains("x:Name=\"RfidStationStatusCard\" Grid.Row=\"1\"", monitorMarkup);
        Assert.DoesNotContain("x:Name=\"SelectedRfidRecognitionSummaryGrid\"", monitorMarkup);
        Assert.Contains("x:Name=\"SelectedRfidValidCountText\"", monitorMarkup);
        Assert.Contains("Text=\"最新RFID\"", monitorMarkup);
        Assert.Contains("Text=\"有效RFID\"", monitorMarkup);
        Assert.Contains("FontFamily=\"Consolas\"", monitorMarkup);

        var detailStart = monitorMarkup.IndexOf("x:Name=\"RfidDetailView\"", StringComparison.Ordinal);
        var detailEnd = monitorMarkup.IndexOf("x:Name=\"SystemStatusCard\"", detailStart, StringComparison.Ordinal);
        var detailMarkup = monitorMarkup.Substring(detailStart, detailEnd - detailStart);
        Assert.Contains("<RowDefinition Height=\"42\" />", detailMarkup);
        Assert.Contains("<RowDefinition Height=\"*\" />", detailMarkup);
    }

    [Fact]
    public void Monitor_alarm_panel_is_raised_to_reduce_empty_detail_space()
    {
        var monitorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));

        var rightRailStart = monitorMarkup.IndexOf("Grid.Row=\"0\" Grid.Column=\"1\" Grid.RowSpan=\"2\"", StringComparison.Ordinal);
        var rightRailEnd = monitorMarkup.IndexOf("x:Name=\"RfidOverviewCard\"", rightRailStart, StringComparison.Ordinal);
        var rightRailMarkup = monitorMarkup.Substring(rightRailStart, rightRailEnd - rightRailStart);
        Assert.Contains("<RowDefinition Height=\"330\" />", rightRailMarkup);
    }

    [Fact]
    public void Monitor_task_cards_have_a_clear_status_accent_and_visual_hierarchy()
    {
        var monitorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));

        Assert.Contains("<Style x:Key=\"TaskCardStationIdStyle\"", monitorMarkup);
        Assert.Contains("<Style x:Key=\"TaskCardLabelStyle\"", monitorMarkup);
        Assert.Contains("<Style x:Key=\"TaskCardValueStyle\"", monitorMarkup);
        Assert.Contains("<Style x:Key=\"TaskCardStationNameStyle\"", monitorMarkup);
        Assert.Contains("<Style x:Key=\"TaskCardPercentageStyle\"", monitorMarkup);
        Assert.Contains("<Style x:Key=\"TaskCardStatusBadgeStyle\"", monitorMarkup);
        Assert.Contains("Text=\"{Binding StationDisplayId}\" Style=\"{StaticResource TaskCardStationIdStyle}\"", monitorMarkup);
        Assert.Contains("BorderBrush=\"{Binding StatusForeground}\"", monitorMarkup);
        Assert.Contains("Text=\"{Binding HeadRfid}\"", monitorMarkup);
        Assert.Contains("Text=\"实时运输任务\"", monitorMarkup);
        Assert.Contains("Source=\"/MineRailMonitor;component/Assets/Icons/rfid-station.png\"", monitorMarkup);
        Assert.Contains("<Setter Property=\"Width\" Value=\"205\" />", monitorMarkup);
        Assert.Contains("<Setter Property=\"Height\" Value=\"110\" />", monitorMarkup);
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

        throw new FileNotFoundException($"Unable to locate {Path.Combine(parts)}.");
    }
}
