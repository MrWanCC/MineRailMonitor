using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MineRailMonitor.Simulator.Communication;
using MineRailMonitor.Simulator.Models;
using MineRailMonitor.Simulator.Protocol;

namespace MineRailMonitor.Simulator;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TextBox> _rfidTextBoxes = new();
    private readonly SimulatorFrameInput[] _stationInputs = Enumerable.Range(0, 6)
        .Select(_ => new SimulatorFrameInput { Slots = new ushort[14] })
        .ToArray();
    private readonly bool[] _stationConfigured = new bool[6];
    private readonly SimulatorStationCatalog _stationCatalog = new();
    private SimulatorUdpResponder? _responder;
    private CancellationTokenSource? _responderCts;
    private int _selectedStationIndex;
    private bool _loadingStation;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => StopResponder();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildRfidInputs();
        HookButtons();
        StationSelectorComboBox.SelectionChanged += OnStationSelectionChanged;
        LoadStationProfile(0);
        ApplyNormalPreset();
        UpdatePreview();
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

        for (var i = 0; i < 14; i++)
        {
            AddInput($"RFID{i + 1:00}", string.Empty);
        }
    }

    private void AddInput(string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        var box = new TextBox { Text = value };
        box.TextChanged += (_, _) => UpdatePreview();
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        _rfidTextBoxes.Add(box);
        RfidInputsPanel.Children.Add(row);
    }

    private void HookButtons()
    {
        NormalPresetButton.Click += (_, _) => ApplyNormalPreset();
        TenWagonPresetButton.Click += (_, _) => ApplyTenWagonPreset();
        ShortPresetButton.Click += (_, _) => ApplyShortPreset();
        DuplicatePresetButton.Click += (_, _) => ApplyDuplicatePreset();
        MultiHeadPresetButton.Click += (_, _) => ApplyMultiHeadPreset();
        NoHeadPresetButton.Click += (_, _) => ApplyNoHeadPreset();
        FirstNonHeadPresetButton.Click += (_, _) => ApplyFirstNonHeadPreset();
        ClearButton.Click += (_, _) => ClearInputs();
        ScanNextButton.Click += (_, _) => ScanNextTag();
        RemoveTagButton.Click += (_, _) => RemoveTag();
        SendOnceButton.Click += (_, _) => StartResponder();
        StartLoopButton.Click += (_, _) => StartResponder();
        StopLoopButton.Click += (_, _) => StopResponder();
    }

    private void ApplyNormalPreset()
    {
        SetRfid(0, "0001");
        for (var i = 1; i <= 10; i++)
        {
            SetRfid(i, $"{0x10 + i:X4}");
        }
        for (var i = 11; i < _rfidTextBoxes.Count; i++)
        {
            SetRfid(i, string.Empty);
        }
        UpdatePreview();
    }

    private void ApplyTenWagonPreset()
    {
        SetRfid(0, "0001");
        for (var i = 1; i <= 9; i++)
        {
            SetRfid(i, $"{0x10 + i:X4}");
        }
        for (var i = 10; i < _rfidTextBoxes.Count; i++)
        {
            SetRfid(i, string.Empty);
        }
        UpdatePreview();
    }

    private void ApplyShortPreset()
    {
        SetRfid(0, "0001");
        for (var i = 1; i <= 5; i++)
        {
            SetRfid(i, $"{0x10 + i:X4}");
        }
        for (var i = 6; i < _rfidTextBoxes.Count; i++)
        {
            SetRfid(i, string.Empty);
        }
        UpdatePreview();
    }

    private void ApplyDuplicatePreset()
    {
        SetRfid(0, "0001");
        var values = new[] { "0011", "0012", "0013", "0013", "0014", "0015" };
        for (var i = 1; i <= 6; i++)
        {
            SetRfid(i, values[i - 1]);
        }
        for (var i = 7; i < _rfidTextBoxes.Count; i++)
        {
            SetRfid(i, string.Empty);
        }
        UpdatePreview();
    }

    private void ApplyMultiHeadPreset()
    {
        SetRfid(0, "0001");
        SetRfid(1, "0003");
        for (var i = 2; i <= 10; i++)
        {
            SetRfid(i, $"{0x09 + i:X4}");
        }
        for (var i = 11; i < _rfidTextBoxes.Count; i++)
        {
            SetRfid(i, string.Empty);
        }
        UpdatePreview();
    }

    private void ApplyNoHeadPreset()
    {
        for (var i = 0; i <= 10; i++)
        {
            SetRfid(i, $"{0x0B + i:X4}");
        }
        for (var i = 11; i < _rfidTextBoxes.Count; i++)
        {
            SetRfid(i, string.Empty);
        }
        UpdatePreview();
    }

    private void ApplyFirstNonHeadPreset()
    {
        SetRfid(0, "001A");
        SetRfid(1, "0001");
        for (var i = 2; i <= 10; i++)
        {
            SetRfid(i, $"{0x0F + i:X4}");
        }
        for (var i = 11; i < _rfidTextBoxes.Count; i++)
        {
            SetRfid(i, string.Empty);
        }
        UpdatePreview();
    }

    private void ClearInputs()
    {
        foreach (var box in _rfidTextBoxes)
        {
            box.Text = string.Empty;
        }
        UpdatePreview();
    }

    private void ScanNextTag()
    {
        if (!RfidValueParser.TryParse(ScanRfidTextBox.Text, out var rfid) || rfid == 0)
        {
            ErrorTextBlock.Text = "扫入标签必须是非零 RFID，例如 001D。";
            return;
        }

        if (_rfidTextBoxes.Any(box => RfidValueParser.TryParse(box.Text, out var current) && current == rfid))
        {
            ErrorTextBlock.Text = $"标签 {rfid:X4} 已存在，重复标签不会增加车辆节数。";
            return;
        }

        var emptyIndex = _rfidTextBoxes
            .Select((box, index) => new { box, index })
            .FirstOrDefault(item => string.IsNullOrWhiteSpace(item.box.Text))?.index ?? -1;
        if (emptyIndex < 0)
        {
            ErrorTextBlock.Text = "14 个 RFID 槽已全部占用。";
            return;
        }

        SetRfid(emptyIndex, rfid.ToString("X4"));
        ErrorTextBlock.Text = string.Empty;
        UpdatePreview();
    }

    private void RemoveTag()
    {
        if (!RfidValueParser.TryParse(RemoveRfidTextBox.Text, out var rfid))
        {
            ErrorTextBlock.Text = "移除标签格式无效，例如 001D。";
            return;
        }

        var index = _rfidTextBoxes
            .Select((box, itemIndex) => new { box, itemIndex })
            .FirstOrDefault(item => RfidValueParser.TryParse(item.box.Text, out var current) && current == rfid)?.itemIndex ?? -1;
        if (index < 0)
        {
            ErrorTextBlock.Text = $"未找到标签 {rfid:X4}。";
            return;
        }

        SetRfid(index, string.Empty);
        ErrorTextBlock.Text = string.Empty;
        UpdatePreview();
    }

    private void SetRfid(int index, string value) => _rfidTextBoxes[index].Text = value;

    private void StartResponder()
    {
        StopResponder();
        SaveCurrentStationProfile();
        _stationCatalog.Clear();
        for (var index = 0; index < _stationInputs.Length; index++)
        {
            if (!_stationConfigured[index])
            {
                continue;
            }
            var configured = _stationInputs[index];
            try
            {
                _stationCatalog.Set(index, new SimulatorStation
                {
                    Address = configured.Address,
                    Slots = (ushort[])configured.Slots.Clone(),
                    CommandBytes = (byte[])configured.CommandBytes.Clone(),
                    CrcHigh = configured.CrcHigh,
                    CrcLow = configured.CrcLow
                });
            }
            catch (ArgumentException exception)
            {
                ErrorTextBlock.Text = exception.Message;
                return;
            }
        }
        var configuredStations = _stationCatalog.ConfiguredStations.ToArray();
        if (configuredStations.Length == 0)
        {
            ErrorTextBlock.Text = "请至少为一个模拟基站填写地址。";
            return;
        }
        var hasCurrentInput = TryBuildInput(out var currentInput, out _);
        var emptySlotValue = hasCurrentInput ? currentInput.EmptySlotValue : (ushort)0;
        ErrorTextBlock.Text = string.Empty;
        try
        {
            var localIp = IPAddress.Parse(LocalIpTextBox.Text);
            var localPort = int.Parse(LocalPortTextBox.Text, CultureInfo.InvariantCulture);
            var responder = new RfidSimulatorResponder(configuredStations, emptySlotValue);
            responder.UnknownAddressReceived += address =>
                System.Diagnostics.Debug.WriteLine($"Simulator 收到未知 RFID 地址：{address:X2}，不返回数据。");
            _responder = new SimulatorUdpResponder(localIp, localPort);
            _responderCts = new CancellationTokenSource();
            _ = _responder.RunAsync(responder.CreateResponse, _responderCts.Token);
        }
        catch (Exception exception)
        {
            ErrorTextBlock.Text = exception.Message;
            return;
        }
        ErrorTextBlock.Text = string.Empty;
        StatusTextBlock.Text = "查询应答已启动，等待上位机请求";
        HeaderStatusText.Text = "查询应答 · UDP 正常";
        HeaderStatusDot.Fill = new SolidColorBrush(Color.FromRgb(99, 230, 176));
    }

    private void StopResponder()
    {
        _responderCts?.Cancel();
        _responderCts?.Dispose();
        _responderCts = null;
        _responder?.Dispose();
        _responder = null;
        StatusTextBlock.Text = "查询应答已停止";
        HeaderStatusText.Text = "已停止 · 可发送";
        HeaderStatusDot.Fill = new SolidColorBrush(Color.FromRgb(224, 164, 63));
    }

    private bool TryBuildInput(out SimulatorFrameInput input, out string error)
    {
        input = new SimulatorFrameInput();
        error = string.Empty;

        if (!byte.TryParse(AddressTextBox.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address))
        {
            error = "分站地址必须是两位十六进制值。";
            return false;
        }

        input.Address = address;

        if (!RfidValueParser.TryParse(EmptySlotTextBox.Text, out var emptySlot))
        {
            error = "空槽填充值格式无效。";
            return false;
        }

        input.EmptySlotValue = emptySlot;
        if (!byte.TryParse(CrcHighTextBox.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var crcHigh) ||
            !byte.TryParse(CrcLowTextBox.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var crcLow))
        {
            error = "CRC高/低位必须是两位十六进制值。";
            return false;
        }

        input.CrcHigh = crcHigh;
        input.CrcLow = crcLow;

        var rfids = new ushort[14];
        for (var i = 0; i < 14; i++)
        {
            var text = _rfidTextBoxes[i].Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                rfids[i] = emptySlot;
                continue;
            }

            if (!RfidValueParser.TryParse(text, out rfids[i]))
            {
                error = $"RFID 第{i + 1:00}项非法：{text}";
                return false;
            }
        }

        input.Slots = rfids;
        return true;
    }

    private void OnStationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingStation || StationSelectorComboBox.SelectedIndex < 0)
        {
            return;
        }
        SaveCurrentStationProfile();
        LoadStationProfile(StationSelectorComboBox.SelectedIndex);
    }

    private void SaveCurrentStationProfile()
    {
        if (_loadingStation || !TryBuildInput(out var input, out _))
        {
            return;
        }
        _stationInputs[_selectedStationIndex] = input;
        _stationConfigured[_selectedStationIndex] = true;
    }

    private void LoadStationProfile(int index)
    {
        _selectedStationIndex = index;
        var input = _stationInputs[index];
        _loadingStation = true;
        AddressTextBox.Text = _stationConfigured[index] ? input.Address.ToString("X2") : string.Empty;
        EmptySlotTextBox.Text = input.EmptySlotValue.ToString("X4");
        CrcHighTextBox.Text = input.CrcHigh.ToString("X2");
        CrcLowTextBox.Text = input.CrcLow.ToString("X2");
        for (var item = 0; item < _rfidTextBoxes.Count; item++)
        {
            var value = input.Slots[item];
            _rfidTextBoxes[item].Text = value == input.EmptySlotValue ? string.Empty : value.ToString("X4");
        }
        _loadingStation = false;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (!TryBuildInput(out var input, out var error))
        {
            TxHexTextBox.Text = string.Empty;
            ErrorTextBlock.Text = error;
            return;
        }

        ErrorTextBlock.Text = string.Empty;
        var frame = RfidResponseFrameBuilder.Build(input);
        TxHexTextBox.Text = BitConverter.ToString(frame).Replace('-', ' ');
        CounterTextBlock.Text = $"Byte7 上报数量：{frame[7]}    实际非零槽数量：{input.Slots.Count(value => value != input.EmptySlotValue)}    固定槽位：14";
    }

}
