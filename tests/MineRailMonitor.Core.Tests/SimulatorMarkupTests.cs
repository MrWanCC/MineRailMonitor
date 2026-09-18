namespace MineRailMonitor.Core.Tests;

public sealed class SimulatorMarkupTests
{
    [Fact]
    public void Simulator_markup_matches_the_operator_workbench_layout()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor.Simulator", "MainWindow.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor.Simulator", "MainWindow.xaml.cs"));

        Assert.Contains("RFID读卡分站模拟器", markup, StringComparison.Ordinal);
        Assert.Contains("连接配置", markup, StringComparison.Ordinal);
        Assert.Contains("顶部状态条", markup, StringComparison.Ordinal);
        Assert.Contains("监听地址", markup, StringComparison.Ordinal);
        Assert.Contains("协议地址", markup, StringComparison.Ordinal);
        Assert.Contains("RFID槽位", markup, StringComparison.Ordinal);
        Assert.Contains("已占用", markup, StringComparison.Ordinal);
        Assert.Contains("测试场景", markup, StringComparison.Ordinal);
        Assert.Contains("当前场景", markup, StringComparison.Ordinal);
        Assert.Contains("有效RFID", markup, StringComparison.Ordinal);
        Assert.Contains("场景播放", markup, StringComparison.Ordinal);
        Assert.Contains("扫卡间隔", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScenarioIntervalTextBox\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScenarioStartButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScenarioNextButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScenarioPauseButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScenarioResetButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScenarioProgressTextBlock\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScenarioCurrentRfidTextBlock\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScenarioNextRfidTextBlock\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScenarioDistanceTextBlock\"", markup, StringComparison.Ordinal);
        Assert.Contains("应答控制", markup, StringComparison.Ordinal);
        Assert.Contains("最近请求", markup, StringComparison.Ordinal);
        Assert.Contains("最近命令", markup, StringComparison.Ordinal);
        Assert.Contains("请求来源", markup, StringComparison.Ordinal);
        Assert.Contains("收发日志", markup, StringComparison.Ordinal);
        Assert.Contains("原始HEX", markup, StringComparison.Ordinal);
        Assert.Contains("帧解析", markup, StringComparison.Ordinal);
        Assert.Contains("自动滚动", markup, StringComparison.Ordinal);
        Assert.Contains("配置无效时禁止启动应答", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RfidInputsPanel\" Columns=\"2\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ApplyConfigButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ConfigErrorTextBlock\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RecentCommandText\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RequestSourceText\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RawTrafficHexTextBox\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FrameParseTextBox\"", markup, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"226\" />", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RecentRequestPanel\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ConnectionConfigPanel\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FaultModeComboBox\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FaultDelayTextBox\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RestoreFaultButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FaultStateTextBlock\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClearLogButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SaveLogButton\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("MetricCardStyle", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("目标上位机", markup, StringComparison.Ordinal);
        Assert.Contains("ValidateConfiguration", code, StringComparison.Ordinal);
        Assert.Contains("ApplyConfiguration", code, StringComparison.Ordinal);
        Assert.Contains("RecentCommandText", code, StringComparison.Ordinal);
        Assert.Contains("HighlightSlot", code, StringComparison.Ordinal);
        Assert.Contains("ClearLogButton.Click", code, StringComparison.Ordinal);
        Assert.Contains("SaveLogButton.Click", code, StringComparison.Ordinal);
        Assert.Contains("SimulatorStationContext", code, StringComparison.Ordinal);
        Assert.Contains("ScenarioPlaybackTimer", code, StringComparison.Ordinal);
        Assert.Contains("LoadScenario", code, StringComparison.Ordinal);
        Assert.Contains("ObservableCollection<SimulatorStationContext>", code, StringComparison.Ordinal);
        Assert.Contains("SelectedStation", code, StringComparison.Ordinal);
        Assert.Contains("SimulatorStationPersistence.Load", code, StringComparison.Ordinal);
        Assert.Contains("SimulatorStationPersistence.Save", code, StringComparison.Ordinal);
        Assert.Contains("mode == SimulatorFaultMode.Delay", code, StringComparison.Ordinal);
        Assert.Contains("FaultConfigErrorTextBlock.Text = string.Empty", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_stationInputs", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Fault_mode_selection_enables_delay_editor_before_validation()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor.Simulator", "MainWindow.xaml.cs"));
        var handlerStart = code.IndexOf("private void OnFaultModeSelectionChanged", StringComparison.Ordinal);
        var handlerEnd = code.IndexOf("private void OnFaultDelayTextChanged", handlerStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(handlerEnd > handlerStart);

        var handler = code.Substring(handlerStart, handlerEnd - handlerStart);
        var enableIndex = handler.IndexOf("FaultDelayTextBox.IsEnabled", StringComparison.Ordinal);
        var applyIndex = handler.IndexOf("TryApplyFaultConfigurationFromUi", StringComparison.Ordinal);

        Assert.True(enableIndex >= 0);
        Assert.True(applyIndex > enableIndex);
        Assert.Contains("SimulatorFaultMode.Delay", handler, StringComparison.Ordinal);
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
