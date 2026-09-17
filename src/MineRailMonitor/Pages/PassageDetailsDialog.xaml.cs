using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Pages;

public partial class PassageDetailsDialog : Window
{
    public PassageDetailsDialog(PassageRecord record, string stationText, bool alarmMode)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        InitializeComponent();
        LoadRecord(record, stationText, alarmMode);
    }

    private void LoadRecord(PassageRecord record, string stationText, bool alarmMode)
    {
        DialogTitleText.Text = alarmMode ? "告警详情" : "记录详情";
        HeaderAccent.Background = alarmMode ? FindResource("AlarmBrush") as System.Windows.Media.Brush : FindResource("PrimaryBlueBrush") as System.Windows.Media.Brush;

        PassageIdValue.Text = record.PassageId.ToString("D");
        StationValue.Text = stationText;
        HeadRfidValue.Text = FormatRfid(record.HeadRfid);
        StartedAtValue.Text = FormatTime(record.StartedAt);
        CompletedAtValue.Text = FormatTime(record.CompletedAt);
        ExpectedCountValue.Text = record.ExpectedVehicleCount.ToString(CultureInfo.InvariantCulture);
        DetectedCountValue.Text = record.DetectedVehicleCount.ToString(CultureInfo.InvariantCulture);
        OutcomeValue.Text = FormatOutcome(record.Outcome);
        ClearStateValue.Text = FormatClearState(record.ClearState);
        AlarmValue.Text = record.AlarmMessage ?? "-";
        WarningValue.Text = record.WarningMessages.Count == 0 ? "-" : string.Join("；", record.WarningMessages);

        var rows = record.RfidObservations.Select(item => new RfidDetailRow(item)).ToArray();
        RfidDetailsGrid.ItemsSource = rows;
        RfidCountText.Text = $"共 {rows.Length} 条，按首次出现顺序";
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private static string FormatTime(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private static string FormatRfid(ushort? value) => value.HasValue ? value.Value.ToString("X4") : "-";

    private static string FormatOutcome(PassageOutcome value) => value == PassageOutcome.Completed ? "正常通过" : "脱节报警";

    private static string FormatClearState(PassageClearState value) => value == PassageClearState.Cleared ? "已清除" : "待清除";

    private sealed class RfidDetailRow
    {
        public RfidDetailRow(PassageRfidObservation observation)
        {
            SequenceNo = observation.SequenceNo;
            RfidText = observation.RfidValue.ToString("X4");
            FirstSeenAtText = FormatTime(observation.FirstSeenAt);
            BatchNo = observation.BatchNo;
        }

        public int SequenceNo { get; }
        public string RfidText { get; }
        public string FirstSeenAtText { get; }
        public int BatchNo { get; }
    }
}
