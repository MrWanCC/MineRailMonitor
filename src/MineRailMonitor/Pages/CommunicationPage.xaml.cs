using System.Collections.ObjectModel;
using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Recognition;

namespace MineRailMonitor.Pages;

public partial class CommunicationPage : System.Windows.Controls.UserControl
{
    private readonly ObservableCollection<string> _receivedItems = new();

    public CommunicationPage()
    {
        InitializeComponent();
        RxListBox.ItemsSource = _receivedItems;
        SetListenerStatus("监听尚未启动");
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
            $"{datagram.ReceivedAt:HH:mm:ss.fff}    {FormatEndpoint(datagram.RemoteEndPoint)}    {datagram.Data.Length} Bytes    {(datagram.IsValid ? "合法帧" : "非法帧")}",
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
        else if (datagram.IsValid && datagram.Data[3] != 0x04)
        {
            lines.Add("非 RFID 模式");
        }
        else if (!datagram.IsValid && datagram.ValidationError is not null)
        {
            lines.Add(datagram.ValidationError);
        }

        var line = string.Join(Environment.NewLine, lines);

        LatestRxText.Text = line;
        _receivedItems.Insert(0, line);
        while (_receivedItems.Count > 50)
        {
            _receivedItems.RemoveAt(_receivedItems.Count - 1);
        }
    }

    public void SetError(Exception exception) =>
        SetListenerStatus($"监听异常：{exception.Message}");

    private static string FormatEndpoint(IPEndPoint endpoint) =>
        $"{endpoint.Address}:{endpoint.Port}";
}
