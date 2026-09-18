using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MineRailMonitor.Simulator.Models;
using MineRailMonitor.Simulator.Protocol;

namespace MineRailMonitor.Simulator;

public partial class MainWindow : Window
{
    private static readonly ushort[] Normal11 =
    {
        0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015,
        0x0016, 0x0017, 0x0018, 0x0019, 0x001A
    };

    private readonly ObservableCollection<TextBox> _rfidTextBoxes = new();
    private readonly List<Ellipse> _rfidIndicators = new();
    private readonly ObservableCollection<SimulatorStationContext> _stations = new();
    private readonly DispatcherTimer _slotHighlightTimer;
    private readonly DispatcherTimer _scenarioPlaybackTimer;
    private readonly string _stationPersistencePath;
    private ICollectionView? _stationsView;
    private TextBox? _highlightedSlot;
    private SimulatorStationContext? _selectedStation;
    private SimulatorStationContext? _stationDeleteTarget;
    private string _selectedYardId = "560";
    private bool _loadingStation;
    private bool _loaded;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(
            UIElement.PreviewMouseRightButtonDownEvent,
            new MouseButtonEventHandler(OnWindowPreviewMouseRightButtonDown),
            handledEventsToo: true);
        _stationPersistencePath = GetStationPersistencePath();
        _slotHighlightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _slotHighlightTimer.Tick += OnSlotHighlightTimerTick;
        _scenarioPlaybackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _scenarioPlaybackTimer.Tick += OnScenarioPlaybackTimerTick;
        Loaded += OnLoaded;
        Closed += OnWindowClosed;
    }

    public ObservableCollection<SimulatorStationContext> Stations => _stations;

    public SimulatorStationContext? SelectedStation => _selectedStation;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildRfidInputs();
        HookButtons();
        _stationsView = new ListCollectionView(Stations);
        _stationsView.Filter = IsStationInSelectedYard;
        VirtualStationsListBox.ItemsSource = _stationsView;
        MultiStationOverviewGrid.ItemsSource = _stationsView;
        LoadStationsFromPersistence();
        _loaded = true;
        _scenarioPlaybackTimer.Start();
        UpdateSelectedStationDetails();
        UpdateStationSummaries();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _scenarioPlaybackTimer.Stop();
        _slotHighlightTimer.Stop();
        SaveStationConfiguration();
        foreach (var station in Stations.ToArray())
        {
            station.Dispose();
        }
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is Button)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            DragMove();
            e.Handled = true;
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void BuildRfidInputs()
    {
        RfidInputsPanel.Children.Clear();
        _rfidTextBoxes.Clear();
        _rfidIndicators.Clear();

        for (var index = 0; index < 14; index++)
        {
            AddInput($"RFID{index + 1:00}", string.Empty);
        }

        UpdateSlotSummary();
    }

    private void AddInput(string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 5, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(157, 190, 204)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });

        var box = new TextBox
        {
            Text = value,
            Margin = new Thickness(4, 0, 4, 0),
            Padding = new Thickness(7, 4, 7, 4)
        };
        var indicator = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        box.TextChanged += (_, _) =>
        {
            UpdateSlotVisual(box, indicator);
            UpdatePreview();
        };

        Grid.SetColumn(box, 1);
        Grid.SetColumn(indicator, 2);
        row.Children.Add(box);
        row.Children.Add(indicator);
        _rfidTextBoxes.Add(box);
        _rfidIndicators.Add(indicator);
        RfidInputsPanel.Children.Add(row);
        UpdateSlotVisual(box, indicator);
    }

    private void HookButtons()
    {
        ApplyConfigButton.Click += (_, _) => ApplyConfiguration();
        CreateSixStationsButton.Click += (_, _) => CreateSixStations();
        AddStationButton.Click += (_, _) => AddStation();
        RemoveStationButton.Click += (_, _) => RemoveSelectedStation();
        StartAllStationsButton.Click += async (_, _) => await StartAllStations();
        StopAllStationsButton.Click += (_, _) => StopAllStations();
        ClearAllStationsButton.Click += (_, _) => ClearAllStations();
        CreateDualYardStationsButton.Click += (_, _) => CreateDualYardStations();
        StartSelectedYardButton.Click += async (_, _) => await StartSelectedYardStations();
        StopSelectedYardButton.Click += (_, _) => StopSelectedYardStations();
        YardFilterComboBox.SelectionChanged += OnYardFilterSelectionChanged;
        ResetAllScenariosButton.Click += (_, _) => ResetAllScenarios();
        SaveStationConfigButton.Click += (_, _) => SaveStationConfigurationManually();
        ConcurrentSixPresetButton.Click += async (_, _) => await ApplyConcurrentSixPreset();
        AllNormalPresetButton.Click += (_, _) => ApplyAllNormalPreset();
        OneNormalFiveOfflinePresetButton.Click += (_, _) => ApplyOneNormalFiveOfflinePreset();
        SameAddressIsolationPresetButton.Click += (_, _) => ApplySameAddressIsolationPreset();
        NormalPresetButton.Click += (_, _) => ApplyNormalPreset();
        TenWagonPresetButton.Click += (_, _) => ApplyTenWagonPreset();
        ShortPresetButton.Click += (_, _) => ApplyShortPreset();
        DuplicatePresetButton.Click += (_, _) => ApplyDuplicatePreset();
        MultiHeadPresetButton.Click += (_, _) => ApplyMultiHeadPreset();
        NoHeadPresetButton.Click += (_, _) => ApplyNoHeadPreset();
        FirstNonHeadPresetButton.Click += (_, _) => ApplyFirstNonHeadPreset();
        ClearButton.Click += (_, _) => ClearSelectedStationSlots();
        ScenarioClearButton.Click += (_, _) => ResetScenarioPlayback();
        ScenarioStartButton.Click += (_, _) => StartScenarioPlayback();
        ScenarioNextButton.Click += (_, _) => StepScenarioPlayback();
        ScenarioPauseButton.Click += (_, _) => PauseScenarioPlayback();
        ScenarioResetButton.Click += (_, _) => ResetScenarioPlayback();
        ScanNextButton.Click += (_, _) => ScanNextTag();
        RemoveTagButton.Click += (_, _) => RemoveTag();
        RestoreFaultButton.Click += (_, _) => RestoreSelectedStationFault();
        SendOnceButton.Click += async (_, _) => await StartSelectedStation();
        StartLoopButton.Click += async (_, _) => await StartSelectedStation();
        StopLoopButton.Click += (_, _) => StopSelectedStation();
        ClearLogButton.Click += (_, _) => ClearLogs();
        SaveLogButton.Click += (_, _) => SaveLog();
    }

    private void LoadStationsFromPersistence()
    {
        var configs = SimulatorStationPersistence.Load(_stationPersistencePath);
        if (configs.Count == 0)
        {
            CreateDualYardStations();
            return;
        }

        foreach (var config in configs)
        {
            AddContext(config, loadDefaultScenario: true);
        }

        _selectedYardId = configs.Select(config => config.YardId)
            .FirstOrDefault(yardId => string.Equals(yardId, "560", StringComparison.OrdinalIgnoreCase))
            ?? configs.FirstOrDefault()?.YardId
            ?? "560";
        YardFilterComboBox.SelectedValue = _selectedYardId;
        RefreshStationView();
        SelectStation(Stations.FirstOrDefault(IsStationInSelectedYard));
    }

    private void CreateSixStations()
    {
        foreach (var station in Stations.ToArray())
        {
            station.Dispose();
        }
        Stations.Clear();

        foreach (var config in SimulatorStationPresets.CreateDefaultSix())
        {
            AddContext(config, loadDefaultScenario: true);
        }

        _selectedYardId = "560";
        YardFilterComboBox.SelectedValue = _selectedYardId;
        RefreshStationView();
        SelectStation(Stations.FirstOrDefault(IsStationInSelectedYard));
        SaveStationConfiguration();
        StatusTextBlock.Text = "已创建 6 个虚拟基站，可分别配置或全部启动。";
    }

    private void CreateDualYardStations()
    {
        foreach (var station in Stations.ToArray())
        {
            station.Dispose();
        }
        Stations.Clear();

        foreach (var config in SimulatorStationPresets.CreateDefaultDualYardStations())
        {
            AddContext(config, loadDefaultScenario: true);
        }

        _selectedYardId = "560";
        YardFilterComboBox.SelectedValue = _selectedYardId;
        RefreshStationView();
        SelectStation(Stations.FirstOrDefault(IsStationInSelectedYard));
        SaveStationConfiguration();
        StatusTextBlock.Text = "已创建 560、620 两个独立站场，共 12 个虚拟基站。";
    }

    private void AddContext(SimulatorStationConfig config, bool loadDefaultScenario)
    {
        var context = new SimulatorStationContext(config);
        if (loadDefaultScenario)
        {
            context.LoadScenario("正常11节", Normal11);
        }
        Stations.Add(context);
    }

    private void AddStation()
    {
        var yardId = _selectedYardId;
        var index = Stations.Count(station => string.Equals(station.Config.YardId, yardId, StringComparison.OrdinalIgnoreCase)) + 1;
        var port = Enumerable.Range(63001, 1000)
            .Where(candidate => candidate != 63002 && candidate != 63012)
            .First(candidate => Stations.All(station => station.Config.ListenPort != candidate));
        var address = Enumerable.Range(1, 255)
            .Select(candidate => (byte)candidate)
            .First(candidate => Stations
                .Where(station => string.Equals(station.Config.YardId, yardId, StringComparison.OrdinalIgnoreCase))
                .All(station => station.Config.ProtocolAddress != candidate));
        var context = new SimulatorStationContext(new SimulatorStationConfig
        {
            YardId = yardId,
            StationName = $"RFID-{yardId}-{index:00}",
            ListenIp = "127.0.0.1",
            ListenPort = port,
            ProtocolAddress = address,
            Enabled = true
        });
        context.LoadScenario("正常11节", Normal11);
        Stations.Add(context);
        RefreshStationView();
        SelectStation(context);
        SaveStationConfiguration();
    }

    private async Task ApplyConcurrentSixPreset()
    {
        CreateSixStations();
        await StartAllStations();
    }

    private void ApplyAllNormalPreset()
    {
        SimulatorStationPresets.ApplyAllNormal(Stations);
        UpdateSelectedStationDetails();
        UpdateStationSummaries();
    }

    private void ApplyOneNormalFiveOfflinePreset()
    {
        SimulatorStationPresets.ApplyOneNormalFiveOffline(Stations);
        UpdateSelectedStationDetails();
        UpdateStationSummaries();
        SaveStationConfiguration();
    }

    private void ApplySameAddressIsolationPreset()
    {
        StopAllStations();
        try
        {
            SimulatorStationPresets.ApplySameAddressIsolation(Stations);
            StatusTextBlock.Text = "同Address隔离预设已应用，请启动两个独立端点。";
        }
        catch (ArgumentException exception)
        {
            ErrorTextBlock.Text = exception.Message;
        }

        UpdateSelectedStationDetails();
        UpdateStationSummaries();
        SaveStationConfiguration();
    }

    private void RemoveSelectedStation() => RemoveStation(_selectedStation);

    private void OnDeleteStationCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not SimulatorStationContext station)
        {
            return;
        }

        SelectStation(station);
        _stationDeleteTarget = station;
        StationDeletePopup.PlacementTarget = button;
        StationDeletePopup.IsOpen = true;
        e.Handled = true;
    }

    private void OnWindowPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var target = FindElementWithStationDataContext(source, out var station);
        if (target is null || station is null)
        {
            return;
        }

        SelectStation(station);
        _stationDeleteTarget = station;
        StationDeletePopup.PlacementTarget = target;
        StationDeletePopup.IsOpen = true;
        e.Handled = true;
    }

    private static FrameworkElement? FindElementWithStationDataContext(
        DependencyObject? current,
        out SimulatorStationContext? station)
    {
        while (current is not null)
        {
            if (current is FrameworkElement element &&
                element.DataContext is SimulatorStationContext context)
            {
                station = context;
                return element;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        station = null;
        return null;
    }

    private void OnVirtualStationsListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var scrollViewer = FindVisualParent<ScrollViewer>(source);
        if (scrollViewer is null || scrollViewer.ExtentHeight <= scrollViewer.ViewportHeight)
        {
            return;
        }

        var currentOffset = scrollViewer.VerticalOffset;
        var maximumOffset = Math.Max(0, scrollViewer.ExtentHeight - scrollViewer.ViewportHeight);
        var targetOffset = Math.Max(0, Math.Min(maximumOffset, currentOffset - e.Delta / 3.0));
        if (Math.Abs(targetOffset - currentOffset) < 0.1)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(targetOffset);
        e.Handled = true;
    }

    private static T? FindVisualParent<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void OnDeleteStationPopupClick(object sender, RoutedEventArgs e)
    {
        var station = _stationDeleteTarget;
        _stationDeleteTarget = null;
        StationDeletePopup.IsOpen = false;
        RemoveStation(station);
    }

    private void RemoveStation(SimulatorStationContext? station)
    {
        if (station is null)
        {
            return;
        }

        var removedIndex = Stations.IndexOf(station);
        if (removedIndex < 0)
        {
            return;
        }

        station.Dispose();
        Stations.Remove(station);
        SelectStation(Stations.ElementAtOrDefault(Math.Max(0, removedIndex - 1)) ?? Stations.FirstOrDefault());
        SaveStationConfiguration();
        StatusTextBlock.Text = $"已删除基站：{station.Config.StationName}";
    }

    private void OnVirtualStationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || _loadingStation || VirtualStationsListBox.SelectedItem is not SimulatorStationContext station)
        {
            return;
        }

        SelectStation(station);
    }

    private void OnYardFilterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (YardFilterComboBox.SelectedValue is not string yardId || string.IsNullOrWhiteSpace(yardId))
        {
            return;
        }

        _selectedYardId = yardId;
        RefreshStationView();
        if (_selectedStation is null || !IsStationInSelectedYard(_selectedStation))
        {
            SelectStation(Stations.FirstOrDefault(IsStationInSelectedYard));
        }
    }

    private bool IsStationInSelectedYard(object item) =>
        item is SimulatorStationContext station &&
        string.Equals(station.Config.YardId, _selectedYardId, StringComparison.OrdinalIgnoreCase);

    private void RefreshStationView()
    {
        _stationsView?.Refresh();
        VirtualStationsListBox?.Items.Refresh();
        MultiStationOverviewGrid?.Items.Refresh();
    }

    private void OnOverviewDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MultiStationOverviewGrid.SelectedItem is SimulatorStationContext station)
        {
            SelectStation(station);
        }
    }

    private void SelectStation(SimulatorStationContext? station)
    {
        _selectedStation = station;
        _loadingStation = true;
        VirtualStationsListBox.SelectedItem = station;
        _loadingStation = false;
        UpdateSelectedStationDetails();
        UpdateStationSummaries();
    }

    private void ApplyNormalPreset() => LoadSelectedScenario("正常11节", Normal11);

    private void ApplyTenWagonPreset() => LoadSelectedScenario("只有10节", new ushort[]
    {
        0x0001, 0x0011, 0x0012, 0x0013, 0x0014,
        0x0015, 0x0016, 0x0017, 0x0018, 0x0019
    });

    private void ApplyShortPreset() => LoadSelectedScenario("只有6节", new ushort[]
    {
        0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015
    });

    private void ApplyDuplicatePreset() => LoadSelectedScenario("重复RFID", new ushort[]
    {
        0x0001, 0x0011, 0x0012, 0x0013, 0x0013, 0x0014, 0x0015
    });

    private void ApplyMultiHeadPreset() => LoadSelectedScenario("多车头11节", new ushort[]
    {
        0x0001, 0x0003, 0x000B, 0x000C, 0x000D, 0x000E,
        0x000F, 0x0010, 0x0011, 0x0012, 0x0013
    });

    private void ApplyNoHeadPreset() => LoadSelectedScenario("无车头11节", new ushort[]
    {
        0x000B, 0x000C, 0x000D, 0x000E, 0x000F, 0x0010,
        0x0011, 0x0012, 0x0013, 0x0014, 0x0015
    });

    private void ApplyFirstNonHeadPreset() => LoadSelectedScenario("首位异常11节", new ushort[]
    {
        0x001A, 0x0001, 0x0011, 0x0012, 0x0013, 0x0014,
        0x0015, 0x0016, 0x0017, 0x0018, 0x0019
    });

    private void LoadSelectedScenario(string name, IEnumerable<ushort> sequence)
    {
        if (_selectedStation is null)
        {
            return;
        }

        _selectedStation.LoadScenario(name, sequence);
        LoadSelectedSlotsIntoDetails();
        UpdateScenarioPlaybackStatus();
        UpdateStationSummaries();
    }

    private void StartScenarioPlayback()
    {
        if (_selectedStation is null)
        {
            InteractionErrorTextBlock.Text = "请先选择一个虚拟基站。";
            return;
        }
        if (!TryReadScenarioInterval(out var interval))
        {
            return;
        }
        if (!_selectedStation.StartScenario(interval))
        {
            InteractionErrorTextBlock.Text = "场景已播放完成，请先重置场景。";
            return;
        }

        InteractionErrorTextBlock.Text = string.Empty;
        LoadSelectedSlotsIntoDetails();
        UpdateScenarioPlaybackStatus();
        UpdateStationSummaries();
    }

    private void StepScenarioPlayback()
    {
        if (_selectedStation is null || !_selectedStation.StepScenario())
        {
            InteractionErrorTextBlock.Text = "场景已播放完成，请先重置场景。";
            return;
        }

        InteractionErrorTextBlock.Text = string.Empty;
        LoadSelectedSlotsIntoDetails();
        UpdateScenarioPlaybackStatus();
        UpdateStationSummaries();
    }

    private void PauseScenarioPlayback()
    {
        _selectedStation?.PauseScenario();
        UpdateScenarioPlaybackStatus();
        UpdateStationSummaries();
    }

    private void ResetScenarioPlayback()
    {
        _selectedStation?.ResetScenario();
        InteractionErrorTextBlock.Text = string.Empty;
        LoadSelectedSlotsIntoDetails();
        UpdateScenarioPlaybackStatus();
        UpdateStationSummaries();
    }

    private void OnScenarioPlaybackTimerTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var station in Stations)
        {
            var stepped = station.TickScenario(now);
            if (stepped && ReferenceEquals(station, _selectedStation))
            {
                LoadSelectedSlotsIntoDetails();
            }
        }

        UpdateSelectedTelemetry();
        UpdateStationSummaries();
    }

    private bool TryReadScenarioInterval(out TimeSpan interval)
    {
        interval = TimeSpan.Zero;
        if (!double.TryParse(ScenarioIntervalTextBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            seconds < 0.1 || seconds > 3600)
        {
            InteractionErrorTextBlock.Text = "扫卡间隔必须是 0.1-3600 秒。";
            return false;
        }

        interval = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private void UpdateScenarioPlaybackStatus()
    {
        if (_selectedStation is null)
        {
            ScenarioProgressTextBlock.Text = "0 / 0";
            ScenarioProgressBar.Value = 0;
            ScenarioCurrentRfidTextBlock.Text = "--";
            ScenarioNextRfidTextBlock.Text = "--";
            ScenarioDistanceTextBlock.Text = "--";
            return;
        }

        var playback = _selectedStation.Playback;
        ScenarioProgressTextBlock.Text = $"{playback.CurrentIndex} / {playback.Sequence.Count}";
        ScenarioProgressBar.Value = playback.Sequence.Count == 0
            ? 0
            : (double)playback.CurrentIndex / playback.Sequence.Count;
        ScenarioCurrentRfidTextBlock.Text = playback.CurrentRfid.HasValue
            ? playback.CurrentRfid.Value.ToString("X4", CultureInfo.InvariantCulture)
            : "--";
        ScenarioNextRfidTextBlock.Text = playback.NextRfid.HasValue
            ? playback.NextRfid.Value.ToString("X4", CultureInfo.InvariantCulture)
            : "--";

        if (playback.IsPlaying && _selectedStation.NextScenarioStepAt.HasValue)
        {
            var remaining = Math.Max(0, (_selectedStation.NextScenarioStepAt.Value - DateTimeOffset.UtcNow).TotalSeconds);
            ScenarioDistanceTextBlock.Text = $"{remaining:0.0}s";
        }
        else if (playback.CurrentIndex >= playback.Sequence.Count && playback.Sequence.Count > 0)
        {
            ScenarioDistanceTextBlock.Text = "完成";
        }
        else
        {
            ScenarioDistanceTextBlock.Text = "--";
        }
    }

    private void ClearSelectedStationSlots()
    {
        _selectedStation?.ClearSlots();
        LoadSelectedSlotsIntoDetails();
        UpdateStationSummaries();
    }

    private void ScanNextTag()
    {
        if (_selectedStation is null)
        {
            return;
        }
        if (!RfidValueParser.TryParse(ScanRfidTextBox.Text, out var rfid) || rfid == 0)
        {
            InteractionErrorTextBlock.Text = "扫入标签必须是非零 RFID，例如 001D。";
            return;
        }
        if (_rfidTextBoxes.Any(box => RfidValueParser.TryParse(box.Text, out var current) && current == rfid))
        {
            InteractionErrorTextBlock.Text = $"标签 {rfid:X4} 已存在，重复标签不会增加车辆节数。";
            return;
        }

        var emptyIndex = _rfidTextBoxes
            .Select((box, index) => new { box, index })
            .FirstOrDefault(item => string.IsNullOrWhiteSpace(item.box.Text))?.index ?? -1;
        if (emptyIndex < 0)
        {
            InteractionErrorTextBlock.Text = "14 个 RFID 槽已全部占用。";
            return;
        }

        SetRfid(emptyIndex, rfid.ToString("X4", CultureInfo.InvariantCulture));
        SyncSelectedSlotsFromDetails();
        InteractionErrorTextBlock.Text = string.Empty;
        HighlightSlot(_rfidTextBoxes[emptyIndex]);
        UpdateStationSummaries();
    }

    private void RemoveTag()
    {
        if (_selectedStation is null)
        {
            return;
        }
        if (!RfidValueParser.TryParse(RemoveRfidTextBox.Text, out var rfid))
        {
            InteractionErrorTextBlock.Text = "移除标签格式无效，例如 001D。";
            return;
        }

        var index = _rfidTextBoxes
            .Select((box, itemIndex) => new { box, itemIndex })
            .FirstOrDefault(item => RfidValueParser.TryParse(item.box.Text, out var current) && current == rfid)?.itemIndex ?? -1;
        if (index < 0)
        {
            InteractionErrorTextBlock.Text = $"未找到标签 {rfid:X4}。";
            return;
        }

        SetRfid(index, string.Empty);
        SyncSelectedSlotsFromDetails();
        InteractionErrorTextBlock.Text = string.Empty;
        UpdateStationSummaries();
    }

    private void SetRfid(int index, string value) => _rfidTextBoxes[index].Text = value;

    private async Task StartSelectedStation()
    {
        if (_selectedStation is null)
        {
            SetConfigError("请先选择一个虚拟基站。");
            return;
        }
        if (!ValidateConfiguration(out var error))
        {
            SetConfigError(error);
            return;
        }

        SyncSelectedSlotsFromDetails();
        await _selectedStation.StartAsync();
        UpdateSelectedTelemetry();
        UpdateStationSummaries();
    }

    private void StopSelectedStation()
    {
        _selectedStation?.Stop();
        UpdateSelectedTelemetry();
        UpdateStationSummaries();
    }

    private async Task StartAllStations()
    {
        foreach (var station in Stations)
        {
            await station.StartAsync();
        }
        UpdateSelectedTelemetry();
        UpdateStationSummaries();
    }

    private async Task StartSelectedYardStations()
    {
        foreach (var station in Stations.Where(IsStationInSelectedYard))
        {
            await station.StartAsync();
        }

        UpdateSelectedTelemetry();
        UpdateStationSummaries();
        StatusTextBlock.Text = $"{_selectedYardId} 站场虚拟基站已启动。";
    }

    private void StopAllStations()
    {
        foreach (var station in Stations)
        {
            station.Stop();
        }
        UpdateSelectedTelemetry();
        UpdateStationSummaries();
    }

    private void StopSelectedYardStations()
    {
        foreach (var station in Stations.Where(IsStationInSelectedYard))
        {
            station.Stop();
        }

        UpdateSelectedTelemetry();
        UpdateStationSummaries();
        StatusTextBlock.Text = $"{_selectedYardId} 站场虚拟基站已停止。";
    }

    private void ClearAllStations()
    {
        foreach (var station in Stations)
        {
            station.ClearSlots();
        }
        LoadSelectedSlotsIntoDetails();
        UpdateStationSummaries();
    }

    private void ResetAllScenarios()
    {
        foreach (var station in Stations)
        {
            station.ResetScenario();
        }
        LoadSelectedSlotsIntoDetails();
        UpdateScenarioPlaybackStatus();
        UpdateStationSummaries();
    }

    private void ApplyConfiguration()
    {
        if (_selectedStation is null)
        {
            SetConfigError("请先选择一个虚拟基站。");
            return;
        }
        if (!TryBuildConfiguration(out var config, out var slots, out var error))
        {
            _selectedStation.Stop();
            SetConfigError(error);
            UpdateSelectedTelemetry();
            UpdateStationSummaries();
            return;
        }

        var applied = _selectedStation.TryApplyConfiguration(config, out error);
        _selectedStation.SetSlots(slots);
        SaveStationConfiguration();
        SetConfigError(error);
        ErrorTextBlock.Text = error;
        StatusTextBlock.Text = applied ? "当前基站配置已应用。" : error;
        UpdateSelectedTelemetry();
        UpdateStationSummaries();
    }

    private bool ValidateConfiguration(out string error)
    {
        return TryBuildConfiguration(out _, out _, out error);
    }

    private bool TryBuildConfiguration(out SimulatorStationConfig config, out ushort[] slots, out string error)
    {
        config = new SimulatorStationConfig();
        slots = new ushort[14];
        error = string.Empty;
        if (_selectedStation is null)
        {
            error = "请先选择一个虚拟基站。";
            return false;
        }
        if (!IPAddress.TryParse(LocalIpTextBox.Text.Trim(), out _))
        {
            error = "监听地址格式无效。";
            return false;
        }
        if (!int.TryParse(LocalPortTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            error = "监听端口必须是 1-65535。";
            return false;
        }
        var addressText = AddressTextBox.Text.Trim();
        if (addressText.Length != 2 || !byte.TryParse(addressText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address))
        {
            error = "协议地址必须是两位十六进制值，例如 01。";
            return false;
        }
        if (!RfidValueParser.TryParse(EmptySlotTextBox.Text, out var emptySlot))
        {
            error = "空槽填充值格式无效。";
            return false;
        }
        if (!TryParseByte(CrcHighTextBox.Text, out var crcHigh) || !TryParseByte(CrcLowTextBox.Text, out var crcLow))
        {
            error = "CRC高/低位必须是两位十六进制值。";
            return false;
        }

        for (var index = 0; index < 14; index++)
        {
            var text = _rfidTextBoxes[index].Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                slots[index] = emptySlot;
                continue;
            }
            if (!RfidValueParser.TryParse(text, out slots[index]))
            {
                error = $"RFID 第{index + 1:00}项非法：{text}";
                return false;
            }
        }

        config = new SimulatorStationConfig
        {
            YardId = _selectedStation.Config.YardId,
            StationName = _selectedStation.Config.StationName,
            ListenIp = LocalIpTextBox.Text.Trim(),
            ListenPort = port,
            ProtocolAddress = address,
            Enabled = StationEnabledCheckBox.IsChecked == true,
            EmptySlotValue = emptySlot,
            CrcHigh = crcHigh,
            CrcLow = crcLow
        };
        return true;
    }

    private static bool TryParseByte(string text, out byte value)
    {
        value = 0;
        return text.Trim().Length == 2 && byte.TryParse(text.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private void SyncSelectedSlotsFromDetails()
    {
        if (_selectedStation is null || !TryReadSlots(out var slots, out _))
        {
            return;
        }
        _selectedStation.SetSlots(slots);
        UpdatePreview();
    }

    private bool TryReadSlots(out ushort[] slots, out string error)
    {
        slots = new ushort[14];
        error = string.Empty;
        if (!RfidValueParser.TryParse(EmptySlotTextBox.Text, out var emptySlot))
        {
            error = "空槽填充值格式无效。";
            return false;
        }

        for (var index = 0; index < 14; index++)
        {
            if (string.IsNullOrWhiteSpace(_rfidTextBoxes[index].Text))
            {
                slots[index] = emptySlot;
                continue;
            }
            if (!RfidValueParser.TryParse(_rfidTextBoxes[index].Text, out slots[index]))
            {
                error = $"RFID 第{index + 1:00}项非法。";
                return false;
            }
        }
        return true;
    }

    private void OnEndpointTextChanged(object sender, TextChangedEventArgs e)
    {
        if (TopListenerText is null || LocalIpTextBox is null || LocalPortTextBox is null)
        {
            return;
        }
        TopListenerText.Text = $"监听 {LocalIpTextBox.Text.Trim()}:{LocalPortTextBox.Text.Trim()}";
    }

    private void OnConfigurationTextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void OnFaultModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingStation || _selectedStation is null)
        {
            return;
        }

        TryApplyFaultConfigurationFromUi();
    }

    private void OnFaultDelayTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingStation || _selectedStation is null)
        {
            return;
        }

        TryApplyFaultConfigurationFromUi();
    }

    private void RestoreSelectedStationFault()
    {
        if (_selectedStation is null)
        {
            return;
        }

        _selectedStation.SetFaultConfiguration(new SimulatorFaultConfiguration(SimulatorFaultMode.Normal));
        _loadingStation = true;
        FaultModeComboBox.SelectedValue = SimulatorFaultMode.Normal.ToString();
        FaultDelayTextBox.Text = "0";
        _loadingStation = false;
        FaultConfigErrorTextBlock.Text = string.Empty;
        UpdateFaultInjectionState(_selectedStation.FaultConfiguration);
    }

    private void TryApplyFaultConfigurationFromUi()
    {
        if (_selectedStation is null || FaultModeComboBox.SelectedValue is not string modeText ||
            !Enum.TryParse<SimulatorFaultMode>(modeText, out var mode))
        {
            return;
        }

        var delay = 0;
        if (mode == SimulatorFaultMode.Delay &&
            (!int.TryParse(FaultDelayTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out delay) ||
             delay is < 0 or > 10000))
        {
            FaultConfigErrorTextBlock.Text = "故障延迟必须是 0-10000 毫秒。";
            return;
        }

        _selectedStation.SetFaultConfiguration(new SimulatorFaultConfiguration(mode, delay));
        FaultConfigErrorTextBlock.Text = string.Empty;
        UpdateFaultInjectionState(_selectedStation.FaultConfiguration);
    }

    private void UpdateFaultInjectionState(SimulatorFaultConfiguration configuration)
    {
        FaultStateTextBlock.Text = configuration.Mode switch
        {
            SimulatorFaultMode.Normal => "正常 · 立即响应",
            SimulatorFaultMode.Drop => "丢包 · 不返回响应",
            SimulatorFaultMode.Delay => $"延迟 · {configuration.DelayMilliseconds} ms",
            SimulatorFaultMode.InvalidFrame => "非法帧 · 帧头损坏",
            _ => "未知"
        };
        FaultStateTextBlock.Foreground = new SolidColorBrush(configuration.Mode switch
        {
            SimulatorFaultMode.Normal => Color.FromRgb(99, 230, 176),
            SimulatorFaultMode.Drop => Color.FromRgb(255, 135, 149),
            SimulatorFaultMode.Delay => Color.FromRgb(241, 216, 74),
            SimulatorFaultMode.InvalidFrame => Color.FromRgb(255, 135, 149),
            _ => Color.FromRgb(154, 174, 187)
        });
        FaultDelayTextBox.IsEnabled = configuration.Mode == SimulatorFaultMode.Delay;
    }

    private void SetConfigError(string error)
    {
        if (ConfigErrorTextBlock is not null)
        {
            ConfigErrorTextBlock.Text = error;
        }
    }

    private void UpdateSelectedStationDetails()
    {
        if (_selectedStation is null || _rfidTextBoxes.Count != 14)
        {
            return;
        }

        _loadingStation = true;
        var config = _selectedStation.Config;
        CurrentStationNameTextBlock.Text = config.StationName;
        StationEnabledCheckBox.IsChecked = config.Enabled;
        LocalIpTextBox.Text = config.ListenIp;
        LocalPortTextBox.Text = config.ListenPort.ToString(CultureInfo.InvariantCulture);
        AddressTextBox.Text = config.ProtocolAddress.ToString("X2", CultureInfo.InvariantCulture);
        EmptySlotTextBox.Text = config.EmptySlotValue.ToString("X4", CultureInfo.InvariantCulture);
        CrcHighTextBox.Text = config.CrcHigh.ToString("X2", CultureInfo.InvariantCulture);
        CrcLowTextBox.Text = config.CrcLow.ToString("X2", CultureInfo.InvariantCulture);
        var faultConfiguration = _selectedStation.FaultConfiguration;
        FaultModeComboBox.SelectedValue = faultConfiguration.Mode.ToString();
        FaultDelayTextBox.Text = faultConfiguration.DelayMilliseconds.ToString(CultureInfo.InvariantCulture);
        LoadSelectedSlotsIntoDetails();
        _loadingStation = false;
        SetConfigError(_selectedStation.ErrorMessage);
        FaultConfigErrorTextBlock.Text = string.Empty;
        UpdateFaultInjectionState(faultConfiguration);
        UpdatePreview();
        UpdateSelectedTelemetry();
    }

    private void LoadSelectedSlotsIntoDetails()
    {
        if (_selectedStation is null || _rfidTextBoxes.Count != 14)
        {
            return;
        }

        _loadingStation = true;
        var emptySlot = _selectedStation.Config.EmptySlotValue;
        var slots = _selectedStation.SnapshotSlots();
        for (var index = 0; index < _rfidTextBoxes.Count; index++)
        {
            var value = slots[index];
            SetRfid(index, value == emptySlot || value == 0 ? string.Empty : value.ToString("X4", CultureInfo.InvariantCulture));
        }
        _loadingStation = false;
        UpdatePreview();
    }

    private void UpdateSelectedTelemetry()
    {
        UpdateHeaderSummary();
        var station = _selectedStation;
        if (station is null)
        {
            return;
        }

        CurrentStationNameTextBlock.Text = station.Config.StationName;
        CurrentStationStatusText.Text = station.StatusText;
        var statusColor = !string.IsNullOrWhiteSpace(station.ErrorMessage)
            ? Color.FromRgb(255, 135, 149)
            : station.IsRunning
                ? Color.FromRgb(99, 230, 176)
                : Color.FromRgb(241, 216, 74);
        CurrentStationStatusText.Foreground = new SolidColorBrush(statusColor);
        CurrentStationStatusBorder.Background = new SolidColorBrush(Color.FromArgb(
            255,
            (byte)Math.Max(0, statusColor.R / 5),
            (byte)Math.Max(0, statusColor.G / 5),
            (byte)Math.Max(0, statusColor.B / 5)));
        CurrentStationStatusBorder.BorderBrush = new SolidColorBrush(statusColor);
        TopListenerText.Text = $"监听 {station.Endpoint}";
        TopAddressText.Text = $"Addr {station.AddressText}";
        TopRunStateText.Text = station.StatusText;
        TopRunStateText.Foreground = new SolidColorBrush(station.IsRunning
            ? Color.FromRgb(99, 230, 176)
            : Color.FromRgb(154, 174, 187));
        HeaderStatusText.Text = station.IsRunning ? "运行中" : "已就绪";
        TopReadyText.Text = station.IsRunning ? "运行" : "已就绪";
        TopReadyText.Foreground = new SolidColorBrush(station.IsRunning
            ? Color.FromRgb(99, 230, 176)
            : Color.FromRgb(154, 174, 187));
        OccupiedCountTextBlock.Text = $"{station.ValidRfidCount} / 14";
        ValidRfidCountTextBlock.Text = station.ValidRfidCount.ToString(CultureInfo.InvariantCulture);
        CurrentScenarioTextBlock.Text = station.ScenarioName;
        SendStateSummaryText.Text = station.IsRunning ? "● 运行中 · 等待上位机请求" : "● 等待上位机请求";
        SendStateSummaryText.Foreground = new SolidColorBrush(station.IsRunning
            ? Color.FromRgb(99, 230, 176)
            : Color.FromRgb(154, 174, 187));
        StatusTextBlock.Text = station.StatusText;
        ErrorTextBlock.Text = station.ErrorMessage;
        RecentRequestTimeText.Text = station.LastRequestAt?.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "-";
        RecentCommandText.Text = station.LastCommand;
        RequestSourceText.Text = station.RequestSource;
        LogTextBox.Text = string.Join(Environment.NewLine, station.Logs);
        RawTrafficHexTextBox.Text = string.Join(Environment.NewLine, station.RawHexLogs);
        FrameParseTextBox.Text = station.FrameParseText;
        UpdateScenarioPlaybackStatus();
        UpdateResponderButtons(station.IsRunning);
    }

    private void UpdateHeaderSummary()
    {
        if (HeaderRunningStationsText is null || RequestCountText is null || ResponseCountText is null ||
            ClearCountText is null || ExceptionCountText is null || HeaderStatusDot is null)
        {
            return;
        }

        var runningCount = Stations.Count(station => station.IsRunning);
        HeaderRunningStationsText.Text = $"{runningCount} / {Stations.Count}";
        RequestCountText.Text = Stations.Sum(station => station.RequestCount).ToString(CultureInfo.InvariantCulture);
        ResponseCountText.Text = Stations.Sum(station => station.ResponseCount).ToString(CultureInfo.InvariantCulture);
        ClearCountText.Text = Stations.Sum(station => station.ClearCount).ToString(CultureInfo.InvariantCulture);
        ExceptionCountText.Text = Stations.Sum(station => station.ErrorCount).ToString(CultureInfo.InvariantCulture);
        HeaderStatusDot.Fill = new SolidColorBrush(runningCount > 0
            ? Color.FromRgb(99, 230, 176)
            : Color.FromRgb(154, 174, 187));
    }

    private void UpdateStationSummaries()
    {
        RefreshStationView();
    }

    private void UpdatePreview()
    {
        if (_rfidTextBoxes.Count != 14 || TxHexTextBox is null || _selectedStation is null)
        {
            return;
        }
        if (!TryBuildConfiguration(out var config, out var slots, out var error))
        {
            TxHexTextBox.Text = string.Empty;
            SetConfigError(error);
            UpdateSlotSummary();
            return;
        }

        SetConfigError(string.Empty);
        var frame = RfidResponseFrameBuilder.Build(new SimulatorFrameInput
        {
            Address = config.ProtocolAddress,
            CommandBytes = new byte[4],
            Slots = slots,
            EmptySlotValue = config.EmptySlotValue,
            CrcHigh = config.CrcHigh,
            CrcLow = config.CrcLow
        });
        TxHexTextBox.Text = BitConverter.ToString(frame).Replace('-', ' ');
        UpdateSlotSummary(config.EmptySlotValue);
        TopAddressText.Text = $"Addr {config.ProtocolAddress:X2}";
    }

    private void UpdateSlotSummary(ushort? emptySlotOverride = null)
    {
        if (OccupiedCountTextBlock is null || ValidRfidCountTextBlock is null)
        {
            return;
        }

        var emptySlot = emptySlotOverride ?? (RfidValueParser.TryParse(EmptySlotTextBox?.Text, out var parsed) ? parsed : (ushort)0);
        var count = _rfidTextBoxes.Count(box =>
            !string.IsNullOrWhiteSpace(box.Text) &&
            RfidValueParser.TryParse(box.Text, out var value) &&
            value != emptySlot && value != 0);
        OccupiedCountTextBlock.Text = $"{count} / 14";
        ValidRfidCountTextBlock.Text = count.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateSlotVisual(TextBox box, Ellipse indicator)
    {
        var occupied = !string.IsNullOrWhiteSpace(box.Text) && RfidValueParser.TryParse(box.Text, out _);
        box.Background = new SolidColorBrush(occupied ? Color.FromRgb(10, 34, 55) : Color.FromRgb(7, 25, 41));
        box.Foreground = new SolidColorBrush(occupied ? Color.FromRgb(234, 246, 255) : Color.FromRgb(126, 158, 174));
        indicator.Fill = occupied
            ? new SolidColorBrush(Color.FromRgb(99, 230, 176))
            : Brushes.Transparent;
    }

    private void HighlightSlot(TextBox box)
    {
        _highlightedSlot = box;
        box.Background = new SolidColorBrush(Color.FromRgb(18, 77, 96));
        _slotHighlightTimer.Stop();
        _slotHighlightTimer.Start();
    }

    private void OnSlotHighlightTimerTick(object? sender, EventArgs e)
    {
        _slotHighlightTimer.Stop();
        if (_highlightedSlot is null)
        {
            return;
        }

        var index = _rfidTextBoxes.IndexOf(_highlightedSlot);
        if (index >= 0 && index < _rfidIndicators.Count)
        {
            UpdateSlotVisual(_highlightedSlot, _rfidIndicators[index]);
        }
        _highlightedSlot = null;
    }

    private void ClearLogs()
    {
        _selectedStation?.ClearLogs();
        UpdateSelectedTelemetry();
    }

    private void SaveLog()
    {
        if (_selectedStation is null || _selectedStation.Logs.Count == 0)
        {
            StatusTextBlock.Text = "暂无可保存的日志。";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "保存 Simulator 日志",
            Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"MineRailMonitor-Simulator-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, string.Join(Environment.NewLine, _selectedStation.Logs), new UTF8Encoding(false));
            StatusTextBlock.Text = $"日志已保存：{dialog.FileName}";
            ErrorTextBlock.Text = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorTextBlock.Text = $"保存日志失败：{exception.Message}";
        }
    }

    private void SaveStationConfiguration()
    {
        if (Stations.Count == 0)
        {
            return;
        }

        SimulatorStationPersistence.Save(_stationPersistencePath, Stations.Select(station => station.Config));
    }

    private void SaveStationConfigurationManually()
    {
        try
        {
            SaveStationConfiguration();
            StatusTextBlock.Text = $"基站配置已保存：{_stationPersistencePath}";
            ErrorTextBlock.Text = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorTextBlock.Text = $"保存基站配置失败：{exception.Message}";
        }
    }

    private void UpdateResponderButtons(bool running)
    {
        if (SendOnceButton is null || StartLoopButton is null || StopLoopButton is null)
        {
            return;
        }

        SendOnceButton.IsEnabled = !running;
        StartLoopButton.IsEnabled = !running;
        StopLoopButton.IsEnabled = running;
    }

    private static string GetStationPersistencePath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MineRailMonitor.Simulator",
        "stations.json");
}
