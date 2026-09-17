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
