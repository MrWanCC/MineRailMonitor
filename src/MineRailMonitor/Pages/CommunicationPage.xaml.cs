using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Recognition;

namespace MineRailMonitor.Pages;

public partial class CommunicationPage : UserControl
{
    private readonly ObservableCollection<string> _packetLog = new();
    private IReadOnlyList<RfidStationConfig> _stations = Array.Empty<RfidStationConfig>();
    private IReadOnlyList<RfidStationPollingStatus> _stationStatuses = Array.Empty<RfidStationPollingStatus>();
    private Func<RfidStationConfig, RfidPollCommand, Task>? _sendTestAsync;

    public CommunicationPage()
    {
        InitializeComponent();
        RxListBox.ItemsSource = _packetLog;
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
        TestStationSelector.Items.Clear();
        foreach (var station in _stations)
        {
            TestStationSelector.Items.Add(new ComboBoxItem
            {
                Content = FormatStation(station),
                Tag = station
            });
        }

        TestStationSelector.SelectedIndex = _stations.Count == 0 ? -1 : 0;
        RefreshStationRows();
    }

    public void SetStationStatuses(IEnumerable<RfidStationPollingStatus> statuses)
    {
        if (statuses is null) throw new ArgumentNullException(nameof(statuses));

        _stationStatuses = statuses.Where(status => status is not null).ToArray();
        RefreshStationRows();
    }

    public void SetListenerStatus(string status) => ListenerStatusText.Text = status;

    public void AddDatagram(
        RfidUdpDatagramEventArgs datagram,
        RfidStationFrame? parsedFrame = null,
        StationRecognitionSession? recognitionSession = null)
    {
        var hex = BitConverter.ToString(datagram.Data).Replace('-', ' ');
        var lines = new List<string>
        {
            $"RX {datagram.ReceivedAt:HH:mm:ss.fff}    {FormatEndpoint(datagram.RemoteEndPoint)}    {datagram.Data.Length} Bytes    {(datagram.IsValid ? "合法帧" : "非法帧")}",
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
    }

    public void ClearLog()
    {
        _packetLog.Clear();
        LatestRxText.Text = "暂无报文";
    }

    public void SetError(Exception exception) =>
        SetListenerStatus($"监听异常：{exception.Message}");

    private async void OnReadTestClick(object sender, RoutedEventArgs e) => await SendSelectedAsync(RfidPollCommand.Read);

    private async void OnClearTestClick(object sender, RoutedEventArgs e) => await SendSelectedAsync(RfidPollCommand.Clear);

    private void OnClearLogClick(object sender, RoutedEventArgs e) => ClearLog();

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

        try
        {
            await _sendTestAsync(station, command);
            var commandText = command == RfidPollCommand.Read ? "读取" : "清空";
            AddLog($"TX {DateTime.Now:HH:mm:ss.fff}    {FormatStation(station)}    {commandText} 请求已发送");
            SetListenerStatus($"测试报文已发送：{FormatStation(station)}");
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    private void RefreshStationRows()
    {
        var rows = _stations.Select(station =>
        {
            var status = _stationStatuses.FirstOrDefault(item => MatchesStation(item, station));
            return new StationStatusRow(station, status);
        }).ToArray();

        StationStatusGrid.ItemsSource = rows;
        NoStationText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool MatchesStation(RfidStationPollingStatus status, RfidStationConfig station)
    {
        if (!string.IsNullOrWhiteSpace(status.StationId))
        {
            return string.Equals(status.StationId, station.StationId, StringComparison.OrdinalIgnoreCase);
        }

        if (!status.EndpointKey.HasValue || !station.TryResolveEndpoint(out var endpoint))
        {
            return false;
        }

        return status.EndpointKey.Value == new RfidStationEndpointKey(endpoint, station.ProtocolAddress);
    }

    private void AddLog(string line)
    {
        LatestRxText.Text = line;
        _packetLog.Insert(0, line);
        while (_packetLog.Count > 50)
        {
            _packetLog.RemoveAt(_packetLog.Count - 1);
        }
    }

    private static string FormatStation(RfidStationConfig station) =>
        string.IsNullOrWhiteSpace(station.Name)
            ? station.StationId
            : $"{station.StationId} · {station.Name}";

    private static string FormatEndpoint(IPEndPoint endpoint) => $"{endpoint.Address}:{endpoint.Port}";

    private static string FormatTime(DateTimeOffset? value) =>
        value.HasValue ? value.Value.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) : "-";

    private sealed class StationStatusRow
    {
        public StationStatusRow(RfidStationConfig station, RfidStationPollingStatus? status)
        {
            StationText = FormatStation(station);
            EndpointText = station.TryResolveEndpoint(out var endpoint) ? FormatEndpoint(endpoint) : "端点无效";
            ProtocolAddressText = station.ProtocolAddress.ToString("X2", CultureInfo.InvariantCulture);
            LastRequestText = FormatTime(status?.LastRequestAt);
            LastResponseText = FormatTime(status?.LastResponseAt);
            StatusText = status?.LastError is not null
                ? "异常"
                : status?.LastResponseAt.HasValue == true ? "在线" : "等待";
        }

        public string StationText { get; }
        public string EndpointText { get; }
        public string ProtocolAddressText { get; }
        public string LastRequestText { get; }
        public string LastResponseText { get; }
        public string StatusText { get; }
    }
}
