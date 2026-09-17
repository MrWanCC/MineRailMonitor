namespace MineRailMonitor.Core.Tests;

public sealed class YardContextMarkupTests
{
    [Fact]
    public void Main_window_switches_context_without_restarting_backend_services()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("CurrentYardContext", code, StringComparison.Ordinal);
        Assert.Contains("IYardRfidStationResolver", code, StringComparison.Ordinal);
        Assert.Contains("_currentYardContext.SelectYard", code, StringComparison.Ordinal);
        Assert.Contains("_currentYardContext.SelectGlobal", code, StringComparison.Ordinal);
        Assert.Contains("_yardRfidStationResolver.Resolve", code, StringComparison.Ordinal);
        Assert.DoesNotContain("StopRfidPoller();\n            StartRfidPoller", ExtractMethod(code, "private async void OnStationClick"), StringComparison.Ordinal);
        Assert.DoesNotContain("CreateRfidRuntimeCoordinator", ExtractMethod(code, "private async void OnStationClick"), StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_wires_yard_communication_configuration_into_settings()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("yardCommunications: result.Project.YardCommunications", code, StringComparison.Ordinal);
        Assert.Contains("SaveYardCommunicationsAsync", code, StringComparison.Ordinal);
        Assert.Contains("await _yardCommunicationManager.RestartAllAsync()", code, StringComparison.Ordinal);
        Assert.Contains("CreateAcceptanceYardCommunications", code, StringComparison.Ordinal);
        Assert.Contains("YardId = \"560\"", code, StringComparison.Ordinal);
        Assert.Contains("YardId = \"620\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_acceptanceOptions.Enabled || project.UsesLegacySharedListener", code, StringComparison.Ordinal);
    }

    [Fact]
    public void All_record_pages_receive_the_current_yard_display_scope()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));
        var historyCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "HistoryPage.xaml.cs"));
        var alarmCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "AlarmHistoryPage.xaml.cs"));
        var statisticsCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "RfidStatisticsPage.xaml.cs"));

        Assert.Contains("GetCurrentYardScope", code, StringComparison.Ordinal);
        Assert.Contains("visibleRfidStationIds", code, StringComparison.Ordinal);
        Assert.Contains("_monitorPage.SetRfidRuntimeStates", code, StringComparison.Ordinal);
        Assert.Contains("_historyPage?.SetDisplayScope", code, StringComparison.Ordinal);
        Assert.Contains("_alarmHistoryPage?.SetDisplayScope", code, StringComparison.Ordinal);
        Assert.Contains("_statisticsPage?.SetDisplayScope", code, StringComparison.Ordinal);
        Assert.Contains("_communicationPage.SetDisplayScope", code, StringComparison.Ordinal);
        Assert.Contains("FilterCurrentYardStates(GetAllRuntimeStates()).ToArray()", code, StringComparison.Ordinal);
        Assert.Contains("YardPassageFilter.BuildStatistics", code, StringComparison.Ordinal);
        Assert.Contains("_visibleRfidStationIds", monitorCode, StringComparison.Ordinal);
        Assert.Contains("SelectedRfidStation", monitorCode, StringComparison.Ordinal);
        Assert.Contains("ClearDetails", monitorCode, StringComparison.Ordinal);
        Assert.Contains("StationIds = _displayScopeStationIds", historyCode, StringComparison.Ordinal);
        Assert.Contains("StationIds = _displayScopeStationIds", alarmCode, StringComparison.Ordinal);
        Assert.Contains("YardPassageFilter.Filter", statisticsCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Resetting_record_filters_keeps_the_global_yard_scope()
    {
        var historyCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "HistoryPage.xaml.cs"));
        var alarmCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "AlarmHistoryPage.xaml.cs"));

        Assert.DoesNotContain("_displayScopeStationIds = null", ExtractMethod(historyCode, "private void OnResetClick"), StringComparison.Ordinal);
        Assert.DoesNotContain("SetYardFilterSelection(RfidStationYardOption.AllId)", ExtractMethod(historyCode, "private void OnResetClick"), StringComparison.Ordinal);
        Assert.DoesNotContain("_displayScopeStationIds = null", ExtractMethod(alarmCode, "private void OnResetClick"), StringComparison.Ordinal);
        Assert.DoesNotContain("SetYardFilterSelection(RfidStationYardOption.AllId)", ExtractMethod(alarmCode, "private void OnResetClick"), StringComparison.Ordinal);
    }

    [Fact]
    public void Monitor_page_does_not_use_protocol_address_for_yard_filtering()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));
        var resolver = File.ReadAllText(Locate("src", "MineRailMonitor.Core", "Services", "YardRfidStationResolver.cs"));

        Assert.DoesNotContain("ProtocolAddress", resolver, StringComparison.Ordinal);
        Assert.Contains("RfidStationId", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("state.StationAddress", code.Substring(code.IndexOf("GetCurrentYardScope", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method: {signature}");
        var next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return next >= 0 ? source.Substring(start, next - start) : source.Substring(start);
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
