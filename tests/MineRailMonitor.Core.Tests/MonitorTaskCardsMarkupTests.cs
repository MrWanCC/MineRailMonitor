namespace MineRailMonitor.Core.Tests;

public sealed class MonitorTaskCardsMarkupTests
{
    [Fact]
    public void Monitor_task_area_uses_transport_task_layout()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("实时运输任务", markup);
        Assert.Contains("矿井轨道运输 · RFID基站监控", markup);
        Assert.Contains("安全 · 高效 · 智能", markup);
        Assert.Contains("Height=\"44\"", markup);
        Assert.Contains("RfidTaskSummaryText", markup);
        Assert.DoesNotContain("搜索车号、任务名称", markup);
        Assert.DoesNotContain("RfidTaskSearchBox", markup);
        Assert.DoesNotContain("RfidTaskExpandButton", markup);
        Assert.DoesNotContain("TaskSearchTextBoxStyle", markup);
        Assert.Contains("RfidTaskScrollViewer", markup);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", markup);
        Assert.Contains("VerticalScrollBarVisibility=\"Disabled\"", markup);
        Assert.Contains("RfidTaskOfflineCountText", markup);
        Assert.Contains("<Setter Property=\"Width\" Value=\"205\" />", markup);
        Assert.Contains("<Setter Property=\"Height\" Value=\"110\" />", markup);
        Assert.Contains("Source=\"/MineRailMonitor;component/Assets/Icons/rfid-station.png\"", markup);
        Assert.Contains("Width=\"48\"", markup);
        Assert.Contains("Height=\"48\"", markup);
        Assert.Contains("RenderOptions.BitmapScalingMode=\"HighQuality\"", markup);
        Assert.Contains("Height=\"60\"", markup);
        Assert.Contains("Width=\"1\"", markup);
        Assert.Contains("Height=\"24\"", markup);
        Assert.Contains("Background=\"#1C4B62\"", markup);
        Assert.DoesNotContain("Data=\"M 14,15", markup);
        Assert.DoesNotContain("<Viewbox Width=\"48\" Height=\"48\"", markup);
        Assert.Contains("LinearGradientBrush", markup);
        Assert.Contains("<StackPanel Orientation=\"Horizontal\" />", markup);
        Assert.DoesNotContain("<WrapPanel />", markup);
        Assert.DoesNotContain("Text=\"&#xE7C0;\"", markup);
        Assert.Contains("Grid.Row=\"1\"", markup);
        Assert.Contains("Text=\"{Binding MapPointName}\"", markup);
        Assert.Contains("RfidStationBindingRules.FindBindings", code);
        Assert.Contains("MapPointName", code);
        Assert.Contains("未绑定地图点位", code);
        Assert.True(
            markup.IndexOf("Text=\"{Binding MapPointName}\"", StringComparison.Ordinal) <
            markup.IndexOf("Text=\"{Binding StationDisplayId}\"", StringComparison.Ordinal),
            "任务卡应优先显示地图点位名称，再显示 RFID 编号。");
        Assert.Contains("hasPassage", code);
        Assert.Contains("hasPassage ? state?.CurrentHeadRfid", code);
        Assert.Contains("isOffline || !hasPassage", code);
        Assert.Contains("ProgressVisibility", code);
        Assert.Contains(": \"--\"", code);
        Assert.Contains("<RowDefinition x:Name=\"RfidTaskRowDefinition\" Height=\"176\" />", markup);
        Assert.Contains("ApplyTaskCardFilter", code);
        Assert.Contains("TaskCategory", code);
        Assert.Contains("RfidTaskRunningCountText", code);
        Assert.Contains("RfidTaskPendingCountText", code);
        Assert.Contains("RfidTaskAlarmCountText", code);
        Assert.Contains("BorderBrush=\"{Binding BorderBrush}\"", markup);
        Assert.Contains("IsUncouplingAlarm(state)", code);
        Assert.Contains("state.VisualState == RfidStationVisualState.Alarm", code);
    }

    [Fact]
    public void Six_task_cards_expand_to_fill_the_current_viewport()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("<Setter Property=\"Width\" Value=\"205\" />", markup);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", markup);
        Assert.Contains("<StackPanel Orientation=\"Horizontal\" />", markup);
        Assert.Contains("Padding=\"8,4,0,0\"", markup);
        Assert.Contains("RfidTaskCardWidthConverter", markup);
        Assert.Contains("<MultiBinding Converter=\"{StaticResource RfidTaskCardWidthConverter}\">", markup);
        Assert.Contains("Path=\"ActualWidth\"", markup);
        Assert.Contains("Path=\"Tag\"", markup);
        Assert.Contains("private const int FilledCardLimit = 6;", File.ReadAllText(Locate("src", "MineRailMonitor", "Converters", "RfidTaskCardWidthConverter.cs")));
        Assert.Contains("x:Name=\"RfidTaskPaginationPanel\"", markup);
        Assert.Contains("x:Name=\"RfidTaskPreviousPageButton\"", markup);
        Assert.Contains("x:Name=\"RfidTaskNextPageButton\"", markup);
        Assert.Contains("Click=\"OnRfidTaskPreviousPageClick\"", markup);
        Assert.Contains("Click=\"OnRfidTaskNextPageClick\"", markup);
        Assert.Contains("RfidTaskPageSize", code);
        Assert.Contains("Skip(_rfidTaskPageIndex * RfidTaskPageSize)", code);
        Assert.Contains("Take(RfidTaskPageSize)", code);
        Assert.Contains("RfidTaskPaginationPanel.Visibility", code);
        Assert.Contains("RfidTaskItemsControl.Tag = cards.Length;", code);
        Assert.Contains("Text=\"车头\"", markup);
        Assert.Contains("Text=\"{Binding HeadRfid}\"", markup);
        Assert.Contains("TextAlignment=\"Left\"", markup);
        Assert.Contains("<ColumnDefinition Width=\"72\" />", markup);
        Assert.Contains("private const double ScrollViewerHorizontalPadding = 8;", File.ReadAllText(Locate("src", "MineRailMonitor", "Converters", "RfidTaskCardWidthConverter.cs")));
        Assert.DoesNotContain("Text=\"识别状态\"", markup);
    }

    private static string Locate(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = segments.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Unable to locate {Path.Combine(segments)} from the test directory.");
    }
}
