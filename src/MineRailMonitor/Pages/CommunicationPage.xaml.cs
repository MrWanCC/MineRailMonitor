using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Core.Recognition;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Pages;

public partial class CommunicationPage : UserControl
{
    private readonly ObservableCollection<string> _packetLog = new();
    private readonly ObservableCollection<CommunicationLogRow> _communicationLogRows = new();
    private readonly ObservableCollection<string> _frameRfidSlots = new();
    private readonly Dictionary<RfidStationEndpointKey, long> _invalidFrameCounts = new();
    private readonly Dictionary<string, long> _timeoutLogCounts = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<RfidStationConfig> _stations = Array.Empty<RfidStationConfig>();
    private IReadOnlyList<StationConfig> _yardConfigs = Array.Empty<StationConfig>();
    private IReadOnlyList<RfidStationYardOption> _yardFilterOptions;
    private IReadOnlyList<RfidStationPollingStatus> _stationStatuses = Array.Empty<RfidStationPollingStatus>();
    private HashSet<string>? _displayScopeStationIds;
    private bool _isYardFilterSync;
    private bool _hasObservedStationStatuses;
    private string? _selectedStationId;
    private long _invalidFrameCount;
    private Func<RfidStationConfig, RfidPollCommand, Task>? _sendTestAsync;

    public CommunicationPage()
    {
        InitializeComponent();
        _yardFilterOptions = RfidYardFilter.BuildOptions(null, _stations);
        YardFilter.ItemsSource = _yardFilterOptions;
        YardFilter.SelectedIndex = 0;
        RxListBox.ItemsSource = _packetLog;
        CommunicationLogList.ItemsSource = _communicationLogRows;
        FrameRfidSlotsItemsControl.ItemsSource = _frameRfidSlots;
        SetListenerStatus("监听尚未启动");
        LatestRxText.Text = "暂无报文";
        NoStationText.Visibility = Visibility.Visible;
    }

    public void ConfigureStations(
        IEnumerable<RfidStationConfig> stations,
        Func<RfidStationConfig, RfidPollCommand, Task>? sendTestAsync = null)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));

        _stations = stations
            .Where(station => station is not null && !string.IsNullOrWhiteSpace(station.StationId))
            .ToArray();
        _sendTestAsync = sendTestAsync;
        _yardFilterOptions = RfidYardFilter.BuildOptions(_yardConfigs, _stations);
        SetYardFilterSelection(GetSelectedYardId());
        RebuildTestStationSelector();
        RefreshStationRows();
    }

    public void SetYardOptions(IEnumerable<StationConfig>? yards)
    {
        _yardConfigs = (yards ?? Array.Empty<StationConfig>())
            .Where(yard => yard is not null)
            .ToArray();
        _yardFilterOptions = RfidYardFilter.BuildOptions(_yardConfigs, _stations);
        SetYardFilterSelection(GetSelectedYardId());
        _displayScopeStationIds = RfidYardFilter.ResolveStationIds(GetSelectedYardId(), _stations);
        RebuildTestStationSelector();
        RefreshStationRows();
    }

    public void SetDisplayScope(IEnumerable<string>? stationIds, string? yardId = null)
    {
        var nextScope = stationIds is null
            ? null
            : new HashSet<string>(
                stationIds.Where(stationId => !string.IsNullOrWhiteSpace(stationId)),
                StringComparer.OrdinalIgnoreCase);
        SetYardFilterSelection(yardId ?? RfidYardFilter.ResolveYardId(stationIds, _stations) ?? RfidStationYardOption.AllId);
        if (!AreScopesEqual(_displayScopeStationIds, nextScope))
        {
            ResetScopePresentation();
        }

        _displayScopeStationIds = nextScope;

        RebuildTestStationSelector();
        RefreshStationRows();
    }

    private void RebuildTestStationSelector()
    {
        var selectedStationId = (TestStationSelector.SelectedItem as ComboBoxItem)?.Tag is RfidStationConfig selectedStation
            ? selectedStation.StationId
            : null;
        var visibleStations = GetVisibleStations();

        TestStationSelector.Items.Clear();
        foreach (var visibleStation in visibleStations)
        {
            TestStationSelector.Items.Add(new ComboBoxItem
            {
                Content = FormatStation(visibleStation),
                Tag = visibleStation
            });
        }

        var selectedItem = TestStationSelector.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is RfidStationConfig station &&
                string.Equals(station.StationId, selectedStationId, StringComparison.OrdinalIgnoreCase));
        TestStationSelector.SelectedItem = selectedItem ?? TestStationSelector.Items.Cast<object>().FirstOrDefault();
        ConfiguredStationCountText.Text = visibleStations.Count.ToString(CultureInfo.InvariantCulture);
        SelectedStationText.Text = TestStationSelector.SelectedItem is ComboBoxItem { Tag: RfidStationConfig station }
            ? FormatStation(station)
            : "-";
    }

    private void ResetScopePresentation()
    {
        ClearLog();
        LatestTxTimeText.Text = "-";
        LatestTxSummaryText.Text = "暂无发送报文";
        LatestTxLengthText.Text = "-";
        LatestTxHexText.Text = "-";
    }

    public void SetStationStatuses(IEnumerable<RfidStationPollingStatus> statuses)
    {
        if (statuses is null) throw new ArgumentNullException(nameof(statuses));

        var nextStatuses = statuses.Where(status => status is not null).ToArray();
        if (_hasObservedStationStatuses)
        {
            AddTimeoutLogEntries(nextStatuses);
        }

        foreach (var status in nextStatuses)
        {
            _timeoutLogCounts[GetStatusLogKey(status)] = status.TimeoutCount;
        }

        _hasObservedStationStatuses = true;
        _stationStatuses = nextStatuses;
        RefreshStationRows();
    }

    public void SetListenerStatus(string status, string? yardId = null)
    {
        if (!string.IsNullOrWhiteSpace(yardId) &&
            !string.Equals(GetSelectedYardId(), RfidStationYardOption.AllId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(GetSelectedYardId(), yardId!.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ListenerStatusText.Text = status;
        ListenerStatusDot.Fill = status.IndexOf("异常", StringComparison.Ordinal) >= 0
            ? (Brush)FindResource("AlarmBrush")
            : (Brush)FindResource("SuccessBrush");
    }

    public void SetAdminMode(bool isAdmin)
    {
        ClearTestButton.IsEnabled = isAdmin;
        ClearTestButton.ToolTip = isAdmin
            ? "发送清空命令前需要确认"
            : "进入管理员模式后可发送清空命令";
    }

    public void AddDatagram(
        RfidUdpDatagramEventArgs datagram,
        RfidStationFrame? parsedFrame = null,
        StationRecognitionSession? recognitionSession = null)
        => AddDatagram(null, datagram, parsedFrame, recognitionSession);

    public void AddDatagram(
        string? yardId,
        RfidUdpDatagramEventArgs datagram,
        RfidStationFrame? parsedFrame = null,
        StationRecognitionSession? recognitionSession = null)
    {
        if (!IsDatagramInDisplayScope(yardId, parsedFrame))
        {
            return;
        }

        var hex = BitConverter.ToString(datagram.Data).Replace('-', ' ');
        if (!datagram.IsValid)
        {
            _invalidFrameCount++;
            if (datagram.Data.Length >= 3)
            {
                var invalidKey = new RfidStationEndpointKey(datagram.RemoteEndPoint, datagram.Data[2]);
                _invalidFrameCounts[invalidKey] = GetInvalidFrameCount(invalidKey) + 1;
            }
        }

        LatestRxTimeText.Text = FormatTime(datagram.ReceivedAt);
        LatestPacketTimeText.Text = datagram.IsValid ? FormatTime(datagram.ReceivedAt) : "-";
        LatestRxSummaryText.Text = $"{FormatYard(yardId)}来源：{FormatEndpoint(datagram.RemoteEndPoint)}";
        LatestRxLengthText.Text = $"长度：{datagram.Data.Length} 字节 · {(datagram.IsValid ? "合法帧" : "非法帧")}";
        LatestRxHexText.Text = hex;
        UpdateCurrentFrameDisplay(datagram, parsedFrame, recognitionSession);

        var packetDescription = !datagram.IsValid
            ? datagram.ValidationError ?? "非法帧"
            : parsedFrame?.ProtocolDataWarning == true
                ? "数据告警"
                : parsedFrame is null
                    ? "收到报文"
                    : "收到有效响应";
        AddCommunicationLog(datagram.ReceivedAt, "RX", hex, packetDescription);

        var lines = new List<string>
        {
            $"RX {datagram.ReceivedAt:HH:mm:ss.fff}    {FormatYard(yardId)}{FormatEndpoint(datagram.RemoteEndPoint)}    {datagram.Data.Length} Bytes    {(datagram.IsValid ? "合法帧" : "非法帧")}",
            hex
        };

        if (parsedFrame is not null)
        {
            lines.Add($"分站地址：{parsedFrame.StationAddress:X2}");
            lines.Add($"模式：{parsedFrame.Mode:X2}");
            lines.Add($"上报数量：{parsedFrame.ReportedCardCount}");
            lines.Add($"实际有效数量：{parsedFrame.ActualNonZeroSlotCount}");
            lines.Add($"RFID 槽位：{string.Join("  ", parsedFrame.RawRfidSlots.Select((value, index) => $"RFID{index + 1}  {value:X4}"))}");
            if (recognitionSession is not null)
            {
                lines.Add($"当前列车：{recognitionSession.DetectedVehicleCount} / {recognitionSession.ExpectedVehicleCount}");
            }
            if (parsedFrame.ProtocolDataWarning)
            {
                lines.Add("协议数据告警：Byte7 上报数量与实际非零槽数量不一致");
            }
        }
        else if (datagram.IsValid && datagram.Data.Length > 3 && datagram.Data[3] != 0x04)
        {
            lines.Add("非 RFID 模式");
        }
        else if (!datagram.IsValid && datagram.ValidationError is not null)
        {
            lines.Add(datagram.ValidationError);
        }

        AddLog(string.Join(Environment.NewLine, lines));
        RefreshStationRows();
    }

    public void ClearLog()
    {
        _packetLog.Clear();
        _communicationLogRows.Clear();
        PacketLogCountText.Text = "0 / 50";
        LatestRxText.Text = "暂无报文";
        LatestRxTimeText.Text = "-";
        LatestRxSummaryText.Text = "暂无接收报文";
        LatestRxLengthText.Text = "-";
        LatestRxHexText.Text = "-";
        LatestPacketTimeText.Text = "-";
        ResetCurrentFrameDisplay();
        UpdateCommunicationStats();
    }

    public void SetError(Exception exception, string? yardId = null) =>
        SetListenerStatus($"监听异常：{exception.Message}", yardId);

    private async void OnReadTestClick(object sender, RoutedEventArgs e) => await SendSelectedAsync(RfidPollCommand.Read);

    private async void OnClearTestClick(object sender, RoutedEventArgs e) => await SendSelectedAsync(RfidPollCommand.Clear);

    private async void OnStationTestClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not StationStatusRow row)
        {
            return;
        }

        await SelectStationAndTestAsync(row);
    }

    private async void OnSelectedStationTestClick(object sender, RoutedEventArgs e)
    {
        if (StationStatusGrid.SelectedItem is StationStatusRow row)
        {
            await SelectStationAndTestAsync(row);
        }
    }

    private async Task SelectStationAndTestAsync(StationStatusRow row)
    {
        _selectedStationId = row.StationId;
        StationStatusGrid.SelectedItem = row;
        var selectorItem = TestStationSelector.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is RfidStationConfig station &&
                string.Equals(station.StationId, row.StationId, StringComparison.OrdinalIgnoreCase));
        if (selectorItem is not null)
        {
            TestStationSelector.SelectedItem = selectorItem;
        }

        await SendSelectedAsync(RfidPollCommand.Read);
    }

    private void OnClearLogClick(object sender, RoutedEventArgs e) => ClearLog();

    private void OnYardFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isYardFilterSync)
        {
            return;
        }

        var nextScope = RfidYardFilter.ResolveStationIds(GetSelectedYardId(), _stations);
        if (!AreScopesEqual(_displayScopeStationIds, nextScope))
        {
            ResetScopePresentation();
        }

        _displayScopeStationIds = nextScope;
        RebuildTestStationSelector();
        RefreshStationRows();
    }

    private void OnTestStationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TestStationSelector.SelectedItem is ComboBoxItem { Tag: RfidStationConfig station })
        {
            SelectedStationText.Text = FormatStation(station);
        }
    }

    private void OnStationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StationStatusGrid.SelectedItem is not StationStatusRow row)
        {
            UpdateSelectedDiagnostic(null, null);
            return;
        }

        _selectedStationId = row.StationId;
        UpdateSelectedDiagnostic(row.Station, row.Status);
    }

    private string GetSelectedYardId() =>
        (YardFilter.SelectedItem as RfidStationYardOption)?.Id ?? RfidStationYardOption.AllId;

    private void SetYardFilterSelection(string selectedYardId)
    {
        var selectedItem = _yardFilterOptions.FirstOrDefault(option =>
            string.Equals(option.Id, selectedYardId, StringComparison.OrdinalIgnoreCase))
            ?? _yardFilterOptions.FirstOrDefault();
        if (selectedItem is null)
        {
            return;
        }

        _isYardFilterSync = true;
        try
        {
            YardFilter.ItemsSource = _yardFilterOptions;
            YardFilter.SelectedItem = selectedItem;
        }
        finally
        {
            _isYardFilterSync = false;
        }
    }

    private async Task SendSelectedAsync(RfidPollCommand command)
    {
        if (TestStationSelector.SelectedItem is not ComboBoxItem { Tag: RfidStationConfig station })
        {
            SetListenerStatus("请先选择测试基站。");
            return;
        }

        if (_sendTestAsync is null)
        {
            SetListenerStatus("测试发送通道尚未就绪。");
            return;
        }

        if (command == RfidPollCommand.Clear && !ConfirmClearCommand(station))
        {
            return;
        }

        try
        {
            await _sendTestAsync(station, command);
            var commandText = command == RfidPollCommand.Read ? "读取" : "清空";
            var sentAt = DateTimeOffset.Now;
            var txHex = station.TryResolveEndpoint(out _)
                ? BitConverter.ToString(RfidRequestFrameBuilder.Build(station, command)).Replace('-', ' ')
                : "-";
            SelectedStationText.Text = FormatStation(station);
            LatestTxTimeText.Text = FormatTime(sentAt);
            LatestTxSummaryText.Text = $"发送至：{FormatEndpoint(station.TryResolveEndpoint(out var endpoint) ? endpoint : new IPEndPoint(IPAddress.None, 0))}";
            LatestTxLengthText.Text = $"命令：{commandText} · 协议地址 {station.ProtocolAddress:X2}";
            LatestTxHexText.Text = txHex;
            AddCommunicationLog(sentAt, "TX", txHex, $"发送{commandText}命令");
            AddLog($"TX {DateTime.Now:HH:mm:ss.fff}    {FormatStation(station)}    {commandText} 请求已发送");
            RefreshStationRows();
            SetListenerStatus($"测试报文已发送：{FormatStation(station)}");
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    private bool ConfirmClearCommand(RfidStationConfig station)
    {
        var dialog = new StyledMessageDialog(
            "确认发送清空命令",
            $"即将向 {FormatStation(station)} 发送清空命令。\n该操作会清除设备当前缓存数据，请确认站场和基站无误。",
            MessageDialogKind.Warning)
        {
            Owner = Window.GetWindow(this)
        };
        return dialog.ShowDialog() == true;
    }

    private void RefreshStationRows()
    {
        var visibleStations = GetVisibleStations();
        var rows = visibleStations.Select((station, index) =>
        {
            var status = _stationStatuses.FirstOrDefault(item => MatchesStation(item, station));
            return new StationStatusRow(station, status, index + 1, GetInvalidFrameCount(station));
        }).ToArray();

        StationStatusGrid.ItemsSource = rows;
        var selectedRow = rows.FirstOrDefault(row =>
            string.Equals(row.StationId, _selectedStationId, StringComparison.OrdinalIgnoreCase))
            ?? rows.FirstOrDefault();
        _selectedStationId = selectedRow?.StationId;
        StationStatusGrid.SelectedItem = selectedRow;
        UpdateSelectedDiagnostic(selectedRow?.Station, selectedRow?.Status);
        UpdateDiagnosticsOverview(visibleStations, rows);
        UpdateCommunicationStats();
        NoStationText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDiagnosticsOverview(
        IReadOnlyList<RfidStationConfig> visibleStations,
        IReadOnlyList<StationStatusRow> rows)
    {
        var statuses = rows
            .Select(row => row.Status)
            .Where(status => status is not null)
            .Cast<RfidStationPollingStatus>()
            .ToArray();
        var onlineCount = statuses.Count(status => status.IsOnline);
        var timeoutCount = statuses.Sum(status => status.TimeoutCount);
        var responseTimes = statuses
            .Where(status => status.LastResponseMilliseconds.HasValue)
            .Select(status => status.LastResponseMilliseconds!.Value)
            .ToArray();
        OverviewStationCountText.Text = visibleStations.Count.ToString(CultureInfo.InvariantCulture);
        OverviewOnlineCountText.Text = onlineCount.ToString(CultureInfo.InvariantCulture);
        OverviewOfflineCountText.Text = Math.Max(0, visibleStations.Count - onlineCount).ToString(CultureInfo.InvariantCulture);
        OverviewResponseTimeText.Text = responseTimes.Length == 0
            ? "-"
            : $"{responseTimes.Average().ToString("0", CultureInfo.InvariantCulture)}ms";
        OverviewTimeoutCountText.Text = timeoutCount.ToString(CultureInfo.InvariantCulture);
        OverviewTimeoutCountRun.Text = timeoutCount.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateSelectedDiagnostic(RfidStationConfig? station, RfidStationPollingStatus? status)
    {
        if (station is null)
        {
            SelectedDiagnosticStationText.Text = "-";
            SelectedDiagnosticBasicStationText.Text = "-";
            SelectedDiagnosticYardText.Text = "-";
            SelectedDiagnosticIpText.Text = "-";
            SelectedDiagnosticPortText.Text = "-";
            SelectedDiagnosticProtocolText.Text = "-";
            SelectedDiagnosticStateText.Text = "-";
            SelectedDiagnosticStateText.Foreground = (Brush)FindResource("TextSecondaryBrush");
            SelectedDiagnosticInnerStateText.Text = "-";
            SelectedDiagnosticInnerStateText.Foreground = (Brush)FindResource("TextSecondaryBrush");
            SelectedDiagnosticOfflineDurationText.Text = string.Empty;
            SelectedDiagnosticInnerOfflineDurationText.Text = "-";
            SelectedDiagnosticLastSentText.Text = "-";
            SelectedDiagnosticLastReceivedText.Text = "-";
            SelectedDiagnosticResponseText.Text = "-";
            SelectedDiagnosticConsecutiveTimeoutStatsText.Text = "0";
            SelectedDiagnosticInvalidText.Text = "0";
            SelectedDiagnosticErrorText.Text = "无";
            SelectedDiagnosticSentText.Text = "0";
            SelectedDiagnosticReceivedText.Text = "0";
            SelectedDiagnosticTimeoutText.Text = "0";
            SelectedDiagnosticCountsText.Text = "发送 0 · 接收 0 · 超时 0";
            return;
        }

        SelectedDiagnosticStationText.Text = FormatStation(station);
        SelectedDiagnosticBasicStationText.Text = FormatStation(station);
        SelectedDiagnosticYardText.Text = string.IsNullOrWhiteSpace(station.YardId) ? "-" : station.YardId;
        if (station.TryResolveEndpoint(out var endpoint))
        {
            SelectedDiagnosticIpText.Text = endpoint.Address.ToString();
            SelectedDiagnosticPortText.Text = endpoint.Port.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            SelectedDiagnosticIpText.Text = "端点无效";
            SelectedDiagnosticPortText.Text = "-";
        }

        SelectedDiagnosticProtocolText.Text = $"0x{station.ProtocolAddress:X2}";
        SelectedDiagnosticStateText.Text = FormatStatus(status);
        var stateBrush = status?.LastSentAt.HasValue != true
            ? (Brush)FindResource("TextSecondaryBrush")
            : status?.IsOnline == true
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("AlarmBrush");
        SelectedDiagnosticStateText.Foreground = stateBrush;
        SelectedDiagnosticInnerStateText.Text = FormatStatus(status);
        SelectedDiagnosticInnerStateText.Foreground = stateBrush;
        var offlineDuration = FormatOfflineDuration(status);
        SelectedDiagnosticOfflineDurationText.Text = offlineDuration;
        SelectedDiagnosticInnerOfflineDurationText.Text = offlineDuration.Length > 0
            ? offlineDuration.StartsWith("已离线：", StringComparison.Ordinal)
                ? offlineDuration.Substring(4)
                : offlineDuration
            : "-";
        SelectedDiagnosticLastSentText.Text = FormatTime(status?.LastSentAt);
        SelectedDiagnosticLastReceivedText.Text = FormatTime(status?.LastReceivedAt);
        SelectedDiagnosticResponseText.Text = status?.LastResponseMilliseconds is long milliseconds
            ? $"{milliseconds} ms"
            : "-";
        SelectedDiagnosticConsecutiveTimeoutStatsText.Text = (status?.ConsecutiveTimeoutCount ?? 0)
            .ToString(CultureInfo.InvariantCulture);
        SelectedDiagnosticInvalidText.Text = GetInvalidFrameCount(station)
            .ToString(CultureInfo.InvariantCulture);
        SelectedDiagnosticErrorText.Text = status?.LastError ?? "无";
        SelectedDiagnosticSentText.Text = (status?.SentCount ?? 0).ToString(CultureInfo.InvariantCulture);
        SelectedDiagnosticReceivedText.Text = (status?.ReceivedCount ?? 0).ToString(CultureInfo.InvariantCulture);
        SelectedDiagnosticTimeoutText.Text = (status?.TimeoutCount ?? 0).ToString(CultureInfo.InvariantCulture);
        SelectedDiagnosticCountsText.Text =
            $"发送 {status?.SentCount ?? 0} · 接收 {status?.ReceivedCount ?? 0} · 超时 {status?.TimeoutCount ?? 0}";
    }

    private void UpdateCommunicationStats()
    {
        var visibleStations = GetVisibleStations();
        var statuses = visibleStations
            .Select(station => _stationStatuses.FirstOrDefault(item => MatchesStation(item, station)))
            .Where(status => status is not null)
            .Cast<RfidStationPollingStatus>()
            .ToArray();
        var onlineCount = statuses.Count(status => status.IsOnline);

        TotalSentCountText.Text = statuses.Sum(status => status.SentCount).ToString(CultureInfo.InvariantCulture);
        TotalReceivedCountText.Text = statuses.Sum(status => status.ReceivedCount).ToString(CultureInfo.InvariantCulture);
        TotalTimeoutCountText.Text = statuses.Sum(status => status.TimeoutCount).ToString(CultureInfo.InvariantCulture);
        TotalInvalidFrameCountText.Text = _invalidFrameCount.ToString(CultureInfo.InvariantCulture);
        OnlineRateText.Text = visibleStations.Count == 0
            ? "0%"
            : $"{(onlineCount * 100d / visibleStations.Count).ToString("0.0", CultureInfo.InvariantCulture)}%";
    }

    private long GetInvalidFrameCount(RfidStationConfig station) =>
        station.TryResolveEndpoint(out var endpoint)
            ? GetInvalidFrameCount(new RfidStationEndpointKey(endpoint, station.ProtocolAddress))
            : 0;

    private long GetInvalidFrameCount(RfidStationEndpointKey key) =>
        _invalidFrameCounts.TryGetValue(key, out var count) ? count : 0;

    private IReadOnlyList<RfidStationConfig> GetVisibleStations()
    {
        var scopeStationIds = _displayScopeStationIds;
        return scopeStationIds is null
            ? _stations
            : _stations
                .Where(station => scopeStationIds.Contains(station.StationId))
                .ToArray();
    }

    private bool IsDatagramInDisplayScope(string? yardId, RfidStationFrame? parsedFrame)
    {
        var scopeStationIds = _displayScopeStationIds;
        if (scopeStationIds is null || string.IsNullOrWhiteSpace(yardId))
        {
            return true;
        }

        var normalizedYardId = yardId!.Trim();
        var yardStations = _stations.Where(station =>
            string.Equals(station.YardId?.Trim(), normalizedYardId, StringComparison.OrdinalIgnoreCase));
        if (parsedFrame is null)
        {
            return yardStations.Any(station => scopeStationIds.Contains(station.StationId));
        }

        return yardStations.Any(station =>
            station.ProtocolAddress == parsedFrame.StationAddress &&
            scopeStationIds.Contains(station.StationId));
    }

    private static bool MatchesStation(RfidStationPollingStatus status, RfidStationConfig station)
    {
        if (status.EndpointKey.HasValue && station.TryResolveEndpoint(out var endpoint))
        {
            return status.EndpointKey.Value == new RfidStationEndpointKey(endpoint, station.ProtocolAddress);
        }

        return !string.IsNullOrWhiteSpace(status.StationId) &&
            string.Equals(status.StationId, station.StationId, StringComparison.OrdinalIgnoreCase);
    }

    private void AddLog(string line)
    {
        LatestRxText.Text = line;
        _packetLog.Insert(0, line);
        while (_packetLog.Count > 50)
        {
            _packetLog.RemoveAt(_packetLog.Count - 1);
        }

        PacketLogCountText.Text = $"{_packetLog.Count.ToString(CultureInfo.InvariantCulture)} / 50";
    }

    private void AddCommunicationLog(DateTimeOffset at, string direction, string data, string description)
    {
        _communicationLogRows.Insert(0, new CommunicationLogRow(at, direction, FormatLogData(data), description));
        while (_communicationLogRows.Count > 50)
        {
            _communicationLogRows.RemoveAt(_communicationLogRows.Count - 1);
        }
    }

    private void AddTimeoutLogEntries(IReadOnlyList<RfidStationPollingStatus> statuses)
    {
        foreach (var status in statuses)
        {
            var statusKey = GetStatusLogKey(status);
            if (!_timeoutLogCounts.TryGetValue(statusKey, out var previousCount))
            {
                _timeoutLogCounts[statusKey] = status.TimeoutCount;
                continue;
            }

            if (status.TimeoutCount <= previousCount)
            {
                continue;
            }

            var newTimeoutCount = status.TimeoutCount - previousCount;
            for (var index = 0; index < newTimeoutCount; index++)
            {
                AddCommunicationLog(
                    DateTimeOffset.Now,
                    "--",
                    "--",
                    $"{FormatStationName(status)}响应超时");
            }
        }
    }

    private string GetStatusLogKey(RfidStationPollingStatus status) =>
        status.EndpointKey?.ToString()
        ?? (!string.IsNullOrWhiteSpace(status.StationId) ? status.StationId : "unknown");

    private string FormatStationName(RfidStationPollingStatus status)
    {
        var station = _stations.FirstOrDefault(item => MatchesStation(status, item));
        return station is null ? "设备" : FormatStation(station);
    }

    private static string FormatLogData(string value) =>
        value.Length <= 42 ? value : $"{value.Substring(0, 42)} …";

    private void UpdateCurrentFrameDisplay(
        RfidUdpDatagramEventArgs datagram,
        RfidStationFrame? parsedFrame,
        StationRecognitionSession? recognitionSession)
    {
        _frameRfidSlots.Clear();

        if (parsedFrame is null)
        {
            FrameAddressText.Text = "-";
            FrameModeText.Text = "-";
            FrameLengthText.Text = "-";
            FrameCountText.Text = "-";
            FrameCrcText.Text = "-";
            FrameParseResultText.Text = datagram.IsValid ? "待解析" : "帧无效";
            FrameParseResultText.Foreground = datagram.IsValid
                ? (Brush)FindResource("WarningBrush")
                : (Brush)FindResource("AlarmBrush");
            FrameDescriptionText.Text = datagram.ValidationError ?? "当前报文未解析为 RFID 数据帧";
            return;
        }

        FrameAddressText.Text = $"0x{parsedFrame.StationAddress:X2}";
        FrameModeText.Text = $"0x{parsedFrame.Mode:X2}";
        FrameLengthText.Text = $"{(parsedFrame.RawData.Length > 0 ? parsedFrame.RawData.Length : datagram.Data.Length)} 字节";
        FrameCountText.Text = parsedFrame.ActualNonZeroSlotCount.ToString(CultureInfo.InvariantCulture);
        FrameCrcText.Text = $"{parsedFrame.CrcHigh:X2} {parsedFrame.CrcLow:X2}";
        FrameParseResultText.Text = parsedFrame.ProtocolDataWarning ? "数据告警" : "解析成功";
        FrameParseResultText.Foreground = parsedFrame.ProtocolDataWarning
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SuccessBrush");
        FrameDescriptionText.Text = recognitionSession is null
            ? $"读取到 {parsedFrame.ActualNonZeroSlotCount} 个 RFID 标签"
            : $"当前列车：{recognitionSession.DetectedVehicleCount} / {recognitionSession.ExpectedVehicleCount}";

        foreach (var (value, index) in parsedFrame.RawRfidSlots.Select((value, index) => (value, index)))
        {
            _frameRfidSlots.Add($"RFID{index + 1:00}  {(value == 0 ? "----" : value.ToString("X4", CultureInfo.InvariantCulture))}");
        }
    }

    private void ResetCurrentFrameDisplay()
    {
        _frameRfidSlots.Clear();
        FrameAddressText.Text = "-";
        FrameModeText.Text = "-";
        FrameLengthText.Text = "-";
        FrameCountText.Text = "-";
        FrameCrcText.Text = "-";
        FrameParseResultText.Text = "-";
        FrameParseResultText.Foreground = (Brush)FindResource("SuccessBrush");
        FrameDescriptionText.Text = "暂无报文";
    }

    private static string FormatStation(RfidStationConfig station) =>
        string.IsNullOrWhiteSpace(station.Name)
            ? RfidStationIdentity.GetDisplayId(station.StationId, station.YardId)
            : $"{RfidStationIdentity.GetDisplayId(station.StationId, station.YardId)} · {station.Name}";

    private static string FormatEndpoint(IPEndPoint endpoint) => $"{endpoint.Address}:{endpoint.Port}";

    private static string FormatYard(string? yardId) =>
        string.IsNullOrWhiteSpace(yardId) ? string.Empty : $"[{yardId!.Trim()}] ";

    private static bool AreScopesEqual(HashSet<string>? left, HashSet<string>? right) =>
        left is null ? right is null : right is not null && left.SetEquals(right);

    private static string FormatTime(DateTimeOffset? value) =>
        value.HasValue ? value.Value.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) : "-";

    private static string FormatOfflineDuration(RfidStationPollingStatus? status)
    {
        if (status is null || status.IsOnline || !status.LastReceivedAt.HasValue)
        {
            return string.Empty;
        }

        var elapsed = DateTimeOffset.Now - status.LastReceivedAt.Value.ToLocalTime();
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return elapsed.TotalMinutes >= 1
            ? $"已离线：{(int)elapsed.TotalMinutes}分{elapsed.Seconds:00}秒"
            : $"已离线：{Math.Max(0, (int)elapsed.TotalSeconds)}秒";
    }

    private sealed class StationStatusRow
    {
        public StationStatusRow(RfidStationConfig station, RfidStationPollingStatus? status, int rowNumber, long invalidFrameCount)
        {
            Station = station;
            Status = status;
            StationId = station.StationId;
            RowNumberText = rowNumber.ToString(CultureInfo.InvariantCulture);
            StationText = FormatStation(station);
            EndpointText = station.TryResolveEndpoint(out var endpoint) ? FormatEndpoint(endpoint) : "端点无效";
            IpText = station.TryResolveEndpoint(out endpoint) ? endpoint.Address.ToString() : "端点无效";
            PortText = station.TryResolveEndpoint(out endpoint)
                ? endpoint.Port.ToString(CultureInfo.InvariantCulture)
                : "-";
            ProtocolAddressText = station.ProtocolAddress.ToString("X2", CultureInfo.InvariantCulture);
            LastSentText = FormatTime(status?.LastSentAt);
            LastReceivedText = FormatTime(status?.LastReceivedAt);
            ResponseMillisecondsText = status?.LastResponseMilliseconds is long milliseconds
                ? $"{milliseconds} ms"
                : "-";
            SentCountText = (status?.SentCount ?? 0).ToString(CultureInfo.InvariantCulture);
            ReceivedCountText = (status?.ReceivedCount ?? 0).ToString(CultureInfo.InvariantCulture);
            TimeoutCountText = (status?.TimeoutCount ?? 0).ToString(CultureInfo.InvariantCulture);
            ConsecutiveTimeoutCountText = (status?.ConsecutiveTimeoutCount ?? 0).ToString(CultureInfo.InvariantCulture);
            InvalidFrameCountText = invalidFrameCount.ToString(CultureInfo.InvariantCulture);
            LastErrorText = status?.LastError ?? "-";
            StatusText = FormatStatus(status);
            StatusBrush = GetStatusBrush(status);
        }

        public RfidStationConfig Station { get; }
        public RfidStationPollingStatus? Status { get; }
        public string StationId { get; }
        public string RowNumberText { get; }
        public string StationText { get; }
        public string EndpointText { get; }
        public string IpText { get; }
        public string PortText { get; }
        public string ProtocolAddressText { get; }
        public string LastSentText { get; }
        public string LastReceivedText { get; }
        public string ResponseMillisecondsText { get; }
        public string SentCountText { get; }
        public string ReceivedCountText { get; }
        public string TimeoutCountText { get; }
        public string ConsecutiveTimeoutCountText { get; }
        public string InvalidFrameCountText { get; }
        public string LastErrorText { get; }
        public string StatusText { get; }
        public Brush StatusBrush { get; }
    }

    private sealed class CommunicationLogRow
    {
        public CommunicationLogRow(DateTimeOffset at, string direction, string data, string description)
        {
            TimeText = FormatTime(at);
            DirectionText = direction;
            DataText = data;
            DescriptionText = description;
            DirectionBrush = direction == "TX"
                ? (Brush)Application.Current.FindResource("SuccessBrush")
                : (Brush)Application.Current.FindResource("AccentBrush");
            DescriptionBrush = description.IndexOf("超时", StringComparison.Ordinal) >= 0 ||
                description.IndexOf("无效", StringComparison.Ordinal) >= 0 ||
                description.IndexOf("告警", StringComparison.Ordinal) >= 0
                ? (Brush)Application.Current.FindResource("AlarmBrush")
                : (Brush)Application.Current.FindResource("TextSecondaryBrush");
        }

        public string TimeText { get; }
        public string DirectionText { get; }
        public string DataText { get; }
        public string DescriptionText { get; }
        public Brush DirectionBrush { get; }
        public Brush DescriptionBrush { get; }
    }

    private static string FormatStatus(RfidStationPollingStatus? status) =>
        status is null || !status.LastSentAt.HasValue
            ? "等待"
            : status.IsOnline ? "在线" : "离线";

    private static Brush GetStatusBrush(RfidStationPollingStatus? status) =>
        status?.LastSentAt.HasValue != true
            ? (Brush)Application.Current.FindResource("TextSecondaryBrush")
            : status.IsOnline
                ? (Brush)Application.Current.FindResource("SuccessBrush")
                : (Brush)Application.Current.FindResource("AlarmBrush");
}
