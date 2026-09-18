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
    public void Rfid_statistics_page_exposes_filters_metrics_trend_distribution_and_records()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml.cs"));

        Assert.Contains("RFID统计", markup, StringComparison.Ordinal);
        Assert.Contains("TodayRangeButton", markup, StringComparison.Ordinal);
        Assert.Contains("Last7DaysButton", markup, StringComparison.Ordinal);
        Assert.Contains("Last30DaysButton", markup, StringComparison.Ordinal);
        Assert.Contains("StationFilter", markup, StringComparison.Ordinal);
        Assert.Contains("站场范围", markup, StringComparison.Ordinal);
        Assert.Contains("YardFilter", markup, StringComparison.Ordinal);
        Assert.Contains("OutcomeFilter", markup, StringComparison.Ordinal);
        Assert.Contains("HeadRfidFilter", markup, StringComparison.Ordinal);
        Assert.Contains("TrendCanvas", markup, StringComparison.Ordinal);
        Assert.Contains("RankingItemsControl", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ResultDistributionPanel", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ResultDonutPath", markup, StringComparison.Ordinal);
        Assert.Contains("RecentPassageGrid", markup, StringComparison.Ordinal);
        Assert.Contains("识别效率洞察", markup, StringComparison.Ordinal);
        Assert.Contains("StatisticsPageGrid", markup, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Stretch\"", markup, StringComparison.Ordinal);
        Assert.Contains("Height=\"*\" MinHeight=\"286\"", markup, StringComparison.Ordinal);
        Assert.Contains("Height=\"*\" MinHeight=\"252\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"StatisticsPageScrollViewer\"", markup, StringComparison.Ordinal);
        Assert.Contains("IPassageRecordStore", code, StringComparison.Ordinal);
        Assert.Contains("YardPassageFilter.BuildStatistics", code, StringComparison.Ordinal);
        Assert.Contains("SetRuntimeStates", code, StringComparison.Ordinal);
        Assert.Contains("SetYardOptions", code, StringComparison.Ordinal);
        Assert.Contains("RfidYardFilter", code, StringComparison.Ordinal);
        Assert.Contains("CompletedAt", code, StringComparison.Ordinal);
        Assert.Contains("RfidTrendAggregator.Build", code, StringComparison.Ordinal);
        Assert.Contains("_rangeDays", code, StringComparison.Ordinal);
        Assert.Contains("RfidStationIdentity.GetDisplayId", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Rfid_statistics_ranking_matches_reference_layout()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml.cs"));

        Assert.DoesNotContain("StatisticsDistributionIconGeometry", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalCountText", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("AlarmCountText", markup, StringComparison.Ordinal);
        Assert.Contains("Width=\"360\" VerticalAlignment=\"Stretch\"", markup, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"360\" />", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"基站通行排行\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RankingScopeText\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"排名\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"基站名称\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"通行次数\"", markup, StringComparison.Ordinal);
        Assert.Contains("TrainIconGeometry", markup, StringComparison.Ordinal);
        Assert.Contains("BooleanToVisibilityConverter", markup, StringComparison.Ordinal);
        Assert.Contains("IsTopRank", markup, StringComparison.Ordinal);
        Assert.Contains("RankingScopeText.Text", code, StringComparison.Ordinal);
        Assert.Contains("configuredStations", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_routes_statistics_and_configures_communication_page()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("RfidStatisticsPage", code, StringComparison.Ordinal);
        Assert.Contains("_statisticsPage.Refresh()", code, StringComparison.Ordinal);
        Assert.Contains("_statisticsPage?.SetRuntimeStates", code, StringComparison.Ordinal);
        Assert.Contains("_communicationPage.ConfigureStations", code, StringComparison.Ordinal);
        Assert.Contains("_historyPage.SetYardOptions", code, StringComparison.Ordinal);
        Assert.Contains("_alarmHistoryPage.SetYardOptions", code, StringComparison.Ordinal);
        Assert.Contains("_statisticsPage.SetYardOptions", code, StringComparison.Ordinal);
        Assert.Contains("_settingsPage?.SetSelectedYard", code, StringComparison.Ordinal);
        Assert.Contains("_communicationPage.SetStationStatuses", code, StringComparison.Ordinal);
        Assert.Contains("_communicationPage.SetDisplayScope", code, StringComparison.Ordinal);
        Assert.Contains("var selectedYardId = scope.IsGlobal ? null : scope.YardId;", code, StringComparison.Ordinal);
        Assert.Contains("SetDisplayScope(stationIds, selectedYardId)", code, StringComparison.Ordinal);
        Assert.Contains("FilterCurrentYardStates(GetAllRuntimeStates()).ToArray()", code, StringComparison.Ordinal);
        Assert.Contains("SendCommunicationTestAsync", code, StringComparison.Ordinal);
        Assert.Contains("page == \"Rfid\"", code, StringComparison.Ordinal);
        Assert.Contains("_statisticsPage?.Refresh()", code, StringComparison.Ordinal);
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
