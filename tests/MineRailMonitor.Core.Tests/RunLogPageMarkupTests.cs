using System.Xml.Linq;

namespace MineRailMonitor.Core.Tests;

public sealed class RunLogPageMarkupTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Run_log_page_exposes_the_four_tabs_grids_and_empty_states()
    {
        var pagePath = LocateSourceFile("src", "MineRailMonitor", "Pages", "RunLogPage.xaml");
        Assert.True(File.Exists(pagePath), "RunLogPage.xaml should be part of the WPF application.");
        if (!File.Exists(pagePath))
        {
            return;
        }

        var markup = XDocument.Load(pagePath);
        var tabControl = markup.Descendants(Presentation + "TabControl").Single();
        Assert.Equal("0", (string?)tabControl.Attribute("SelectedIndex"));
        Assert.Equal(
            new[] { "车辆事件", "基站状态", "系统日志", "操作记录" },
            tabControl.Elements(Presentation + "TabItem")
                .Select(item => (string?)item.Attribute("Header"))
                .ToArray());

        Assert.Equal(
            new[] { "时间", "事件类型", "车辆编号", "事件内容", "状态" },
            GetGridHeaders(markup, "EventGrid"));
        Assert.Equal(
            new[] { "基站名称", "StationId", "IP地址", "端口", "状态", "最后接收时间" },
            GetGridHeaders(markup, "CommunicationGrid"));
        Assert.Equal(
            new[] { "时间", "操作类型", "对象", "内容", "结果" },
            GetGridHeaders(markup, "OperationGrid"));

        var allText = string.Join(" ", markup.Descendants(Presentation + "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .Where(text => text is not null));
        Assert.Contains("当前状态", allText, StringComparison.Ordinal);
        Assert.Contains("车辆名称", allText, StringComparison.Ordinal);
        Assert.Contains("VehicleNo", markup.ToString(), StringComparison.Ordinal);
        Assert.Contains("车头标签", allText, StringComparison.Ordinal);
        Assert.Contains("理论节数", allText, StringComparison.Ordinal);
        Assert.Contains("实际节数", allText, StringComparison.Ordinal);
        Assert.Contains("缺少节数", allText, StringComparison.Ordinal);
        Assert.Contains("通行编号", allText, StringComparison.Ordinal);
        Assert.Contains("暂无事件记录", allText, StringComparison.Ordinal);
        Assert.Contains("车辆运行详情", allText, StringComparison.Ordinal);
        Assert.Contains("运行过程", allText, StringComparison.Ordinal);
        Assert.Contains("暂无运行过程", allText, StringComparison.Ordinal);
        Assert.Contains("暂无系统日志", allText, StringComparison.Ordinal);
        Assert.Contains("暂无操作记录", allText, StringComparison.Ordinal);
        Assert.Contains("操作详情", allText, StringComparison.Ordinal);
        Assert.Contains("修改前", allText, StringComparison.Ordinal);
        Assert.Contains("修改后", allText, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_exposes_and_routes_the_run_log_navigation_item()
    {
        var windowPath = LocateSourceFile("src", "MineRailMonitor", "MainWindow.xaml");
        var windowMarkup = XDocument.Load(windowPath);
        var runLogButton = windowMarkup.Descendants(Presentation + "Button")
            .SingleOrDefault(button => (string?)button.Attribute("Tag") == "RunLogs");

        Assert.NotNull(runLogButton);
        Assert.Contains("运行日志", string.Join(" ", runLogButton!.Descendants(Presentation + "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))), StringComparison.Ordinal);

        var codePath = LocateSourceFile("src", "MineRailMonitor", "MainWindow.xaml.cs");
        var code = File.ReadAllText(codePath);
        Assert.Contains("page == \"RunLogs\"", code, StringComparison.Ordinal);
        Assert.Contains("PageContent.Content = _runLogPage", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_log_event_grid_reads_query_results_and_shows_an_empty_state()
    {
        var codePath = LocateSourceFile("src", "MineRailMonitor", "Pages", "RunLogPage.xaml.cs");
        var code = File.ReadAllText(codePath);
        var formatter = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor.Core", "Communication", "PassageEventDisplayFormatter.cs"));
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "RunLogPage.xaml"));

        Assert.Contains("IPassageEventQueryService", code, StringComparison.Ordinal);
        Assert.Contains("QueryPage(0,", code, StringComparison.Ordinal);
        Assert.Contains("QueryByPassageId", code, StringComparison.Ordinal);
        Assert.Contains("PassageEventDisplayFormatter.Format", code, StringComparison.Ordinal);
        Assert.Contains("PassageEventDisplayFormatter.FormatPassage", code, StringComparison.Ordinal);
        Assert.Contains("StationIdText", formatter, StringComparison.Ordinal);
        Assert.Contains("OutcomeReasonText", formatter, StringComparison.Ordinal);
        Assert.Contains("完整性未判定", formatter, StringComparison.Ordinal);
        Assert.Contains("{Binding StationIdText", markup, StringComparison.Ordinal);
        Assert.Contains("{Binding OutcomeReasonText", markup, StringComparison.Ordinal);
        Assert.Contains("EventGrid.ItemsSource", code, StringComparison.Ordinal);
        Assert.Contains("EventEmptyText.Visibility", code, StringComparison.Ordinal);
        Assert.Contains("IOperationRecordStore", code, StringComparison.Ordinal);
        Assert.Contains("_operationRecordStore.QueryPage(0, PageSize)", code, StringComparison.Ordinal);
        Assert.Contains("OperationRecordDisplayFormatter.Format", code, StringComparison.Ordinal);
        Assert.Contains("OperationGrid.ItemsSource", code, StringComparison.Ordinal);
        Assert.Contains("OperationDetailsPanel.DataContext", code, StringComparison.Ordinal);
        Assert.Contains("RunDetailsPanel.DataContext", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new RunLogEventRow(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_log_station_status_uses_live_statuses_and_station_scope_without_mock_rows()
    {
        var codePath = LocateSourceFile("src", "MineRailMonitor", "Pages", "RunLogPage.xaml.cs");
        var code = File.ReadAllText(codePath);
        var formatterPath = LocateSourceFile("src", "MineRailMonitor.Core", "Communication", "RfidStationStatusDisplayFormatter.cs");
        var formatterCode = File.ReadAllText(formatterPath);
        var windowPath = LocateSourceFile("src", "MineRailMonitor", "MainWindow.xaml.cs");
        var windowCode = File.ReadAllText(windowPath);

        Assert.DoesNotContain("192.168.1.100", code, StringComparison.Ordinal);
        Assert.DoesNotContain("10:30:35", code, StringComparison.Ordinal);
        Assert.Contains("GetPollingStatuses", windowCode, StringComparison.Ordinal);
        Assert.Contains("RfidStationConfig", code, StringComparison.Ordinal);
        Assert.Contains("RfidStationPollingStatus", code, StringComparison.Ordinal);
        Assert.Contains("StationId", code, StringComparison.Ordinal);
        Assert.Contains("SetDisplayScope", code, StringComparison.Ordinal);
        Assert.Contains("_displayScopeStationIds is null", code, StringComparison.Ordinal);
        Assert.Contains("_rfidStations\n            .Where(station => _displayScopeStationIds is null", code, StringComparison.Ordinal);
        Assert.Contains("_displayScopeStationIds.Contains(station.StationId.Trim())", code, StringComparison.Ordinal);
        Assert.Contains("GroupBy(status => status.StationId.Trim()", code, StringComparison.Ordinal);
        Assert.Contains("当前站场未配置 RFID 基站", code, StringComparison.Ordinal);
        Assert.Contains("LastReceivedAt", code, StringComparison.Ordinal);
        Assert.Contains("等待通信", formatterCode, StringComparison.Ordinal);
        Assert.Contains("已停用", formatterCode, StringComparison.Ordinal);
        Assert.Contains("_runLogPage.SetDisplayScope(stationIds, selectedYardId)", windowCode, StringComparison.Ordinal);
        Assert.Contains("_runLogPage.SetRfidStations", windowCode, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(PageContent.Content, _runLogPage)", windowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_log_station_status_is_rebuilt_during_refresh()
    {
        var codePath = LocateSourceFile("src", "MineRailMonitor", "Pages", "RunLogPage.xaml.cs");
        var code = File.ReadAllText(codePath);
        var refreshBody = code.Substring(code.IndexOf("public void Refresh()", StringComparison.Ordinal));

        Assert.Contains("RefreshStationStatuses()", refreshBody, StringComparison.Ordinal);
        Assert.Contains("_pollingStatusesProvider()", code, StringComparison.Ordinal);
        Assert.Contains("TryResolveEndpoint", code, StringComparison.Ordinal);
        Assert.Contains("端点不一致", code, StringComparison.Ordinal);
    }

    private static string[] GetGridHeaders(XDocument markup, string name)
    {
        var dataGrid = markup.Descendants(Presentation + "DataGrid")
            .Single(grid => (string?)grid.Attribute(Xaml + "Name") == name);
        return dataGrid.Descendants(Presentation + "DataGridTextColumn")
            .Select(column => (string?)column.Attribute("Header"))
            .ToArray()!;
    }

    private static string LocateSourceFile(params string[] parts)
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

        return Path.Combine(new[] { AppContext.BaseDirectory }.Concat(parts).ToArray());
    }
}
