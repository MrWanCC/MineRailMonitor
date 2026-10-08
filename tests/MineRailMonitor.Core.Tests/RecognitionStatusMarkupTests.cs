using MineRailMonitor.Simulator.Models;

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

        Assert.Contains("全局回退节数（含车头，可选）", settingsMarkup);
        Assert.Contains("留空且基站未配置时，未知车辆不做完整性报警", settingsMarkup);
        Assert.Contains("ExpectedVehicleCount", settingsCode);
        Assert.Contains("InterVehicleTimeoutSeconds", settingsCode);
        Assert.Contains("RecognitionNotice", monitorMarkup);
        Assert.Contains("RecognitionSnapshotText", monitorMarkup);
        Assert.Contains("SetRecognitionSnapshot", monitorCode);
        Assert.Contains("SetRecognitionStatus", monitorCode);
        Assert.Contains("SetRfidRuntimeStates", monitorCode);
        Assert.Contains("RfidStationVisualState", deviceVisualCode);
        Assert.Contains("SetRuntimeStates", deviceLayerCode);
        Assert.Contains("协议数据告警", mainWindowCode);
        Assert.Contains("脱节报警", mainWindowCode);
        Assert.Contains("ExpectedVehicleCount = project.RfidSettings.ExpectedVehicleCount", mainWindowCode);
        Assert.Contains(
            "if (_acceptanceOptions.Enabled)\n        {\n            result.Project.Vehicles = CreateAcceptanceVehicles();\n            result.Project.RfidSettings.ExpectedVehicleCount = _acceptanceOptions.DisableGlobalExpectedCountFallback\n                ? null\n                : 11;\n        }",
            mainWindowCode.Replace("\r\n", "\n"));
        Assert.Contains("LoadScenario(\"11节正常通过\"", simulatorCode);
        Assert.Contains("11节缺5节", simulatorCode);
        Assert.Contains("11节缺1节", simulatorCode);
        Assert.Contains("11节补扫完成", simulatorCode);
        Assert.Contains("7节正常通过", simulatorCode);
        Assert.DoesNotContain("只有6节", simulatorMarkup);
        Assert.DoesNotContain("只有10节", simulatorMarkup);
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

    [Fact]
    public void Normal_eleven_section_scenario_loads_and_advances_through_slots()
    {
        using var station = new SimulatorStationContext(SimulatorStationPresets.CreateDefaultSix().First());

        SimulatorStationPresets.ApplyAllNormal(new[] { station });

        Assert.Equal("11节正常通过", station.ScenarioName);
        Assert.Equal(11, station.Playback.Sequence.Count);
        Assert.Equal(11, station.Playback.Sequence.Distinct().Count());
        Assert.True(station.StartScenario(TimeSpan.FromMilliseconds(1)));
        Assert.Equal(1, station.ValidRfidCount);
        Assert.NotEqual((ushort)0, station.SnapshotSlots()[0]);

        for (var index = 1; index < 11; index++)
        {
            var nextStepAt = station.NextScenarioStepAt;
            Assert.True(nextStepAt.HasValue);
            Assert.True(station.TickScenario(nextStepAt.Value));
        }

        Assert.Equal(11, station.ValidRfidCount);
        Assert.Equal(11, station.SnapshotSlots().Count(value => value != 0));
        Assert.Equal(14, station.SnapshotSlots().Count);
    }

    [Fact]
    public void Head_diagnostics_describe_protocol_position_zero()
    {
        var runtimeCode = File.ReadAllText(Locate("src", "MineRailMonitor.Core", "Services", "RfidRuntimeCoordinator.cs"));

        Assert.Contains("未检测到车头槽位(Position 0)", runtimeCode);
        Assert.Contains("首个识别RFID未出现在车头槽位(Position 0)", runtimeCode);
        Assert.Contains("检测到多个车头槽位RFID", runtimeCode);
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
