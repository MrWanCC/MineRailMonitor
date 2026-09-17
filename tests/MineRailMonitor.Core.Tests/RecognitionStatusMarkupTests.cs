namespace MineRailMonitor.Core.Tests;

public sealed class RecognitionStatusMarkupTests
{
    [Fact]
    public void Settings_and_monitor_expose_vehicle_count_and_head_warning_copy()
    {
        var settingsMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var settingsCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));
        var monitorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));
        var deviceVisualCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "DeviceVisual.cs"));
        var deviceLayerCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "DeviceLayer.xaml.cs"));
        var mainWindowCode = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));
        var simulatorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor.Simulator", "MainWindow.xaml"));
        var simulatorCode = File.ReadAllText(Locate("src", "MineRailMonitor.Simulator", "MainWindow.xaml.cs"));
        var simulatorContextCode = File.ReadAllText(Locate("src", "MineRailMonitor.Simulator", "Models", "SimulatorStationContext.cs"));
        var communicationCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "CommunicationPage.xaml.cs"));

        Assert.Contains("标准列车节数（含车头）", settingsMarkup);
        Assert.Contains("提示：包含车头", settingsMarkup);
        Assert.Contains("ExpectedVehicleCount", settingsCode);
        Assert.Contains("InterVehicleTimeoutSeconds", settingsCode);
        Assert.Contains("RecognitionNotice", monitorMarkup);
        Assert.Contains("RecognitionSnapshotText", monitorMarkup);
        Assert.Contains("SetRecognitionSnapshot", monitorCode);
        Assert.Contains("SetRecognitionStatus", monitorCode);
        Assert.Contains("SetRfidRuntimeStates", monitorCode);
        Assert.Contains("RfidStationVisualState", deviceVisualCode);
        Assert.Contains("SetRuntimeStates", deviceLayerCode);
        Assert.Contains("未检测到车头标签", mainWindowCode);
        Assert.Contains("首个识别标签不是有效车头标签", mainWindowCode);
        Assert.Contains("检测到多个车头标签", mainWindowCode);
        Assert.Contains("协议数据告警", mainWindowCode);
        Assert.Contains("脱节报警", mainWindowCode);
        Assert.Contains("LoadScenario(\"正常11节\"", simulatorCode);
        Assert.Contains("0x0011", simulatorCode);
        Assert.Contains("SimulatorStationContext", simulatorCode);
        Assert.Contains("ScenarioPlaybackState", simulatorContextCode);
        Assert.Contains("多车头11节", simulatorMarkup);
        Assert.Contains("无车头11节", simulatorMarkup);
        Assert.Contains("首位异常11节", simulatorMarkup);
        Assert.Contains("ApplyMultiHeadPreset", simulatorCode);
        Assert.Contains("ApplyNoHeadPreset", simulatorCode);
        Assert.Contains("ApplyFirstNonHeadPreset", simulatorCode);
        Assert.Contains("Slots", simulatorCode);
        Assert.Contains("扫入下一张", simulatorMarkup);
        Assert.Contains("RFID 槽位：", communicationCode);
        Assert.Contains("ActualNonZeroSlotCount", communicationCode);
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
