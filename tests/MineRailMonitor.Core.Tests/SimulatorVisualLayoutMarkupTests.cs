namespace MineRailMonitor.Core.Tests;

public sealed class SimulatorVisualLayoutMarkupTests
{
    [Fact]
    public void Simulator_markup_has_the_reference_three_column_workbench_structure()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor.Simulator", "MainWindow.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor.Simulator", "MainWindow.xaml.cs"));

        Assert.Contains("Width=\"1680\" Height=\"942\"", markup, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"64\" />", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HeaderStatsPanel\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MainContentGrid\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SaveStationConfigButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StationDeletePopup\"", markup, StringComparison.Ordinal);
        Assert.Contains("OnDeleteStationPopupClick", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviewMouseRightButtonDown=\"OnStationCardPreviewMouseRightButtonDown\"", markup, StringComparison.Ordinal);
        Assert.Contains("StaysOpen=\"True\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<ContextMenu", markup, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", markup, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseWheel=\"OnVirtualStationsListPreviewMouseWheel\"", markup, StringComparison.Ordinal);
        Assert.Contains("删除基站", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CurrentStationPanel\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CurrentStationHeader\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CurrentStationStatusText\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScenarioPanel\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ResponseControlPanel\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScenarioProgressBar\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CommunicationLogPanel\"", markup, StringComparison.Ordinal);
        Assert.Contains("Width=\"290\"", markup, StringComparison.Ordinal);
        Assert.Contains("Width=\"500\"", markup, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"2\"", markup, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", markup, StringComparison.Ordinal);
        Assert.Contains("HeaderRunningStationsText", code, StringComparison.Ordinal);
        Assert.Contains("CurrentStationStatusText", code, StringComparison.Ordinal);
        Assert.Contains("ScenarioProgressBar.Value", code, StringComparison.Ordinal);
        Assert.Contains("OnWindowPreviewMouseRightButtonDown", code, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseRightButtonDownEvent", code, StringComparison.Ordinal);
        Assert.Contains("OnDeleteStationPopupClick", code, StringComparison.Ordinal);
        Assert.Contains("OnVirtualStationsListPreviewMouseWheel", code, StringComparison.Ordinal);
        Assert.Contains("SaveStationConfigurationManually", code, StringComparison.Ordinal);
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

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar.ToString(), segments));
    }
}
