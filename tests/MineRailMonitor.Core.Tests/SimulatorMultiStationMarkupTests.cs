namespace MineRailMonitor.Core.Tests;

public sealed class SimulatorMultiStationMarkupTests
{
    [Fact]
    public void Simulator_markup_exposes_multi_station_management_without_duplicate_details()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor.Simulator", "MainWindow.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor.Simulator", "MainWindow.xaml.cs"));

        Assert.Contains("虚拟基站列表", markup, StringComparison.Ordinal);
        Assert.Contains("多站总览", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"VirtualStationsListBox\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiStationOverviewGrid\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CreateSixStationsButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CreateDualYardStationsButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"YardFilterComboBox\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartSelectedYardButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StopSelectedYardButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartAllStationsButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StopAllStationsButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClearAllStationsButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ResetAllScenariosButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AddStationButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RemoveStationButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnDeleteStationCardClick\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LocalIpTextBox\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LocalPortTextBox\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AddressTextBox\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"LocalPortTextBox\" Text=\"62001\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("监听 127.0.0.1:62001", markup, StringComparison.Ordinal);

        Assert.Contains("ObservableCollection<SimulatorStationContext>", code, StringComparison.Ordinal);
        Assert.Contains("OnVirtualStationSelectionChanged", code, StringComparison.Ordinal);
        Assert.Contains("TryApplyConfiguration", code, StringComparison.Ordinal);
        Assert.Contains("CreateDefaultSix", code, StringComparison.Ordinal);
        Assert.Contains("CreateDefaultDualYardStations", code, StringComparison.Ordinal);
        Assert.Contains("RefreshStationView", code, StringComparison.Ordinal);
        Assert.Contains("StartSelectedYardStations", code, StringComparison.Ordinal);
        Assert.Contains("StopSelectedYardStations", code, StringComparison.Ordinal);
        Assert.Contains("StartAllStations", code, StringComparison.Ordinal);
        Assert.Contains("StopAllStations", code, StringComparison.Ordinal);
        Assert.Contains("SaveStationConfiguration", code, StringComparison.Ordinal);
        Assert.Contains("OnDeleteStationCardClick", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private SimulatorUdpResponder? _responder", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SimulatorFrameInput[] _stationInputs", code, StringComparison.Ordinal);
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
