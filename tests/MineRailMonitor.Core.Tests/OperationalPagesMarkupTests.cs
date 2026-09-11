namespace MineRailMonitor.Core.Tests;

public sealed class OperationalPagesMarkupTests
{
    [Fact]
    public void Communication_page_exposes_station_test_controls_and_live_packet_panels()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml.cs"));

        Assert.Contains("通信测试", markup, StringComparison.Ordinal);
        Assert.Contains("TestStationSelector", markup, StringComparison.Ordinal);
        Assert.Contains("发送读取", markup, StringComparison.Ordinal);
        Assert.Contains("发送清空", markup, StringComparison.Ordinal);
        Assert.Contains("StationStatusGrid", markup, StringComparison.Ordinal);
        Assert.Contains("LatestRxText", markup, StringComparison.Ordinal);
        Assert.Contains("ConfigureStations", code, StringComparison.Ordinal);
        Assert.Contains("_sendTestAsync", code, StringComparison.Ordinal);
        Assert.Contains("ClearLog", code, StringComparison.Ordinal);
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
        Assert.Contains("OutcomeFilter", markup, StringComparison.Ordinal);
        Assert.Contains("HeadRfidFilter", markup, StringComparison.Ordinal);
        Assert.Contains("TrendCanvas", markup, StringComparison.Ordinal);
        Assert.Contains("ResultDonutPath", markup, StringComparison.Ordinal);
        Assert.Contains("RankingItemsControl", markup, StringComparison.Ordinal);
        Assert.Contains("RecentPassageGrid", markup, StringComparison.Ordinal);
        Assert.Contains("识别效率洞察", markup, StringComparison.Ordinal);
        Assert.Contains("StatisticsPageGrid", markup, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Stretch\"", markup, StringComparison.Ordinal);
        Assert.Contains("Height=\"*\" MinHeight=\"286\"", markup, StringComparison.Ordinal);
        Assert.Contains("Height=\"*\" MinHeight=\"252\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"StatisticsPageScrollViewer\"", markup, StringComparison.Ordinal);
        Assert.Contains("IPassageRecordStore", code, StringComparison.Ordinal);
        Assert.Contains("GetStatistics", code, StringComparison.Ordinal);
        Assert.Contains("SetRuntimeStates", code, StringComparison.Ordinal);
        Assert.Contains("CompletedAt", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_routes_statistics_and_configures_communication_page()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("RfidStatisticsPage", code, StringComparison.Ordinal);
        Assert.Contains("_statisticsPage.Refresh()", code, StringComparison.Ordinal);
        Assert.Contains("_statisticsPage?.SetRuntimeStates", code, StringComparison.Ordinal);
        Assert.Contains("_communicationPage.ConfigureStations", code, StringComparison.Ordinal);
        Assert.Contains("_communicationPage.SetStationStatuses", code, StringComparison.Ordinal);
        Assert.Contains("SendCommunicationTestAsync", code, StringComparison.Ordinal);
        Assert.Contains("page == \"Rfid\"", code, StringComparison.Ordinal);
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
