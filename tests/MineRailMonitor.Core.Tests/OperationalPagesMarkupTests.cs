namespace MineRailMonitor.Core.Tests;

public sealed class OperationalPagesMarkupTests
{
    [Fact]
    public void Communication_page_exposes_station_test_controls_and_live_packet_panels()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml.cs"));

        Assert.Contains("通信测试", markup, StringComparison.Ordinal);
        Assert.Contains("站场范围", markup, StringComparison.Ordinal);
        Assert.Contains("YardFilter", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DisplayName}\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayMemberPath=\"DisplayName\"", markup, StringComparison.Ordinal);
        Assert.Contains("TestStationSelector", markup, StringComparison.Ordinal);
        Assert.Contains("发送读取", markup, StringComparison.Ordinal);
        Assert.Contains("发送清空", markup, StringComparison.Ordinal);
        Assert.Contains("StationStatusGrid", markup, StringComparison.Ordinal);
        Assert.Contains("CommunicationOverviewBar", markup, StringComparison.Ordinal);
        Assert.Contains("OverviewStationCountText", markup, StringComparison.Ordinal);
        Assert.Contains("OverviewOnlineCountText", markup, StringComparison.Ordinal);
        Assert.Contains("OverviewOfflineCountText", markup, StringComparison.Ordinal);
        Assert.Contains("OverviewTimeoutCountText", markup, StringComparison.Ordinal);
        Assert.Contains("SelectedDiagnosticCard", markup, StringComparison.Ordinal);
        Assert.Contains("RealtimeCommunicationLogCard", markup, StringComparison.Ordinal);
        Assert.Contains("CommunicationLogList", markup, StringComparison.Ordinal);
        Assert.Contains("TotalSentCountText", markup, StringComparison.Ordinal);
        Assert.Contains("TotalReceivedCountText", markup, StringComparison.Ordinal);
        Assert.Contains("TotalTimeoutCountText", markup, StringComparison.Ordinal);
        Assert.Contains("BlackBoxStatusText", markup, StringComparison.Ordinal);
        Assert.Contains("BlackBoxWrittenCountText", markup, StringComparison.Ordinal);
        Assert.Contains("OpenBlackBoxDirectoryButton", markup, StringComparison.Ordinal);
        Assert.Contains("SelectedDiagnosticCountsText", markup, StringComparison.Ordinal);
        Assert.Contains("ConsecutiveTimeoutCountText", markup, StringComparison.Ordinal);
        Assert.Contains("LatestRxText", markup, StringComparison.Ordinal);
        Assert.Contains("ConfigureStations", code, StringComparison.Ordinal);
        Assert.Contains("_sendTestAsync", code, StringComparison.Ordinal);
        Assert.Contains("ClearLog", code, StringComparison.Ordinal);
        Assert.Contains("MatchesStation", code, StringComparison.Ordinal);
        Assert.Contains("SetDisplayScope", code, StringComparison.Ordinal);
        Assert.Contains("SetYardOptions", code, StringComparison.Ordinal);
        Assert.Contains("RfidYardFilter", code, StringComparison.Ordinal);
        Assert.Contains("RfidStationIdentity.GetDisplayId", code, StringComparison.Ordinal);
        Assert.Contains("_displayScopeStationIds", code, StringComparison.Ordinal);
        Assert.Contains("GetVisibleStations", code, StringComparison.Ordinal);
        Assert.Contains("ResetScopePresentation", code, StringComparison.Ordinal);
        Assert.Contains("UpdateDiagnosticsOverview", code, StringComparison.Ordinal);
        Assert.Contains("UpdateSelectedDiagnostic", code, StringComparison.Ordinal);
        Assert.Contains("SetBlackBoxStatus", code, StringComparison.Ordinal);
        Assert.Contains("OpenBlackBoxDirectoryRequested", code, StringComparison.Ordinal);
        Assert.Contains("OnStationSelectionChanged", code, StringComparison.Ordinal);
        Assert.Contains("OnStationTestClick", code, StringComparison.Ordinal);
        Assert.Contains("RfidStationEndpointKey", code, StringComparison.Ordinal);
        Assert.DoesNotContain("item.StationAddress == station.ProtocolAddress", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Communication_page_exposes_external_alarm_send_and_receive_log_tab()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml.cs"));
        var mainWindowCode = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"CommunicationTabs\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TabHeaderPanel\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectedIndicator\"", markup, StringComparison.Ordinal);
        Assert.Contains("FocusVisualStyle", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"下位机通信\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"外部接口\"", markup, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"OnCommunicationTabChanged\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RfidTestControls\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalAlarmTab\"", markup, StringComparison.Ordinal);
        Assert.Contains("本机收发端点", markup, StringComparison.Ordinal);
        Assert.Contains("固定 UDP Socket（发送/接收共用）", code, StringComparison.Ordinal);
        Assert.Contains("RfidTestControls.Visibility", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalAlarmTxLogList\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalAlarmRxLogList\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"状态\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"车厢数\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"CRC\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"解析结果\"", markup, StringComparison.Ordinal);
        Assert.Contains("AddExternalAlarmSent", code, StringComparison.Ordinal);
        Assert.Contains("AddExternalAlarmReceived", code, StringComparison.Ordinal);
        Assert.Contains("_externalAlarmTxLogRows", code, StringComparison.Ordinal);
        Assert.Contains("_externalAlarmRxLogRows", code, StringComparison.Ordinal);
        Assert.Contains("ExternalAlarmStatusSendFailed", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("ExternalAlarmFrameReceived", mainWindowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Communication_page_uses_engineering_test_layout()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml.cs"));

        Assert.Contains("x:Name=\"CommunicationHeaderBar\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CommunicationStatusBar\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LatestTxCard\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LatestRxCard\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CurrentFrameCard\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FrameRfidSlotsItemsControl\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CommunicationStatsBar\"", markup, StringComparison.Ordinal);
        Assert.Contains("LatestTxHexText", markup, StringComparison.Ordinal);
        Assert.Contains("LatestRxHexText", markup, StringComparison.Ordinal);
        Assert.Contains("FrameParseResultText", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"最近平均响应\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"平均响应时间\"", markup, StringComparison.Ordinal);
        Assert.Contains("LatestTxHexText.Text", code, StringComparison.Ordinal);
        Assert.Contains("LatestRxHexText.Text", code, StringComparison.Ordinal);
        Assert.Contains("FrameRfidSlotsItemsControl.ItemsSource", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Communication_clear_command_is_guarded_by_admin_mode_and_confirmation()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml.cs"));
        var mainWindowCode = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"ClearTestButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"False\"", markup, StringComparison.Ordinal);
        Assert.Contains("进入管理员模式后可发送清空命令", markup, StringComparison.Ordinal);
        Assert.Contains("SetAdminMode", code, StringComparison.Ordinal);
        Assert.Contains("确认发送清空命令", code, StringComparison.Ordinal);
        Assert.Contains("RfidPollCommand.Clear", code, StringComparison.Ordinal);
        Assert.Contains("_communicationPage.SetAdminMode(_adminModeService.IsAdmin)", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("发送清空命令需要管理员模式", mainWindowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Communication_page_uses_actual_command_events_without_reconstructing_tx_from_counts()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml.cs"));
        var mainWindowCode = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("AddCommandSent", code, StringComparison.Ordinal);
        Assert.Contains("RfidPollCommand.Clear", code, StringComparison.Ordinal);
        Assert.Contains("$\"发送{commandText}命令\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_sentLogCounts", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetReadRequestHex", code, StringComparison.Ordinal);
        Assert.Contains("StationCommandSent", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("_communicationPage.AddCommandSent(station, command, sentAt)", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("DatagramSent", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("CreateTxBlackBoxRecord", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("CreateRxBlackBoxRecord", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("_rawPacketBlackBoxWriter.TryEnqueue", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("RawPacketBlackBoxRecordFactory", mainWindowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_disposes_manager_before_unsubscribing_black_box_events()
    {
        var mainWindowCode = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));
        var firstDispose = mainWindowCode.IndexOf("_yardCommunicationManager.Dispose();", StringComparison.Ordinal);
        var firstUnsubscribe = mainWindowCode.IndexOf(
            "_yardCommunicationManager.DatagramReceived -= OnYardDatagramReceived;",
            StringComparison.Ordinal);
        var lastDispose = mainWindowCode.LastIndexOf("_yardCommunicationManager.Dispose();", StringComparison.Ordinal);
        var lastUnsubscribe = mainWindowCode.LastIndexOf(
            "_yardCommunicationManager.DatagramReceived -= OnYardDatagramReceived;",
            StringComparison.Ordinal);

        Assert.True(firstDispose >= 0);
        Assert.True(firstUnsubscribe > firstDispose);
        Assert.True(lastDispose >= 0);
        Assert.True(lastUnsubscribe > lastDispose);
        Assert.Contains("_rawPacketBlackBoxWriter.Dispose();", mainWindowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceContext_stops_before_detaching_datagram_events()
    {
        var managerCode = File.ReadAllText(Locate(
            "src",
            "MineRailMonitor.Core",
            "Services",
            "YardCommunicationManager.cs"));
        var start = managerCode.IndexOf("private async Task ReplaceContextAsync", StringComparison.Ordinal);
        var end = managerCode.IndexOf(
            "private static bool AreConfigurationsEqual",
            start,
            StringComparison.Ordinal);
        var method = managerCode.Substring(start, end - start);

        var detachBusiness = method.IndexOf("DetachBusinessEvents(current)", StringComparison.Ordinal);
        var stop = method.IndexOf("await current.StopAsync()", StringComparison.Ordinal);
        var detachDatagram = method.IndexOf("DetachDatagramEvents(current)", StringComparison.Ordinal);
        var dispose = method.IndexOf("current.Dispose()", StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        Assert.DoesNotContain("DetachContext(current)", method, StringComparison.Ordinal);
        Assert.True(detachBusiness >= 0);
        Assert.True(stop > detachBusiness);
        Assert.True(detachDatagram > stop);
        Assert.True(dispose > detachDatagram);
    }

    [Fact]
    public void Stopped_context_rx_is_blackbox_only()
    {
        var mainWindowCode = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));
        var start = mainWindowCode.IndexOf("private void OnYardDatagramReceived", StringComparison.Ordinal);
        var end = mainWindowCode.IndexOf("private void OnYardDatagramSent", start, StringComparison.Ordinal);
        var method = mainWindowCode.Substring(start, end - start);

        var blackBox = method.IndexOf(
            "_rawPacketBlackBoxWriter.TryEnqueue(CreateRxBlackBoxRecord",
            StringComparison.Ordinal);
        var stoppedGuard = method.IndexOf("if (!context.IsRunning)", StringComparison.Ordinal);
        var response = method.IndexOf("context.RecordResponse", StringComparison.Ordinal);
        var parser = method.IndexOf("_rfidFrameParser.TryParse", StringComparison.Ordinal);
        var dispatcher = method.IndexOf("Dispatcher.BeginInvoke", StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        Assert.True(blackBox >= 0);
        Assert.True(stoppedGuard > blackBox);
        Assert.True(response > stoppedGuard);
        Assert.True(parser > stoppedGuard);
        Assert.True(dispatcher > stoppedGuard);
    }

    [Fact]
    public void Black_box_station_resolution_requires_protocol_address()
    {
        var factoryCode = File.ReadAllText(Locate(
            "src",
            "MineRailMonitor.Infrastructure",
            "BlackBox",
            "RawPacketBlackBoxRecordFactory.cs"));

        Assert.Contains("if (!protocolAddress.HasValue)", factoryCode, StringComparison.Ordinal);
        Assert.Contains("return null", factoryCode, StringComparison.Ordinal);
        Assert.Contains("station.ProtocolAddress == protocolAddress.Value", factoryCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Communication_timeout_logs_are_scoped_identified_and_deduplicated()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml.cs"));

        Assert.Contains("AddTimeoutCommunicationLogEntries", code, StringComparison.Ordinal);
        Assert.Contains("visibleStations.FirstOrDefault(station => MatchesStation(status, station))", code, StringComparison.Ordinal);
        Assert.Contains("status.LastErrorAt ?? status.LastSentAt", code, StringComparison.Ordinal);
        Assert.Contains("设备响应超时：", code, StringComparison.Ordinal);
        Assert.Contains("FormatEndpoint(endpoint)", code, StringComparison.Ordinal);
        Assert.Contains("var newTimeoutCount = status.TimeoutCount - previousTimeoutCount", code, StringComparison.Ordinal);
        Assert.Contains("_timeoutLogCounts[statusKey] = status.TimeoutCount", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Statistics_analysis_page_exposes_the_operations_dashboard_without_internal_codes()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml.cs"));

        Assert.Contains("统计分析", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TimeRangeSelector\"", markup, StringComparison.Ordinal);
        Assert.Contains("今日", markup, StringComparison.Ordinal);
        Assert.Contains("昨日", markup, StringComparison.Ordinal);
        Assert.Contains("最近7天", markup, StringComparison.Ordinal);
        Assert.Contains("最近30天", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UpdatedAtText\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"KpiMetricsItemsControl\"", markup, StringComparison.Ordinal);
        Assert.Contains("\"通行次数\"", code, StringComparison.Ordinal);
        Assert.Contains("\"正常通过\"", code, StringComparison.Ordinal);
        Assert.Contains("\"脱节报警\"", code, StringComparison.Ordinal);
        Assert.Contains("\"完整性未判定\"", code, StringComparison.Ordinal);
        Assert.Contains("\"报警率\"", code, StringComparison.Ordinal);
        Assert.Contains("\"平均通行时长\"", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TrendChartCanvas\"", markup, StringComparison.Ordinal);
        Assert.Contains("通行趋势", markup, StringComparison.Ordinal);
        Assert.Contains("告警状态概况", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AlarmStatusItemsControl\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RecentAlarmsItemsControl\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RankingTabs\"", markup, StringComparison.Ordinal);
        Assert.Contains("报警最多基站", markup, StringComparison.Ordinal);
        Assert.Contains("报警最多车辆", markup, StringComparison.Ordinal);
        Assert.Contains("通行最多车辆", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"最近通行记录\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"时间\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"基站\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"车辆编号\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"车辆名称\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"判定结果\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"实际/理论\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"报警状态\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"通行时长\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RecentExceptionsItemsControl\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"车辆编号\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"车辆名称\"", markup, StringComparison.Ordinal);
        Assert.Contains("未匹配车辆", code, StringComparison.Ordinal);
        Assert.Contains("Completed => \"正常通过\"", code, StringComparison.Ordinal);
        Assert.Contains("UncouplingAlarm => \"脱节报警\"", code, StringComparison.Ordinal);
        Assert.Contains("Unverified => \"完整性未判定\"", code, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(number)", code, StringComparison.Ordinal);
        Assert.Contains("return \"未匹配车辆\";", code, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(record.VehicleNo) ? \"—\"", code, StringComparison.Ordinal);
        Assert.Contains("StatisticsDashboardQueryService", code, StringComparison.Ordinal);
        Assert.Contains("StatisticsComparisonRange.ForPreset", code, StringComparison.Ordinal);
        Assert.Contains("includeDate ? \"MM-dd HH:mm:ss\" : \"HH:mm:ss\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"UncouplingAlarm\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Unverified\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"null\"", markup, StringComparison.Ordinal);
        Assert.Contains("StatisticsPageGrid", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"统计范围：全部站场\"", markup, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Stretch\"", markup, StringComparison.Ordinal);
        Assert.Contains("IStatisticsService", code, StringComparison.Ordinal);
        Assert.Contains("IPassageRecordStore", code, StringComparison.Ordinal);
        Assert.Contains("public void SetDisplayScope", code, StringComparison.Ordinal);
        Assert.Contains("FormatDisplayScope", code, StringComparison.Ordinal);
        Assert.Contains("AlarmRatePercent", code, StringComparison.Ordinal);
        Assert.Contains("AverageDurationSeconds", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Statistics_dashboard_places_trend_alarm_summary_and_rankings_in_primary_zone()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml.cs"));

        System.Xml.Linq.XNamespace presentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = System.Xml.Linq.XDocument.Parse(markup);
        var mainZone = document.Descendants(presentationNamespace + "Grid")
            .Single(element => (string?)element.Attribute(xamlNamespace + "Name") == "DashboardMainGrid");
        var primaryPanels = mainZone.Elements()
            .Select(element => (string?)element.Attribute(xamlNamespace + "Name"))
            .Where(name => name is not null)
            .ToArray();

        Assert.Equal(new[] { "TrendPanel", "AlarmSummaryColumn", "RankingPanel" }, primaryPanels);
        Assert.Contains(mainZone.Descendants(presentationNamespace + "Border"), element =>
            (string?)element.Attribute(xamlNamespace + "Name") == "AlarmSummaryPanel");
        Assert.Contains(mainZone.Descendants(presentationNamespace + "Border"), element =>
            (string?)element.Attribute(xamlNamespace + "Name") == "RecentAlarmsPanel");
        Assert.Contains("Text=\"总告警\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AlarmTotalText\"", markup, StringComparison.Ordinal);
        Assert.Contains("AlarmStatus.Unconfirmed => \"未确认\"", code, StringComparison.Ordinal);
        Assert.Contains("AlarmStatus.Acknowledged => \"已确认\"", code, StringComparison.Ordinal);
        Assert.Contains("AlarmStatus.Recovered => \"已恢复\"", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TrendPeakPeriodText\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TrendPeakPassageText\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TrendAlarmPeakText\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Statistics_dashboard_keeps_recent_limits_and_uses_compact_exception_list()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml"));
        var pageCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml.cs"));
        var queryCode = File.ReadAllText(Locate("src", "MineRailMonitor.Core", "Services", "StatisticsDashboardQueryService.cs"));

        Assert.Contains("x:Name=\"RecentPassagesGrid\"", markup, StringComparison.Ordinal);
        Assert.Contains("RecentPassageLimit = 10", queryCode, StringComparison.Ordinal);
        Assert.Contains("RecentExceptionDisplayLimit = 5", pageCode, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RecentExceptionsItemsControl\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RecentAlarmsItemsControl\"", markup, StringComparison.Ordinal);
        const string oneWayBarWidthBinding = "Value=\"{Binding BarWidth, Mode=OneWay}\"";
        Assert.Equal(2, markup.Split(new[] { oneWayBarWidthBinding }, StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Text=\"TOP5\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"UncouplingAlarm\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Unverified\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Statistics_dashboard_fills_tall_viewports_and_keeps_small_window_scroll_limits()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml"));
        System.Xml.Linq.XNamespace presentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = System.Xml.Linq.XDocument.Parse(markup);
        var scrollViewer = document.Descendants(presentationNamespace + "ScrollViewer")
            .Single(element => (string?)element.Attribute(xamlNamespace + "Name") == "StatisticsScrollViewer");
        var contentGrid = scrollViewer.Elements(presentationNamespace + "Grid").Single();
        var rowDefinitions = contentGrid.Element(presentationNamespace + "Grid.RowDefinitions")!
            .Elements(presentationNamespace + "RowDefinition")
            .Select(element => (string?)element.Attribute("Height"))
            .ToArray();
        var tabs = contentGrid.Elements(presentationNamespace + "TabControl").Single();
        var overviewTab = tabs.Elements(presentationNamespace + "TabItem")
            .Single(element => (string?)element.Attribute("Header") == "统计总览");
        var overviewGrid = overviewTab.Elements(presentationNamespace + "Grid").Single();
        var overviewRows = overviewGrid.Element(presentationNamespace + "Grid.RowDefinitions")!
            .Elements(presentationNamespace + "RowDefinition")
            .Select(element => (string?)element.Attribute("Height"))
            .ToArray();

        Assert.Equal("{Binding ViewportHeight, ElementName=StatisticsScrollViewer}",
            (string?)contentGrid.Attribute("MinHeight"));
        Assert.Equal(new[] { "Auto", "Auto", "*" }, rowDefinitions);
        Assert.Equal(new[] { "Auto", "1.2*", "1*" }, overviewRows);
        Assert.Contains(overviewGrid.Elements(), element =>
            (string?)element.Attribute(xamlNamespace + "Name") == "DashboardMainGrid");
        Assert.Contains(overviewGrid.Elements(), element =>
            (string?)element.Attribute(xamlNamespace + "Name") is null &&
            element.Name == presentationNamespace + "Grid" &&
            (string?)element.Attribute("MinHeight") == "240");
    }

    [Fact]
    public void Statistics_dashboard_empty_states_hide_empty_chart_and_table_chrome()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml.cs"));
        var mainWindow = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml"));
        var renderStart = code.IndexOf("private void RenderTrendChart()", StringComparison.Ordinal);
        var noDataGuard = code.IndexOf("_currentTrend.All(bucket => bucket.TotalCount == 0)", renderStart, StringComparison.Ordinal);
        var firstAxisLabel = code.IndexOf("AddChartText(\"0\"", renderStart, StringComparison.Ordinal);

        Assert.True(noDataGuard >= renderStart && noDataGuard < firstAxisLabel);
        Assert.Contains("RecentPassagesGrid.Visibility = _recentPassageItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;",
            code, StringComparison.Ordinal);
        Assert.Contains("var hasComparison = previous != 0 || current != 0;", code, StringComparison.Ordinal);
        Assert.Contains("hasComparison ? deltaBrush : MutedBrush", code, StringComparison.Ordinal);
        Assert.Contains("无对比数据", code, StringComparison.Ordinal);
        Assert.Contains("currentRate.HasValue && previousRate.HasValue", code, StringComparison.Ordinal);
        Assert.Contains("StatisticsEmptyStateTextStyle", markup, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{StaticResource TextSecondaryBrush}\" />", markup, StringComparison.Ordinal);
        Assert.Contains("TrendAlarmPeakText.Foreground = peakAlarm is null ? MutedBrush : AlarmBrush;",
            code, StringComparison.Ordinal);
        Assert.Contains("Text=\"暂无可排名数据\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"暂无通行记录\"", markup, StringComparison.Ordinal);
        Assert.Contains("<ContentControl x:Name=\"PageContent\" Grid.Column=\"1\" FocusVisualStyle=\"{x:Null}\" />",
            mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Statistics_dashboard_shows_station_tag_statistics_before_the_existing_overview()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml"));
        System.Xml.Linq.XNamespace presentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = System.Xml.Linq.XDocument.Parse(markup);
        var tabs = document.Descendants(presentationNamespace + "TabControl")
            .Single(element => (string?)element.Attribute(xamlNamespace + "Name") == "StatisticsPageTabs");
        var tabItems = tabs.Elements(presentationNamespace + "TabItem").ToArray();
        var qualityTab = tabItems[0];
        var visibleCopy = string.Join(" ", document.Descendants()
            .SelectMany(element => element.Attributes("Text"))
            .Concat(document.Descendants().SelectMany(element => element.Attributes("Header")))
            .Where(attribute => !attribute.Value.StartsWith("{Binding", StringComparison.Ordinal))
            .Select(attribute => attribute.Value));
        var tagGrid = tabItems[0].Descendants(presentationNamespace + "DataGrid")
            .Single(element => (string?)element.Attribute(xamlNamespace + "Name") == "StationTagStatisticsGrid");

        Assert.Equal(new[] { "基站标签统计", "统计总览" },
            tabItems.Select(element => (string?)element.Attribute("Header")));
        Assert.Equal(new[] { "车辆 / 标签类型", "出现通行趟数", "首次观察", "最近观察" },
            tagGrid.Element(presentationNamespace + "DataGrid.Columns")!
                .Elements(presentationNamespace + "DataGridTextColumn")
                .Select(element => (string?)element.Attribute("Header")));
        Assert.Contains(qualityTab.Descendants(presentationNamespace + "ItemsControl"), element =>
            (string?)element.Attribute(xamlNamespace + "Name") == "StationQualityKpisItemsControl");
        Assert.Contains(qualityTab.Descendants(presentationNamespace + "ListBox"), element =>
            (string?)element.Attribute(xamlNamespace + "Name") == "StationQualityListBox");
        Assert.Contains(qualityTab.Descendants(presentationNamespace + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "重点关注基站");
        Assert.Contains(qualityTab.Descendants(presentationNamespace + "ItemsControl"), element =>
            (string?)element.Attribute(xamlNamespace + "Name") == "StationProfileItemsControl");
        Assert.Contains(qualityTab.Descendants(presentationNamespace + "ItemsControl"), element =>
            (string?)element.Attribute(xamlNamespace + "Name") == "StationTagRankingItemsControl");
        Assert.Contains("统计口径 ⓘ", markup, StringComparison.Ordinal);
        Assert.Contains("同一趟中同一 RFID 仅计一次", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("效果示意", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Demo", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("演示", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Expected Count", visibleCopy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Detected Count", visibleCopy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Completeness", visibleCopy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(tabItems[1].Descendants(presentationNamespace + "ItemsControl"), element =>
            (string?)element.Attribute(xamlNamespace + "Name") == "KpiMetricsItemsControl");
    }

    [Fact]
    public void Statistics_analysis_reuses_the_existing_rfid_navigation_entry()
    {
        var navigation = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml"));

        Assert.Contains("Text=\"统计分析\"", navigation, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Rfid\"", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("Tag=\"Statistics\"", navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_routes_statistics_and_configures_communication_page()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("RfidStatisticsPage", code, StringComparison.Ordinal);
        Assert.Contains("new RfidStatisticsPage(_passageRecordStore, _passageRecordStore)", code, StringComparison.Ordinal);
        Assert.Contains("_statisticsPage.Refresh()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_statisticsPage?.SetRuntimeStates", code, StringComparison.Ordinal);
        Assert.Contains("_communicationPage.ConfigureStations", code, StringComparison.Ordinal);
        Assert.Contains("_historyPage.SetYardOptions", code, StringComparison.Ordinal);
        Assert.Contains("_alarmHistoryPage.SetYardOptions", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_statisticsPage.SetYardOptions", code, StringComparison.Ordinal);
        Assert.Contains("_settingsPage?.SetSelectedYard", code, StringComparison.Ordinal);
        Assert.Contains("_communicationPage.SetStationStatuses", code, StringComparison.Ordinal);
        Assert.Contains("_communicationPage.SetDisplayScope", code, StringComparison.Ordinal);
        Assert.Contains("var selectedYardId = scope.IsGlobal ? null : scope.YardId;", code, StringComparison.Ordinal);
        Assert.Contains("SetDisplayScope(stationIds, selectedYardId)", code, StringComparison.Ordinal);
        Assert.Contains("FilterCurrentYardStates(GetAllRuntimeStates()).ToArray()", code, StringComparison.Ordinal);
        Assert.Contains("SendCommunicationTestAsync", code, StringComparison.Ordinal);
        Assert.Contains("page == \"Rfid\"", code, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(PageContent.Content, _statisticsPage)", code, StringComparison.Ordinal);
        Assert.Contains("FindRemovedReferencedStationIds", code, StringComparison.Ordinal);
        Assert.Contains("请先解除地图绑定后再删除", code, StringComparison.Ordinal);
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
